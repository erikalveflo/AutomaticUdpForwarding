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
			_readBuffer = new byte[Marshal.SizeOf<Request>()];
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

				Console.WriteLine($"Requesting that {portOwner.Id} '{portOwner.ProcessName}' forward port " +
					$"{_telemetryPort}");

				string pipeName = GetPipeName(portOwner, _telemetryPort);
				Console.WriteLine($"Connecting to pipe: {pipeName}");

				using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out,
					PipeOptions.Asynchronous))
				{
					var connectTask = pipe.ConnectAsync(ct);
					await TaskUtils.WithTimeout(connectTask, PIPE_TIMEOUT);

					var request = UdpUtils.StructToBytes(new Request
					{
						Magic = REQUEST_MAGIC,
						Port = _listeningPort,
					});
					var writeTask = pipe.WriteAsync(request, 0, request.Length, ct);
					await TaskUtils.WithTimeout(writeTask, PIPE_TIMEOUT);
					await pipe.FlushAsync(ct);
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
