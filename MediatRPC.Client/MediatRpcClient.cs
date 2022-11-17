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

        /// <summary>
        /// 发送和接收消息包
        /// </summary>
        /// <param name="rpcRequestPackage"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task<MediatRpcResponsePackage> SendAndReceiveMessagePackge(MediatRpcRequestPackage rpcRequestPackage, CancellationToken cancellationToken = default)
        {
            // 打开一个出站的双向流
            var stream = await ClientQuicConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);
            var reader = PipeReader.Create(stream);
            var writer = PipeWriter.Create(stream);
            //准备接收数据
            var processTask = ProcessLinesAsync(reader, writer);
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
            await writer.WriteAsync(bytesToSend);
            Console.WriteLine($"Request Package -> " + JsonSerializer.Serialize(rpcRequestPackage));
            //等待网络返回消息包
            MediatRpcResponsePackage rpcResponsePackage = await processTask.WaitAsync(new TimeSpan(0, 0, 30));
            Console.WriteLine($"Response Package -> " + JsonSerializer.Serialize(rpcResponsePackage));
            return rpcResponsePackage;
        }

        private MediatRpcRequestPackage GenerateRpcRequestPackage(string mediatRMethod,object requestBody)
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
            MediatRpcResponsePackage rpcResponsePackage = await SendAndReceiveMessagePackge(rpcRequestPackage);
            var rpcResponseBody = JsonSerializer.Deserialize<TResponse>(rpcResponsePackage.ResponseBody);

            Console.WriteLine($"Got Response -> " + JsonSerializer.Serialize(rpcResponseBody));
            return rpcResponseBody;
        }

        public async Task<bool> Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"Publishing Notification -> " + JsonSerializer.Serialize(notification as object));
            //创建请求消息包
            MediatRpcRequestPackage rpcRequestPackage = GenerateRpcRequestPackage("Publish", notification);
            MediatRpcResponsePackage rpcResponsePackage = await SendAndReceiveMessagePackge(rpcRequestPackage);
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
