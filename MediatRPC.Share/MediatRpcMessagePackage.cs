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
        /// <summary>
        /// 要调用的MediatR方法. "Send" or "Publish"
        /// </summary>
        public string MediatRMethod { get; set; }
        /// <summary>
        /// Request Headers
        /// </summary>
        public Dictionary<string,string> RequestHeaders { get; set; }
        /// <summary>
        /// Request Body
        /// </summary>
        public byte[] RequestBody { get; set; }
    }

    public class MediatRpcResponsePackage
    {
        public MediatRpcResponsePackage()
        {
            ResponseHeaders = new Dictionary<string, string>();
        }
        /// <summary>
        /// Response Headers
        /// </summary>
        public Dictionary<string, string> ResponseHeaders { get; set; }
        /// <summary>
        /// Response Body
        /// </summary>
        public byte[] ResponseBody { get; set; }
    }
}
