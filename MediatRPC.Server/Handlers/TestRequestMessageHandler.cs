using MediatR;
using MediatRPC.Share;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MediatRPC.Server.Handlers
{
    public class TestRequestMessageHandler : IRequestHandler<TestRequestMessage, TestResponseMessage>
    {

        public TestRequestMessageHandler()
        {
        }

        public async Task<TestResponseMessage> Handle(TestRequestMessage request, CancellationToken cancellationToken)
        {
            TestResponseMessage testResponseMessage = new TestResponseMessage();
            testResponseMessage.Message = $"ACK:{request.Message},{DateTime.Now.ToString("HH:mm:ss")}";
            return testResponseMessage;
        }
    }
}
