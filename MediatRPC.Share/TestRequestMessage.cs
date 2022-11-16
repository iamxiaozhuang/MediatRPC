using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MediatRPC.Share
{
    public class TestRequestMessage : IRequest<TestResponseMessage>
    {
        public string Message { get; set; }
    }
    public class TestResponseMessage
    {
        public string Message { get; set; }
    }
}
