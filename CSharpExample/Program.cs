using System;
using System.Diagnostics;
using System.Net;
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
}
