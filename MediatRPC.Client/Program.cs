using System.Net.Quic;
using System.Net.Security;
using System.Net;
using System.Text;
using System.IO.Pipelines;
using System.Buffers;
using System.Reflection.PortableExecutable;
using System.IO;
using MediatRPC.Client;
using MediatRPC.Share;
using System.Text.Json;

Console.WriteLine("MediatRPC Client Running...");
Console.WriteLine();

MediatRpcClient mediatRpcClient = await MediatRpcClient.Build();

var responseMessage = await mediatRpcClient.Send<TestResponseMessage>(new TestRequestMessage() { Message = "Hello MediatRPC 1" });
Console.WriteLine(JsonSerializer.Serialize(responseMessage));

Console.WriteLine();

responseMessage = await mediatRpcClient.Send<TestResponseMessage>(new TestRequestMessage() { Message = "Hello MediatRPC 2" });
Console.WriteLine(JsonSerializer.Serialize(responseMessage));


Console.ReadKey();


