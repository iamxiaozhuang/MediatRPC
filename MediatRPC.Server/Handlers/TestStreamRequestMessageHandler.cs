using MediatR;
using MediatRPC.Share;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MediatRPC.Server.Handlers
{
    public class TestStreamRequestMessageHandler : IStreamRequestHandler<TestStreamRequestMessage, TestStreamResponseMessage>
    {

        public TestStreamRequestMessageHandler()
        {
        }

        public async IAsyncEnumerable<TestStreamResponseMessage> Handle(TestStreamRequestMessage request, CancellationToken cancellationToken)
        {
            yield return await Task.Run(() => new TestStreamResponseMessage { Message = request.Message + " do" });
            yield return await Task.Run(() => new TestStreamResponseMessage { Message = request.Message + " re" });
            yield return await Task.Run(() => new TestStreamResponseMessage { Message = request.Message + " mi" });
            yield return await Task.Run(() => new TestStreamResponseMessage { Message = request.Message + " fa" });
            yield return await Task.Run(() => new TestStreamResponseMessage { Message = request.Message + " so" });
            yield return await Task.Run(() => new TestStreamResponseMessage { Message = request.Message + " la" });
            yield return await Task.Run(() => new TestStreamResponseMessage { Message = request.Message + " ti" });
        }
    }
}
