## Protocol for Automatic UDP Forwarding

Some games use UDP for telemetry. This is problematic when they choose to send the telemetry to a single listener. This leads to problems when multiple programs try to consume the telemetry; such as HaptiConnect, SimHub, Simrig Control Center, and others. Currently this problem is solved by configuring a unique port per program and then having each program forward telemetry to the next. This is cumbersome and error prone; not to mention tricky for our customers. We can do better.

I propose a simple protocol for automatically setting up forwarding of UDP packets between our programs. The protocol is based on a single request sent over a [named pipe](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipes). Named pipes are similar to UDP sockets in that they are datagram based, fully duplex, and asynchronous. However, instead of ports, they use names. This has one great advantage used in this protocol. Using careful naming conventions it allows one program to guess the name used by another program.

A *telemetry source* is a game that transmits UDP telemetry packets to a known port. This port is called the *telemetry port*. A *telemetry consumer* is bound to some UDP port in order to receive telemetry.

The telemetry consumer attempts to bind the telemetry port at startup. This succeeds unless the port is already bound by another telemetry consumer. The telemetry consumer that successfully binds to the telemetry port becomes the *main consumer*. All telemetry consumers that fail to bind the telemetry port become *secondary consumers*. 

It is the job of the main consumer to forward telemetry to all secondary consumers. For this purpose it must accept all telemetry packets from the telemetry source. It must forward all UDP packets received on the telemetry port to all registered secondary consumers. 

Secondary consumers are registered with the main consumer by send a requests over a named pipe. The main consumer must create a named pipe server to allow such registrations. The named pipe must be asynchronous and operate in message mode. It must have a name with the following pattern `\\.\pipe\3aa45f85-9c74-41e0-b560-bd6b9e456275\{PID}\{PORT}` where `PID` is the process ID of the main consumer and `PORT` is the telemetry port. The secondary consumer can construct this name since it knowns the telemetry port; and it can find the PID of the process bound to that port.

The main consumer must continuously wait for connections on the named pipe. When a connection is made it must read a request packet. If the request is valid it must enable UDP forwarding for the provided port for one minute. Forwarding is disabled for that port after one minute unless another request is received. The read operation should timeout if no data is read within a timely manner. The main consumer should thereafter close and recreate the pipe to accept new connections.

Secondary consumers must bind a UDP port at random (or through some other means pick an unused port.) This port is used to receive forwarded UDP telemetry. It must then register with the main consumer by creating a named pipe client and issuing a forwarding request for its randomly bound UDP port. Requests are continuously resent every 15 seconds so longs as the telemetry port remains bound by another process. The named pipe client must be asynchronous and operate in message mode.

Secondary consumers should periodically attempt to bind the telemetry port as the main consumer may terminate at any time. If successfully bound, the secondary consumer becomes the main consumer.

The forwarding requests serves the purpose of creating new (and temporary) forwarding rules in the main consumer. Forwarding is temporarily enabled on a per-port basis.

## Pseudo-code

**Main consumer**
1. Create a UDP socket and attempt to bind the telemetry port
	- Become a secondary consumer on failure to bind the telemetry port
1. Create named pipe server based on PID of current process and telemetry port
	- Make sure to create a asynchronous, in, message pipe
1. In parallel
	- Listen for telemetry on the UDP socket
	- Listen for connections on the named pipe
	- Remove old forwarding
1. When a connections is made on the named pipe:
	1. Read a `Request` struct (16 bytes) within 500 ms
	1. Create a new temporary forwarding rule for the UDP port in the request
	1. Close and recreate the named socket server
1. When telemetry is received on the UDP socket:
	1. Process and forward the telemetry
1. Every minute:
	1. Remove forwarding to UDP ports if no `Request` has been seen for that port in the last minute


**Secondary consumers**
1. Create a UDP socket and attempt to bind the telemetry port
	- Become the main consumer if the port is bound successfully
1. Bind a random UDP port
1. In parallel
	- Listen for telemetry on the UDP socket
	- Request forwarding using a named pipe
1. When telemetry is received on the UDP socket:
	- Process the telemetry
1. Every 15 seconds:
	1. Find the PID of the main consumer using [`GetExtendedUdpTable`](https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getextendedudptable)
		- Try to become the main consumer if no process is found
	1. Create a named pipe client based on the PID of the main consumer and telemetry port
		- Make sure to create a asynchronous, out, message pipe
	1. Create a `Request` with our random port and send it over the named pipe

## Protocol definitions

```csharp
internal static class Protocol
{
	// Enable forwarding for one minute unless another request is received.
	public static readonly TimeSpan REGISTRATION_TIMEOUT = TimeSpan.FromMinutes(1);

	// Send requests for forwarding every 15 seconds. Technically, we could wait 59 seconds, but
	// this would lead to unnecessary delays if the main consumer is terminated and another takes
	// its place. We'd rather send too many requests and react quickly to changes in main consumer.
	public static readonly TimeSpan REQUEST_INTERVAL = TimeSpan.FromSeconds(15);

	// Allow only for a short timeout in pipe communications. Pipes are fast. There is no need to 
	// wait for long.
	public static readonly TimeSpan PIPE_TIMEOUT = TimeSpan.FromMilliseconds(500);

	// This random number identifies a request for forwarding. By using a unique ID we can identify
	// forwarding request and potentially expand the protocol in the future.
	public const int REQUEST_MAGIC = 0x3634B30B;

	// This is the request packet sent from secondary consumer to main consumer when requesting UDP
	// forwarding.
	[StructLayout(LayoutKind.Sequential)]
	public struct Request
	{
		// Use `REQUEST_MAGIC` to identify this packet as a request for to enable UDP forwarding.
		[MarshalAs(UnmanagedType.I4)] public int Magic;

		// This is the UDP port bound by the secondary consumer. The main consumer is requested to
		// enable UDP forwarding of telemetry packets to this port.
		[MarshalAs(UnmanagedType.I4)] public int Port;
	}

	// Gets the pipe name used by the main consumer `process` for telemetry port `port`.
	public static string GetPipeName(Process mainConsumer, int telemetryPort)
	{
		return $@"\\.\pipe\3aa45f85-9c74-41e0-b560-bd6b9e456275\{mainConsumer.Id}\{telemetryPort}";
	}
}
```