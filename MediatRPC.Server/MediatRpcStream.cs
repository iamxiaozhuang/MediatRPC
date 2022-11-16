using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Quic;
using System.Net.Security;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using MediatRPC.Share;
using System.Buffers;
using System.IO.Pipelines;
using System.Collections.ObjectModel;
using System.Reflection.PortableExecutable;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Runtime.CompilerServices;

namespace MediatRPC.Server
{
    public class MediatRpcStream
    {
        private readonly IMediator _mediator;

        public MediatRpcConnect ParentMediatRpcConnect { get; }

        public MediatRpcStream(IServiceProvider serviceProvider, MediatRpcConnect mediatRpcConnect,QuicStream quicStream)
        {
            ParentMediatRpcConnect = mediatRpcConnect;
            _mediator = serviceProvider.GetRequiredService<IMediator>();

            _ = ProcessLinesAsync(quicStream);
        }


        // 处理流数据
        async Task ProcessLinesAsync(QuicStream quicStream)
        {
            var reader = PipeReader.Create(quicStream);
            var writer = PipeWriter.Create(quicStream);

            while (true)
            {
                ReadResult result = await reader.ReadAsync();
                ReadOnlySequence<byte> buffer = result.Buffer;

                while (TryReadLine(ref buffer, out ReadOnlySequence<byte> line))
                {
                    // Process the line. 
                    await ProcessLine(line, writer);
                }

                // Tell the PipeReader how much of the buffer has been consumed.
                reader.AdvanceTo(buffer.Start, buffer.End);

                // Stop reading if there's no more data coming.
                if (result.IsCompleted)
                {
                    break;
                }
            }
            await writer.CompleteAsync();
            await reader.CompleteAsync();
            Console.WriteLine($"Stream [{quicStream.Id}]: completed");
            ParentMediatRpcConnect.MediatRpcStreams.Remove(quicStream.Id);
        }

        bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
        {
            // Look for a EOL in the buffer.
            SequencePosition? position = buffer.PositionOf((byte)'\n');

            if (position == null)
            {
                line = default;
                return false;
            }

            // Skip the line + the \n.
            line = buffer.Slice(0, position.Value);
            buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
            return true;
        }

        async Task ProcessLine(ReadOnlySequence<byte> buffer, PipeWriter writer)
        {
            MediatRpcRequestPackage rpcRequestPackage = null;
            foreach (var segment in buffer)
            {
                rpcRequestPackage = JsonSerializer.Deserialize<MediatRpcRequestPackage>(segment.Span);
            }
            if (rpcRequestPackage == null)
            {
                throw new Exception("MediatRpcRequestPackage is null");
            }
            Console.WriteLine("Request Package -> " + JsonSerializer.Serialize(rpcRequestPackage));
            var rpcRequestBody = JsonSerializer.Deserialize(rpcRequestPackage.RequestBody, Type.GetType(rpcRequestPackage.RequestHeaders["ContentType"]));
            Console.WriteLine("Request -> " + JsonSerializer.Serialize(rpcRequestBody));

            MediatRpcResponsePackage rpcResponsePackage = new MediatRpcResponsePackage();
            string mediatRMethod = rpcRequestPackage.RequestHeaders["MediatRMethod"];
            if (mediatRMethod == "Send")
            {
                object rpcResponseBody = await _mediator.Send(rpcRequestBody);
                Console.WriteLine("Response -> " + JsonSerializer.Serialize(rpcResponseBody));
                rpcResponsePackage.ResponseHeaders.Add("StatusCode", "200");
                using (MemoryStream ms = new MemoryStream())
                {
                    JsonSerializer.Serialize(ms, rpcResponseBody);
                    rpcResponsePackage.ResponseBody = ms.ToArray();
                };
            }

            if (mediatRMethod == "Publish")
            {
                await _mediator.Publish(rpcRequestBody);
                rpcResponsePackage.ResponseHeaders.Add("StatusCode", "204");
            }

            //消息对象序列化为字节
            byte[] bytesOfResponsePackage;
            using (MemoryStream ms = new MemoryStream())
            {
                JsonSerializer.Serialize(ms, rpcResponsePackage);
                bytesOfResponsePackage = ms.ToArray();
            };
            byte[] bytesOfEOL = Encoding.UTF8.GetBytes("\n");

            //拼接要发送的字节
            byte[] bytesToSend = bytesOfResponsePackage.Concat(bytesOfEOL).ToArray();
            //写入字节到Stream
            await writer.WriteAsync(bytesToSend);
            Console.WriteLine("Response Package -> " + JsonSerializer.Serialize(rpcResponsePackage));
        }
    }
}
