using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MediatRPC.Share
{
    public class TestNotificationMessage : INotification
    {
        public string Message { get; set; }
    }
}
