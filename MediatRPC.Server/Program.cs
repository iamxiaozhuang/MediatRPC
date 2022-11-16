

using MediatR;
using MediatRPC.Server;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

var serviceProvider = new ServiceCollection()
    .AddMediatR(Assembly.GetExecutingAssembly())
    .BuildServiceProvider();



Console.WriteLine("MediatRPC Server Running...");

await MediatRpcServer.BuildAndRun(serviceProvider);


Console.ReadKey();


