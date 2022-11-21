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
using System.Net.NetworkInformation;
using System.IO;

namespace MediatRPC.Server
{
    public class MediatRpcStream
    {
        private readonly IMediator _mediator;

        private readonly MediatRpcConnect parentMediatRpcConnect;

        private readonly PipeReader streamReader;
        private readonly PipeWriter streamWriter;

        public MediatRpcStream(IServiceProvider serviceProvider, MediatRpcConnect mediatRpcConnect,QuicStream quicStream)
        {
            parentMediatRpcConnect = mediatRpcConnect;
            _mediator = serviceProvider.GetRequiredService<IMediator>();

            streamReader = PipeReader.Create(quicStream);
            streamWriter = PipeWriter.Create(quicStream);
            _ = ProcessStreamAsync(streamReader, streamWriter,() =>
            {
                Console.WriteLine($"Stream [{quicStream.Id}]: completed");
                parentMediatRpcConnect.MediatRpcStreams.Remove(quicStream.Id);
            }
            );
        }

        public void StopStream()
        {
            streamReader.CancelPendingRead();
            streamWriter.CancelPendingFlush();
        }


        // 处理流数据
        async Task ProcessStreamAsync(PipeReader reader, PipeWriter writer, Action streamCompletedCallBack)
        {
         
            try
            {
                while (true)
                {
                    ReadResult readResult = await reader.ReadAsync();
                    ReadOnlySequence<byte> buffer = readResult.Buffer;

                    try
                    {
                        if (readResult.IsCanceled)
                        {
                            break;
                        }
                        if (TryParseLines(ref buffer, out List<MediatRpcRequestPackage> rpcRequestPackages))
                        {
                            if (!await ProcessLines(rpcRequestPackages, writer))
                            {
                                break;
                            }
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
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
            finally
            {
                await reader.CompleteAsync();
                await writer.CompleteAsync();

                streamCompletedCallBack();
            }
        }

        bool TryParseLines(ref ReadOnlySequence<byte> buffer, out List<MediatRpcRequestPackage> rpcRequestPackages)
        {
            List<MediatRpcRequestPackage> result = new List<MediatRpcRequestPackage>();
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
                else
                {
                    var data = new Span<byte>(new byte[line.Length]);
                    line.CopyTo(data);
                    lineBytes = data;
                }
                MediatRpcRequestPackage rpcRequestPackage = JsonSerializer.Deserialize<MediatRpcRequestPackage>(lineBytes);
                result.Add(rpcRequestPackage);

                buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
            };

            rpcRequestPackages = result;
            return rpcRequestPackages.Count != 0;
        }

        async Task<bool> ProcessLines(List<MediatRpcRequestPackage> rpcRequestPackages, PipeWriter writer)
        {
            foreach (MediatRpcRequestPackage rpcRequestPackage in rpcRequestPackages)
            {

                Console.WriteLine("Request Package -> " + JsonSerializer.Serialize(rpcRequestPackage));
                var rpcRequestBody = JsonSerializer.Deserialize(rpcRequestPackage.RequestBody, Type.GetType(rpcRequestPackage.RequestHeaders["ContentType"]));
                Console.WriteLine("Request -> " + JsonSerializer.Serialize(rpcRequestBody));

                string mediatRMethod = rpcRequestPackage.MediatRMethod;
                if (mediatRMethod == "Send")
                {
                    object rpcResponseBody = await _mediator.Send(rpcRequestBody);
                    Console.WriteLine("Response -> " + JsonSerializer.Serialize(rpcResponseBody));
                    MediatRpcResponsePackage rpcResponsePackage = new MediatRpcResponsePackage();
                    rpcResponsePackage.ResponseHeaders.Add("StatusCode", "200");
                    using (MemoryStream ms = new MemoryStream())
                    {
                        JsonSerializer.Serialize(ms, rpcResponseBody);
                        rpcResponsePackage.ResponseBody = ms.ToArray();
                    };

                    if (!await SendResponsePackge(rpcResponsePackage, writer))
                    {
                        return false;
                    }
                }

                if (mediatRMethod == "Publish")
                {
                    await _mediator.Publish(rpcRequestBody);
                    MediatRpcResponsePackage rpcResponsePackage = new MediatRpcResponsePackage();
                    rpcResponsePackage.ResponseHeaders.Add("StatusCode", "204");
                    if (!await SendResponsePackge(rpcResponsePackage, writer))
                    {
                        return false;
                    }
                }

                if (mediatRMethod == "CreateStream")
                {
                    await foreach (object rpcResponseBody in _mediator.CreateStream(rpcRequestBody))
                    {
                        // 根据流式响应的结果进行处理
                        Console.WriteLine("Response -> " + JsonSerializer.Serialize(rpcResponseBody));
                        MediatRpcResponsePackage rpcResponsePackage = new MediatRpcResponsePackage();
                        rpcResponsePackage.ResponseHeaders.Add("StatusCode", "200");
                        using (MemoryStream ms = new MemoryStream())
                        {
                            JsonSerializer.Serialize(ms, rpcResponseBody);
                            rpcResponsePackage.ResponseBody = ms.ToArray();
                        };
                        if (!await SendResponsePackge(rpcResponsePackage, writer))
                        {
                            return false;
                        }
                    }
                }
            }
            //数据写入完成,发送数据完成（分页）标记
            await writer.WriteAsync(Encoding.UTF8.GetBytes("\f"));
            return true;
        }

        async Task<bool> SendResponsePackge(MediatRpcResponsePackage rpcResponsePackage, PipeWriter writer)
        {
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

            Console.WriteLine("Response Package -> " + JsonSerializer.Serialize(rpcResponsePackage));
            FlushResult flushResult = await writer.WriteAsync(bytesToSend);
            if (flushResult.IsCanceled || flushResult.IsCompleted)
            {
                return false;
            }
            return true;
        }
    }
}
