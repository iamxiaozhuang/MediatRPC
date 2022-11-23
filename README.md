# MediatRPC
 MediatRPC is a MediatR-based RPC (Remote Procedure Calls) example. 
 
 It takes advantage of MediatR's message-oriented programming philosophy and supports sending and receiving MediatR contract messages in a QUIC connection.
 In other words, it attaches the ability to transmit messages over the network to MediatR.
 
## Architecture diagram:
![Architecture diagram of MediatRPC](https://github.com/iamxiaozhuang/OnionArch/blob/master/Docs/Images/OnionArch.png)

On the client side, it establishes a QUIC connection with the server side. The MediatR's standard methods like ''send/publish/createstream'' are implemented to open outbound bidirectional stream used to send and receive MediatR contract messages.
On the server side, it sets up a QuicListener to listen for client connections and accept the inbound stream when the client make the call after connected. It reads the request message from the stream and passes it to the MediatR handlers for processing, Then it writes the response message to the stream after processed.

## Run the solution
Output from the server site console:
![解决方案](https://github.com/iamxiaozhuang/OnionArch/blob/master/Docs/Images/solution.png)

Output from the client site console:
![解决方案](https://github.com/iamxiaozhuang/OnionArch/blob/master/Docs/Images/solution.png)



## Technologies
* .NET Core 7.0
* MediatR 11.0.0
* System.Net.Quic
