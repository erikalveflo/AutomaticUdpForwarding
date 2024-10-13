using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpExample
{
	internal class Program
	{
		const int DEFAULT_PORT = 20777;

		static async Task Main(string[] args)
		{
			Console.WriteLine(
				$"This program simulates a telemetry receiver such as SimHub or Simrig Control Center. It listens " +
				$"for telemetry on UDP port {DEFAULT_PORT} (used by DiRT Rally 2.0). The purpose of the program is " +
				$"to validate an automatic UDP forwarding protocol.");
			Console.WriteLine();
			Console.WriteLine();

			var receiver = new UdpTelemetryReceiver();

			var exitTask = Task.Run(() => Console.ReadKey(true));
			while (!exitTask.IsCompleted)
			{
				Process portOwner = UdpUtils.ProcessBoundToPort(DEFAULT_PORT);
				Console.WriteLine(
					$"Trying to bind telemetry port {DEFAULT_PORT} (bound by '{portOwner?.ProcessName}')");

				if (receiver.TryBind(DEFAULT_PORT))
				{
					Console.WriteLine($"Running as main consumer on port {receiver.Port}");

					var cancel = new CancellationTokenSource();
					var forwarder = new UdpForwarder();
					var accepter = new ForwardingAccepter();

					var receiveTask = receiver.Receive();
					var acceptTask = accepter.WaitForRequest(cancel.Token);
					var removeTask = Task.Run(async () =>
					{
						while (true)
						{
							cancel.Token.ThrowIfCancellationRequested();
							forwarder.RemoveInactiveTargets();
							await Task.Delay(Protocol.REQUEST_INTERVAL, cancel.Token);
						}
					});

					while (!cancel.IsCancellationRequested)
					{
						var done = await Task.WhenAny(receiveTask, acceptTask, removeTask, exitTask);
						if (done == receiveTask)
						{
							var datagram = receiveTask.Result;
							await forwarder.Forward(datagram, datagram.Length);
							await ProcessTelemetry(datagram, datagram.Length);

							receiveTask = receiver.Receive();
						}
						else if (done == acceptTask)
						{
							var request = acceptTask.Result;
							forwarder.AddOrRenewTarget(new IPEndPoint(IPAddress.Loopback, request.Port));

							acceptTask = accepter.WaitForRequest(cancel.Token);
						}
						else if (done == removeTask)
						{
							if (removeTask.Exception != null)
							{
								throw removeTask.Exception;
							}
						}
						else // cancelTask
						{
							cancel.Cancel();
						}
					}
				}
				else
				{
					receiver.BindRandomPort();

					Console.WriteLine($"Running as secondary consumer on port {receiver.Port}");

					var cancel = new CancellationTokenSource();
					var requester = new ForwardingRequester(DEFAULT_PORT, receiver.Port);

					var receiveTask = receiver.Receive();
					var requestTask = requester.RequestForwarding(cancel.Token);

					while (!cancel.IsCancellationRequested)
					{
						var done = await Task.WhenAny(receiveTask, requestTask, exitTask);
						if (done == receiveTask)
						{
							var datagram = receiveTask.Result;
							await ProcessTelemetry(datagram, datagram.Length);

							receiveTask = receiver.Receive();
						}
						else if (done == requestTask)
						{
							var result = requestTask.Result;
							if (result == ForwardingRequester.Result.NoProcessBoundToPort)
							{
								Console.WriteLine("No process bound to telemetry port");
							}

							requestTask = Task.Run(async () =>
							{
								await Task.Delay(Protocol.REQUEST_INTERVAL, cancel.Token);
								return await requester.RequestForwarding(cancel.Token);
							});
						}
						else // cancelTask
						{
							cancel.Cancel();
						}
					}
				}
			}

			Console.WriteLine($"Shutdown");
		}

		static async Task ProcessTelemetry(byte[] datagram, int numBytes)
		{
			// There is where processing of telemetry would occur.
			await Task.Delay(0);
		}
	}

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

	internal class UdpTelemetryReceiver : IDisposable
	{
		private UdpClient _client;

		public int Port => ((IPEndPoint)_client?.Client?.LocalEndPoint)?.Port ?? 0;

		public UdpTelemetryReceiver()
		{
			_client = null;
		}

		public void Dispose()
		{
			_client?.Dispose();
		}

		public bool TryBind(int port)
		{
			if (_client != null && _client.Client.IsBound)
			{
				_client.Dispose();
				_client = null;
			}

			if (_client == null)
			{
				_client = new UdpClient();
				UdpUtils.IgnoreDisconnects(_client);
			}

			try
			{
				_client.Client.Bind(new IPEndPoint(IPAddress.Any, port));
				return true;
			}
			catch (SocketException)
			{
				return false;
			}
		}

		public void BindRandomPort()
		{
			TryBind(0);
		}

		public async Task<byte[]> Receive()
		{
			var result = await _client.ReceiveAsync();
			return result.Buffer;
		}
	}

	internal class UdpForwarder : IDisposable
	{
		private class ForwardingTarget
		{
			public IPEndPoint RemoteEp;
			public DateTime LastSeen;
		}

		private List<ForwardingTarget> _targets;
		private UdpClient _client;

		public UdpForwarder()
		{
			_targets = new List<ForwardingTarget>();
			_client = new UdpClient(new IPEndPoint(IPAddress.Any, 0)); // Any port will do
			UdpUtils.IgnoreDisconnects(_client);
		}

		public void Dispose()
		{
			_client?.Dispose();
		}

		public async Task Forward(byte[] datagram, int numBytes)
		{
			foreach (var target in _targets)
			{
				await _client.SendAsync(datagram, numBytes, target.RemoteEp);
			}
		}

		public void AddOrRenewTarget(IPEndPoint targetEp)
		{
			var targetName = UdpUtils.ProcessNameBoundToPort(targetEp.Port);

			var target = _targets.FirstOrDefault(x => x.RemoteEp.Equals(targetEp));
			if (target == null)
			{
				Console.WriteLine($"Forwarding enabled for '{targetName}' {targetEp}");
				_targets.Add(new ForwardingTarget
				{
					RemoteEp = targetEp,
					LastSeen = DateTime.Now
				});
			}
			else
			{
				target.LastSeen = DateTime.Now;
			}
		}

		public void RemoveInactiveTargets()
		{
			_targets.RemoveAll(x => DateTime.Now - x.LastSeen > Protocol.REGISTRATION_TIMEOUT);
		}
	}
}
