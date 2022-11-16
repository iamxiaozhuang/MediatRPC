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

        public async Task<TResponse> Send<TResponse>(object request, CancellationToken cancellationToken = default)
        {
            // 打开一个出站的双向流
            var stream = await ClientQuicConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);
            var reader = PipeReader.Create(stream);
            var writer = PipeWriter.Create(stream);
            //准备接收数据
            var processTask = ProcessLinesAsync<TResponse>(reader, writer);

            byte[] bytesToSend;
            //消息对象序列化为字节
            using (MemoryStream ms = new MemoryStream())
            {
                JsonSerializer.Serialize(ms, request);
                bytesToSend = ms.ToArray();
            };
            //拼接要发送的字节
            bytesToSend = Encoding.UTF8.GetBytes($"{request.GetType().AssemblyQualifiedName}\r").Concat(bytesToSend).Concat(Encoding.UTF8.GetBytes("\n")).ToArray();
            //写入字节
            await writer.WriteAsync(bytesToSend);
            Console.WriteLine("Sent -> " + request);
            //等待网络返回消息
            TResponse response = await processTask.WaitAsync(new TimeSpan(0, 0, 30));
            Console.WriteLine("Recevied -> " + JsonSerializer.Serialize(response));
            return response;
        }

        async Task<TResponse> ProcessLinesAsync<TResponse>(PipeReader reader, PipeWriter writer)
        {
            TResponse result = default;
            ReadResult readResult = await reader.ReadAsync();
            ReadOnlySequence<byte> buffer = readResult.Buffer;

            while (TryReadLine(ref buffer, out ReadOnlySequence<byte> line))
            {
                // 处理行数据
                result = await ProcessLine<TResponse>(line);
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

        async Task<TResponse> ProcessLine<TResponse>(ReadOnlySequence<byte> buffer)
        {
            TResponse result = default;
            foreach (var segment in buffer)
            {
                result = JsonSerializer.Deserialize<TResponse>(segment.Span);
            }
            return result;
        }

    }
}
