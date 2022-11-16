using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MediatRPC.Share
{
    public class MediatRpcRequestPackage
    {
        public MediatRpcRequestPackage()
        {
            RequestHeaders = new Dictionary<string, string>();
        }

        public Dictionary<string,string> RequestHeaders { get; set; }
        public byte[] RequestBody { get; set; }
    }

    public class MediatRpcResponsePackage
    {
        public MediatRpcResponsePackage()
        {
            ResponseHeaders = new Dictionary<string, string>();
        }

        public Dictionary<string, string> ResponseHeaders { get; set; }
        public byte[] ResponseBody { get; set; }
    }
}
