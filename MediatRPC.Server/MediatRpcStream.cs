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

            SequencePosition? position = buffer.PositionOf((byte)'\r');

            var objectTypeBuffer = buffer.Slice(0, position.Value);
            var objectBuffer = buffer.Slice(buffer.GetPosition(1, position.Value));
            string objectTypeStr = string.Empty;
            foreach (var segment in objectTypeBuffer)
            {
                objectTypeStr = System.Text.Encoding.UTF8.GetString(segment.Span);
                Console.WriteLine("Recevied ObjectType -> " + objectTypeStr);
            }

            foreach (var segment in objectBuffer)
            {
                var receviedObj = JsonSerializer.Deserialize(segment.Span, Type.GetType(objectTypeStr));
                Console.WriteLine("Recevied Object -> " + JsonSerializer.Serialize(receviedObj));
                var sendObj = await _mediator.Send(receviedObj);
                byte[] bytesToSend;
                using (MemoryStream ms = new MemoryStream())
                {
                    JsonSerializer.Serialize(ms, sendObj);
                    bytesToSend = ms.ToArray();
                };
                bytesToSend= bytesToSend.Concat(Encoding.UTF8.GetBytes("\n")).ToArray();
                await writer.WriteAsync(bytesToSend);
               Console.WriteLine("Sent Object -> " + JsonSerializer.Serialize(sendObj));
            }
            Console.WriteLine();
        }
    }
}
