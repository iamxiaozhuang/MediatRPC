using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MediatRPC.Share
{
    public class TestStreamRequestMessage : IStreamRequest<TestStreamResponseMessage>
    {
        public string Message { get; set; }
    }
    public class TestStreamResponseMessage
    {
        public string Message { get; set; }
    }

}
