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

namespace MediatRPC.Server
{
    public class MediatRpcConnect
    {
        CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private readonly IServiceProvider _serviceProvider;

        public Dictionary<long,MediatRpcStream> MediatRpcStreams { get; set; }
        public MediatRpcServer ParentMediatRpcServer { get; }

        public MediatRpcConnect(IServiceProvider serviceProvider,MediatRpcServer mediatRpcServer, QuicConnection quicConnection)
        {
            _serviceProvider = serviceProvider;
            ParentMediatRpcServer = mediatRpcServer;

            _ = WaitingForStream(quicConnection);
        }

        public async Task WaitingForStream(QuicConnection quicConnection)
        {
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                var stream = await quicConnection.AcceptInboundStreamAsync();
                Console.WriteLine($"Stream [{stream.Id}]: created");
                MediatRpcStreams = MediatRpcStreams ?? new Dictionary<long, MediatRpcStream>();
                MediatRpcStreams.Add(stream.Id,new MediatRpcStream(_serviceProvider,this, stream));
            }
        }

        public async Task CancelWaitingForStream()
        {
            //处理数据
            cancellationTokenSource.Cancel();
        }

    }
}
