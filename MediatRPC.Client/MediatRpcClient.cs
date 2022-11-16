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

        private async Task<MediatRpcResponsePackage> SendRequestPackge(MediatRpcRequestPackage rpcRequestPackage, CancellationToken cancellationToken = default)
        {
            // 打开一个出站的双向流
            var stream = await ClientQuicConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);
            var reader = PipeReader.Create(stream);
            var writer = PipeWriter.Create(stream);
            //准备接收数据
            var processTask = ProcessLinesAsync(reader, writer);
            //消息对象序列化为字节
            byte[] bytesOfRequestPackage;
            using (MemoryStream ms = new MemoryStream())
            {
                JsonSerializer.Serialize(ms, rpcRequestPackage);
                bytesOfRequestPackage = ms.ToArray();
            };
            byte[] bytesOfEOL = Encoding.UTF8.GetBytes("\n");

            //拼接要发送的字节
            byte[] bytesToSend = bytesOfRequestPackage.Concat(bytesOfEOL).ToArray();
            //写入字节到Stream
            await writer.WriteAsync(bytesToSend);
            Console.WriteLine($"Request Package -> " + JsonSerializer.Serialize(rpcRequestPackage));
            //等待网络返回消息
            MediatRpcResponsePackage rpcResponsePackage = await processTask.WaitAsync(new TimeSpan(0, 0, 30));
            Console.WriteLine($"Response Package -> " + JsonSerializer.Serialize(rpcResponsePackage));
            return rpcResponsePackage;
        }

        public async Task<TResponse> Send<TResponse>(object request, CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"Request -> " + JsonSerializer.Serialize(request));
            //创建请求消息包
            MediatRpcRequestPackage rpcRequestPackage = new MediatRpcRequestPackage();
            rpcRequestPackage.MediatRMethod = "Send";
            rpcRequestPackage.RequestHeaders.Add("ContentType", request.GetType().AssemblyQualifiedName);
            using (MemoryStream ms = new MemoryStream())
            {
                JsonSerializer.Serialize(ms, request);
                rpcRequestPackage.RequestBody = ms.ToArray();
            };
            MediatRpcResponsePackage rpcResponsePackage = await SendRequestPackge(rpcRequestPackage);
            var rpcResponseBody = JsonSerializer.Deserialize<TResponse>(rpcResponsePackage.ResponseBody);

            Console.WriteLine($"Response -> " + JsonSerializer.Serialize(rpcResponseBody));
            return rpcResponseBody;
        }

        public async Task<bool> Publish(object notification, CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"Request -> " + JsonSerializer.Serialize(notification));
            //创建请求消息包
            MediatRpcRequestPackage rpcRequestPackage = new MediatRpcRequestPackage();
            rpcRequestPackage.MediatRMethod =  "Publish";
            rpcRequestPackage.RequestHeaders.Add("ContentType", notification.GetType().AssemblyQualifiedName);
            using (MemoryStream ms = new MemoryStream())
            {
                JsonSerializer.Serialize(ms, notification);
                rpcRequestPackage.RequestBody = ms.ToArray();
            };
            MediatRpcResponsePackage rpcResponsePackage = await SendRequestPackge(rpcRequestPackage);
            if (rpcResponsePackage.ResponseHeaders["StatusCode"] == "204")
            {
                return true;
            }
            return false;
        }

        async Task<MediatRpcResponsePackage> ProcessLinesAsync(PipeReader reader, PipeWriter writer)
        {
            MediatRpcResponsePackage result = default;
            ReadResult readResult = await reader.ReadAsync();
            ReadOnlySequence<byte> buffer = readResult.Buffer;

            if (TryReadLine(ref buffer, out ReadOnlySequence<byte> line))
            {
                // 处理行数据
                result = await ProcessLine(line);
            }
            reader.AdvanceTo(buffer.Start, buffer.End);
            await writer.CompleteAsync();
            await reader.CompleteAsync();
            return result;
        }

        bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
        {
            SequencePosition? position = buffer.PositionOf((byte)'\n');

            if (position == null)
            {
                line = default;
                return false;
            }

            line = buffer.Slice(0, position.Value);
            buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
            return true;
        }

        async Task<MediatRpcResponsePackage> ProcessLine(ReadOnlySequence<byte> buffer)
        {
            MediatRpcResponsePackage result = default;
            foreach (var segment in buffer)
            {
                result = JsonSerializer.Deserialize<MediatRpcResponsePackage>(segment.Span);
            }
            return result;
        }

    }
}
