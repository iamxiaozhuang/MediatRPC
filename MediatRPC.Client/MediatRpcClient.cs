using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Quic;
using System.Net.Security;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using MediatR;
using System.IO.Pipelines;
using System.Buffers;
using MediatRPC.Share;
using System.Text.Json;
using System.IO;

namespace MediatRPC.Client
{
    public class MediatRpcClient
    {
        public QuicConnection ClientQuicConnection { get; set; }

        private MediatRpcClient(QuicConnection quicConnection)
        {
            ClientQuicConnection = quicConnection;
        }

        public static async Task<MediatRpcClient> Build()
        {
            var connection = await QuicConnection.ConnectAsync(new QuicClientConnectionOptions
            {
                DefaultCloseErrorCode = 0,
                DefaultStreamErrorCode = 0,
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 9999),
                ClientAuthenticationOptions = new SslClientAuthenticationOptions
                {
                    ApplicationProtocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http3 },
                    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
                    {
                        return true;
                    }
                }
            });
            return new MediatRpcClient(connection);
        }

        //public async Task<string> SendMessage(string message)
        //{
        //    // 打开一个出站的双向流
        //    var stream = await ClientQuicConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);
        //    var reader = PipeReader.Create(stream);
        //    var writer = PipeWriter.Create(stream);
        //    //处理数据
        //    var processTask = ProcessLinesAsync(reader, writer);
        //    await writer.WriteAsync(Encoding.UTF8.GetBytes(message+ "\n"));
        //    Console.WriteLine("Sent -> " + message);
        //    string result = await processTask.WaitAsync(new TimeSpan(0, 0, 30));
        //    Console.WriteLine("Recevied -> " + result);
        //    return result;
        //}

        /// <summary>
        /// 发送和接收消息包
        /// </summary>
        /// <param name="rpcRequestPackage"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async IAsyncEnumerable<MediatRpcResponsePackage> SendAndReceiveMessagePackge(MediatRpcRequestPackage rpcRequestPackage, CancellationToken cancellationToken = default)
        {
            // 打开一个出站的双向流
            var stream = await ClientQuicConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);
            //处理流数据，发送并等待消息返回
            await foreach (MediatRpcResponsePackage rpcResponsePackage in  ProcessStreamAsync(stream, rpcRequestPackage))
            {
                Console.WriteLine($"Response Package -> " + JsonSerializer.Serialize(rpcResponsePackage));
                yield return rpcResponsePackage;
            }
        }

        private MediatRpcRequestPackage GenerateRpcRequestPackage(string mediatRMethod, object requestBody)
        {
            //创建请求消息包
            MediatRpcRequestPackage rpcRequestPackage = new MediatRpcRequestPackage();
            rpcRequestPackage.MediatRMethod = mediatRMethod;
            rpcRequestPackage.RequestHeaders.Add("ContentType", requestBody.GetType().AssemblyQualifiedName);
            using (MemoryStream ms = new MemoryStream())
            {
                JsonSerializer.Serialize(ms, requestBody);
                rpcRequestPackage.RequestBody = ms.ToArray();
            };
            return rpcRequestPackage;
        }

        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"Sending Request -> " + JsonSerializer.Serialize(request as object));
            //创建请求消息包
            MediatRpcRequestPackage rpcRequestPackage = GenerateRpcRequestPackage("Send", request);
            MediatRpcResponsePackage rpcResponsePackage = default;
            await foreach (MediatRpcResponsePackage package in SendAndReceiveMessagePackge(rpcRequestPackage))
            {
                rpcResponsePackage = package;
            }
            var rpcResponseBody = JsonSerializer.Deserialize<TResponse>(rpcResponsePackage.ResponseBody);

            Console.WriteLine($"Got Response -> " + JsonSerializer.Serialize(rpcResponseBody));
            return rpcResponseBody;
        }

        public async Task<bool> Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"Publishing Notification -> " + JsonSerializer.Serialize(notification as object));
            //创建请求消息包
            MediatRpcRequestPackage rpcRequestPackage = GenerateRpcRequestPackage("Publish", notification);
            MediatRpcResponsePackage rpcResponsePackage = default;
            await foreach (MediatRpcResponsePackage package in SendAndReceiveMessagePackge(rpcRequestPackage))
            {
                rpcResponsePackage = package;
            }
            if (rpcResponsePackage.ResponseHeaders["StatusCode"] == "204")
            {
                return true;
            }
            return false;
        }

        public async IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"Creating Stream Request -> " + JsonSerializer.Serialize(request as object));
            //创建请求消息包
            MediatRpcRequestPackage rpcRequestPackage = GenerateRpcRequestPackage("CreateStream", request);
            await foreach (MediatRpcResponsePackage package in SendAndReceiveMessagePackge(rpcRequestPackage))
            {
                var rpcResponseBody = JsonSerializer.Deserialize<TResponse>(package.ResponseBody);
                Console.WriteLine($"Got Response -> " + JsonSerializer.Serialize(rpcResponseBody));
                yield return rpcResponseBody;
            }
        }

        async IAsyncEnumerable<MediatRpcResponsePackage> ProcessStreamAsync(QuicStream stream, MediatRpcRequestPackage rpcRequestPackage)
        {
            var reader = PipeReader.Create(stream);
            var writer = PipeWriter.Create(stream);
            //发送数据
            FlushResult flushResult = await SendRequestPackge(rpcRequestPackage, writer);

            while (true)
            {
                ReadResult readResult = await reader.ReadAsync();
                ReadOnlySequence<byte> buffer = readResult.Buffer;

                try
                {
                    if (flushResult.IsCanceled || flushResult.IsCompleted)
                    {
                        break;
                    }
                    if (readResult.IsCanceled)
                    {
                        break;
                    }
                    if (TryParseLines(ref buffer, out List<MediatRpcResponsePackage> rpcResponsePackages))
                    {
                        foreach (MediatRpcResponsePackage rpcResponsePackage in rpcResponsePackages)
                        {
                            yield return rpcResponsePackage;
                        }
                    }

                    if (IsEOP(ref buffer))
                    {
                        break;
                    }
                    if (readResult.IsCompleted)
                    {
                        if (!buffer.IsEmpty)
                        {
                            throw new InvalidDataException("Incomplete message.");
                        }
                        break;
                    }
                }
                finally
                {
                    reader.AdvanceTo(buffer.Start, buffer.End);
                }
            }
            await reader.CompleteAsync();
            await writer.CompleteAsync();
        }

        bool TryParseLines(ref ReadOnlySequence<byte> buffer, out List<MediatRpcResponsePackage> rpcResponsePackages)
        {
            List<MediatRpcResponsePackage> result = new List<MediatRpcResponsePackage>();
            SequencePosition? position;
            while (true)
            {
                position = buffer.PositionOf((byte)'\n');
                if (!position.HasValue)
                    break;

                var line = buffer.Slice(buffer.Start, position.Value);
                ReadOnlySpan<byte> lineBytes;
                if (line.IsSingleSegment)
                {
                    lineBytes = line.FirstSpan;
                }
                else if (line.Length < 128)
                {
                    var data = new Span<byte>(new byte[line.Length]);
                    line.CopyTo(data);
                    lineBytes = data;
                }
                else
                {
                    lineBytes = new ReadOnlySpan<byte>(line.ToArray());
                }
                MediatRpcResponsePackage rpcResponsePackage = JsonSerializer.Deserialize<MediatRpcResponsePackage>(lineBytes);
                result.Add(rpcResponsePackage);

                buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
            };

            rpcResponsePackages = result;
            return rpcResponsePackages.Count != 0;
        }

        //是否数据包接收完成（翻页符号）
        bool IsEOP(ref ReadOnlySequence<byte> buffer)
        {
            SequencePosition? position = buffer.PositionOf((byte)'\f');
            if (position.HasValue)
            {
                buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
                return true;
            }
            return false;
        }


        async ValueTask<FlushResult> SendRequestPackge(MediatRpcRequestPackage rpcRequestPackage, PipeWriter writer)
        {
            //消息对象序列化为字节
            //消息包序列化为字节
            byte[] bytesOfRequestPackage;
            using (MemoryStream ms = new MemoryStream())
            {
                JsonSerializer.Serialize(ms, rpcRequestPackage);
                bytesOfRequestPackage = ms.ToArray();
            };
            byte[] bytesOfEOL = Encoding.UTF8.GetBytes("\n");

            //拼接要发送的字节
            byte[] bytesToSend = bytesOfRequestPackage.Concat(bytesOfEOL).ToArray();
            //发送消息字节
            Console.WriteLine($"Request Package -> " + JsonSerializer.Serialize(rpcRequestPackage));
           return await writer.WriteAsync(bytesToSend);
        }

    }
}
