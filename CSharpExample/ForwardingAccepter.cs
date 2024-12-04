using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static CSharpExample.Protocol;

namespace CSharpExample
{
	internal class ForwardingAccepter
	{
		public ForwardingAccepter()
		{
		}

		public async Task<IPEndPoint> WaitForRequestAsync(CancellationToken ct)
		{
			var pipeName = GetPipeName(Process.GetCurrentProcess());
			var readBuffer = new byte[Marshal.SizeOf<RequestAndReponse>()];

			while (true)
			{
				ct.ThrowIfCancellationRequested();

				Console.WriteLine($"Creating pipe: {pipeName}");
				using (var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
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
						var readTask = pipe.ReadAsync(readBuffer, 0, readBuffer.Length, ct);
						int read = await TaskUtils.WithTimeout(readTask, PIPE_TIMEOUT);
						if (read != Marshal.SizeOf<RequestAndReponse>())
						{
							Console.WriteLine("Invalid request: Packet size mismatch.");
							continue;
						}
					}
					catch (Exception ex) when (
						ex is OperationCanceledException ||
						ex is TaskCanceledException ||
						ex is TimeoutException)
					{
						Console.WriteLine("Invalid request: Timeout while reading.");
						continue;
					}

					var request = UdpUtils.BytesToStruct<RequestAndReponse>(readBuffer);
					if (request.Magic != REQUEST_MAGIC)
					{
						Console.WriteLine("Invalid request: Magic value mismatch.");
						continue;
					}

					if (request.Port < 0 || request.Port > UInt16.MaxValue)
					{
						Console.WriteLine("Invalid request: Invalid port number.");
						continue;
					}

					var targetName = UdpUtils.ProcessNameBoundToPort(request.Port);
					var targetEp = new IPEndPoint(IPAddress.Loopback, request.Port);

					Console.WriteLine($"Forwarding requested by '{targetName}' {targetEp}");

					try
					{
						var response = UdpUtils.StructToBytes(new RequestAndReponse
						{
							Magic = RESPONSE_MAGIC,
							Port = request.Port,
						});
						var writeTask = pipe.WriteAsync(response, 0, response.Length, ct);
						await TaskUtils.WithTimeout(writeTask, PIPE_TIMEOUT);
						await pipe.FlushAsync(ct);
					}
					catch (Exception ex) when (
						ex is OperationCanceledException ||
						ex is TaskCanceledException ||
						ex is TimeoutException)
					{
						Console.WriteLine("Invalid request: Timeout while writing.");
						continue;
					}

					return targetEp;
				}
			}
		}
	}
}
