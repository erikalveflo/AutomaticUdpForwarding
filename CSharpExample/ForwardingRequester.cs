using System;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static CSharpExample.Protocol;

namespace CSharpExample
{
	internal class ForwardingRequester
	{
		private int _telemetryPort;
		private int _listeningPort;
		private byte[] _readBuffer;

		public ForwardingRequester(int telemetryPort, int listeningPort)
		{
			_telemetryPort = telemetryPort;
			_listeningPort = listeningPort;
			_readBuffer = new byte[Marshal.SizeOf<RequestAndReponse>()];
		}

		public enum Result
		{
			Error,
			Success,
			NoProcessBoundToPort,
		}

		public async Task<Result> RequestForwardingAsync(CancellationToken ct)
		{
			try
			{
				var portOwner = UdpUtils.ProcessBoundToPort(_telemetryPort);
				if (portOwner == null)
				{
					return Result.NoProcessBoundToPort;
				}

				string pipeName = GetPipeName(portOwner);
				Console.WriteLine($"Connecting to pipe: {pipeName}");

				using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
					PipeOptions.Asynchronous))
				{
					var connectTask = pipe.ConnectAsync(ct);
					await TaskUtils.WithTimeout(connectTask, PIPE_TIMEOUT);

					Console.WriteLine($"Requesting UDP forwarding to: {_listeningPort}");
					{
						var request = UdpUtils.StructToBytes(new RequestAndReponse
						{
							Magic = REQUEST_MAGIC,
							Port = _listeningPort,
						});
						var writeTask = pipe.WriteAsync(request, 0, request.Length, ct);
						await TaskUtils.WithTimeout(writeTask, PIPE_TIMEOUT);
						await pipe.FlushAsync(ct);
					}

					Console.WriteLine($"Checking response");
					{
						var readTask = pipe.ReadAsync(_readBuffer, 0, _readBuffer.Length, ct);
						int read = await TaskUtils.WithTimeout(readTask, PIPE_TIMEOUT);
						if (read != Marshal.SizeOf<RequestAndReponse>())
						{
							throw new Exception("Invalid response: Packet size mismatch.");
						}

						var response = UdpUtils.BytesToStruct<RequestAndReponse>(_readBuffer);
						if (response.Magic != RESPONSE_MAGIC)
						{
							throw new Exception("Invalid response: Magic value mismatch.");
						}

						if (response.Port != _listeningPort)
						{
							throw new Exception("Invalid response: Port number mismatch.");
						}
					}
				}

				Console.WriteLine("Success");
				return Result.Success;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to request forwarding: {ex.GetType().Name}: {ex.Message}");
				return Result.Error;
			}
		}

		public async Task<Result> DelayedRequestForwardingAsync(CancellationToken ct)
		{
			await Task.Delay(REQUEST_INTERVAL, ct);
			ct.ThrowIfCancellationRequested();
			return await RequestForwardingAsync(ct);
		}
	}
}
