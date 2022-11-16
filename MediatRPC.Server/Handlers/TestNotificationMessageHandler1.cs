﻿using MediatR;
using MediatRPC.Share;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MediatRPC.Server.Handlers
{
    public class TestNotificationMessageHandler1 : INotificationHandler<TestNotificationMessage>
    {

        public TestNotificationMessageHandler1()
        {
        }


        public async Task Handle(TestNotificationMessage notification, CancellationToken cancellationToken)
        {
           string message = notification.Message;
        }
    }
}
