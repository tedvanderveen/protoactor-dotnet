﻿using System.Threading.Tasks;

namespace Proto
{
    
    public delegate Task Receive(IContext context);
    
    public delegate Task Receiver(IReceiverContext context, MessageEnvelope envelope);

    public delegate Task Sender(ISenderContext context, PID target, MessageEnvelope envelope);
}