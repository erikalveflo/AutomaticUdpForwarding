## Protocol for Automatic UDP Forwarding

Some games use UDP for telemetry. This is problematic when they choose to send the telemetry to a single listener. This leads to problems when multiple programs try to consume the telemetry; such as HaptiConnect, SimHub, Simrig Control Center, and others. Currently this problem is solved by configuring a unique port per program and then having each program forward telemetry to the next. This is cumbersome and error prone; not to mention tricky for our customers. We can do better.

I propose a simple protocol for automatically setting up forwarding of UDP packets between our programs. The protocol is based on a single request and response with fixed payloads. The request is called the *setup packet* and response is called the *accept packet*. When a setup packet is received, the receiver configures itself to forward all telemetry to the sender's port, and responds with an accept packet.

More formally, a *telemetry source* is a game that transmits UDP telemetry packets to a known port. This port is called the *telemetry port*. A *telemetry consumer* is bound to some UDP port in order to receive telemetry.

The telemetry consumer attempts to bind the telemetry port at startup. This succeeds unless the port is already bound by another telemetry consumer. The telemetry consumer that successfully binds to the telemetry port becomes the *main consumer*. All telemetry consumers that fail to bind the telemetry port become *secondary consumers*.

The main consumer must accept all telemetry packets from the telemetry source. It must also accept setup packets. When a setup packet is received, the main consumer must enable forwarding of all telemetry packets to the setup packet's origin (address and port) and respond with an accept packet. Forwarding is enabled for one minute, after which it is disabled unless another setup packet is received.

Secondary consumers must bind a port at random (or through some other means pick an unused port.) This port is then used to send a setup packet to the main consumer. After which it is used to receive telemetry and send additional setup packets.

Secondary consumers should periodically attempt to bind the telemetry port as the main consumer may terminate at any time. If successfully bound, the secondary consumer becomes the main consumer.

The setup and accept packets are 21 bytes:

```cs
// "UdpForwardingRequest" in bytes
byte[] SETUP_PACKET = [ 0x55, 0x64, 0x70, 0x46, 0x6F, 0x72, 0x77, 0x61, 0x72, 0x64, 0x69, 0x6E, 0x67, 0x52, 0x65, 0x71, 0x75, 0x65, 0x73, 0x74 ];

// "UdpForwardingEnabled" in bytes
byte[] ACCEPT_PACKET = [ 0x55, 0x64, 0x70, 0x46, 0x6F, 0x72, 0x77, 0x61, 0x72, 0x64, 0x69, 0x6E, 0x67, 0x45, 0x6E, 0x61, 0x62, 0x6C, 0x65, 0x64 ];
```

The setup packet serves the purpose of creating new (and temporary) forwarding rules in the main consumer. The accept packet serves as a probe to test if this protocol is supported by the main consumer.
