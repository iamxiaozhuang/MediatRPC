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

namespace MediatRPC.Server
{
    public class MediatRpcServer
    {
        CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        public List<MediatRpcConnect> MediatRpcConnects { get; set; }
        public readonly IServiceProvider _serviceProvider;

        private MediatRpcServer(IServiceProvider serviceProvider, QuicListener quicListener)
        {
            _serviceProvider = serviceProvider;
            _ = WaitingForConnect(quicListener);
        }

        public static async Task<MediatRpcServer> BuildAndRun(IServiceProvider serviceProvider)
        {
            var listener = await QuicListener.ListenAsync(new QuicListenerOptions
            {
                ApplicationProtocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http3 },
                ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 9999),
                ConnectionOptionsCallback = (connection, ssl, token) => ValueTask.FromResult(new QuicServerConnectionOptions()
                {
                    DefaultStreamErrorCode = 0,
                    DefaultCloseErrorCode = 0,
                    ServerAuthenticationOptions = new SslServerAuthenticationOptions()
                    {
                        ApplicationProtocols = new List<SslApplicationProtocol>() { SslApplicationProtocol.Http3 },
                        ServerCertificate = CertificateHelper.GenerateManualCertificate()
                    }
                })
            });
            return new MediatRpcServer(serviceProvider,listener);
        }


        public async Task WaitingForConnect(QuicListener quicListener)
        {
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                var connection = await quicListener.AcceptConnectionAsync();

                Console.WriteLine($"Client [{connection.RemoteEndPoint}]: connected");

                MediatRpcConnects = MediatRpcConnects ?? new List<MediatRpcConnect>();
                MediatRpcConnects.Add(new MediatRpcConnect(_serviceProvider, this, connection));
            }
        }
        public async Task CancelWaitingForConnect()
        {
            //处理数据
            cancellationTokenSource.Cancel();
        }
    }
}
