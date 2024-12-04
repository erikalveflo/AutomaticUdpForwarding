using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpExample
{
	internal class ForwardingAccepter
	{
		public ForwardingAccepter()
		{
		}

		public async Task<Protocol.RequestPacket> WaitForRequest(CancellationToken ct)
		{
			var pipeName = Protocol.GetPipeName(Process.GetCurrentProcess());
			var packet = new byte[Marshal.SizeOf<Protocol.RequestPacket>()];

			while (true)
			{
				ct.ThrowIfCancellationRequested();

				Console.WriteLine($"Opening pipe: {pipeName}");
				using (var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
					PipeTransmissionMode.Message, PipeOptions.Asynchronous))
				{
					try
					{
						await pipe.WaitForConnectionAsync(ct);
						Console.WriteLine("Pipe connected");
					}
					catch (IOException)
					{
						continue;
					}

					try
					{
						var readTask = pipe.ReadAsync(packet, 0, packet.Length, ct);
						int read = await TaskUtils.WithTimeout(readTask, Protocol.PIPE_TIMEOUT);
						if (read != packet.Length)
						{
							Console.WriteLine("Invalid request: Packet size mismatch.");
							continue;
						}
					}
					catch (TimeoutException)
					{
						Console.WriteLine("Invalid request: Timeout.");
						continue;
					}
					catch (Exception ex) when (ex is OperationCanceledException || ex is TaskCanceledException)
					{
						Console.WriteLine("Invalid request: Timeout.");
						continue;
					}

					var request = UdpUtils.BytesToStruct<Protocol.RequestPacket>(packet);
					if (request.Magic != Protocol.REQUEST_MAGIC)
					{
						Console.WriteLine("Invalid request: Magic value mismatch.");
						continue;
					}

					if (request.Port < 0 || request.Port > UInt16.MaxValue)
					{
						Console.WriteLine("Invalid request: Invalid port number.");
						continue;
					}

					Console.WriteLine($"Forwarding requested to: {request.Port}");
					return request;
				}
			}
		}
	}
}
