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

var responseMessage1 = await mediatRpcClient.Send(new TestRequestMessage() { Message = "Test Send Message" });
Console.WriteLine(JsonSerializer.Serialize(responseMessage1));

Console.WriteLine();

var responseMessage2 = await mediatRpcClient.Publish(new TestNotificationMessage() { Message = "Test Publish Message" });
Console.WriteLine(responseMessage2);

Console.WriteLine();


await foreach (TestStreamResponseMessage responseMessage in mediatRpcClient.CreateStream(new TestStreamRequestMessage() { Message = "Test Create Stream Message" }))
{
    Console.WriteLine(responseMessage);
}
  

Console.ReadKey();


