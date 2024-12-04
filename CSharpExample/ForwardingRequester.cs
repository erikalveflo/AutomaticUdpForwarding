using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpExample
{
	internal class ForwardingRequester
	{
		private int _telemetryPort;
		private int _listeningPort;

		public ForwardingRequester(int telemetryPort, int listeningPort)
		{
			_telemetryPort = telemetryPort;
			_listeningPort = listeningPort;
		}

		public enum Result
		{
			Success,
			NoProcessBoundToPort,
			OtherError,
			Timeout,
			Cancelled,
		}

		public async Task<Result> RequestForwarding(CancellationToken ct)
		{
			try
			{
				var portOwner = UdpUtils.ProcessBoundToPort(_telemetryPort);
				if (portOwner == null)
				{
					return Result.NoProcessBoundToPort;
				}

				string pipeName = Protocol.GetPipeName(portOwner);
				Console.WriteLine($"Opening pipe: {pipeName}");

				using (var timeout = new CancellationTokenSource(Protocol.PIPE_TIMEOUT))
				using (var timeoutOrCancel = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, ct))
				using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out,
					PipeOptions.Asynchronous))
				{
					await pipe.ConnectAsync(timeoutOrCancel.Token);

					var request = new Protocol.RequestPacket
					{
						Magic = Protocol.REQUEST_MAGIC,
						Port = _listeningPort,
					};
					var datagram = UdpUtils.StructToBytes(request);

					Console.WriteLine($"Requesting UDP forwarding to: {_listeningPort}");
					await pipe.WriteAsync(datagram, 0, datagram.Length, timeoutOrCancel.Token);
					await pipe.FlushAsync(timeoutOrCancel.Token);
				}

				Console.WriteLine("Success");
				return Result.Success;
			}
			catch (IOException ex)
			{
				Console.WriteLine($"Failed to request forwarding: {ex}");
				return Result.OtherError;
			}
			catch (TimeoutException)
			{
				Console.WriteLine($"Request timed out");
				return Result.Timeout;
			}
			catch (Exception ex) when (ex is OperationCanceledException || ex is TaskCanceledException)
			{
				if (!ct.IsCancellationRequested)
				{
					Console.WriteLine($"Request timed out");
					return Result.Timeout;
				}
				return Result.Cancelled;
			}
		}
	}
}
