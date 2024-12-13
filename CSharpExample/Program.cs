//
// Copyright (c) 2024 Erik Alveflo
//
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpExample
{
	internal class Program
	{
		static async Task Main(string[] args)
		{
			// We are pretending to receive telemetry from one of the F1 games (or DiRT Rally 2.0)
			const int TELEMETRY_PORT = 20777;

			Console.WriteLine(
				$"This program simulates a telemetry receiver such as SimHub or Simrig Control Center. It listens " +
				$"for telemetry on UDP port {TELEMETRY_PORT} (used by F1 and DiRT Rally 2.0). The purpose of the " +
				$"program is to validate an automatic UDP forwarding protocol.");
			Console.WriteLine();
			Console.WriteLine();

			var exitTask = Task.Run(() => Console.ReadKey(true));
			while (!exitTask.IsCompleted)
			{
				Process portOwner = UdpUtils.GetProcessBoundToPort(TELEMETRY_PORT);
				Console.WriteLine($"Trying to bind telemetry port {TELEMETRY_PORT}");

				var receiver = new UdpTelemetryReceiver();
				if (receiver.TryBind(TELEMETRY_PORT))
				{
					Console.WriteLine($"Running as main consumer on port {receiver.Port}");

					var cancel = new CancellationTokenSource();
					var forwarder = new UdpForwarder();
					var accepter = new ForwardingRequestAccepter(TELEMETRY_PORT);

					var receiveTask = receiver.ReceiveAsync(cancel.Token);
					var acceptTask = accepter.WaitForRequestAsync(cancel.Token);
					var removeTask = Task.CompletedTask;

					while (!cancel.IsCancellationRequested)
					{
						var done = await Task.WhenAny(receiveTask, acceptTask, removeTask, exitTask);
						if (done == receiveTask)
						{
							var datagram = await receiveTask; // Propagates exception

							await forwarder.Forward(datagram, datagram.Length);
							await ProcessTelemetry(datagram, datagram.Length);

							receiveTask = receiver.ReceiveAsync(cancel.Token);
						}
						else if (done == acceptTask)
						{
							var remoteEp = await acceptTask; // Propagates exception

							forwarder.AddOrRenewTarget(remoteEp);

							acceptTask = accepter.WaitForRequestAsync(cancel.Token);
						}
						else if (done == removeTask)
						{
							await removeTask; // Propagates exception

							removeTask = Task.Run(async () =>
							{
								forwarder.RemoveInactiveTargets();
								await Task.Delay(Protocol.REGISTRATION_TIMEOUT, cancel.Token);
							});
						}
						else if (done == exitTask)
						{
							cancel.Cancel();
						}
						else
						{
							throw new NotImplementedException(); // Unreachable
						}
					}
				}
				else
				{
					Console.WriteLine($"Telemetry port bound by process {portOwner?.Id} '{portOwner?.ProcessName}'");

					receiver.BindRandomPort();

					Console.WriteLine($"Running as secondary consumer on port {receiver.Port}");

					var cancel = new CancellationTokenSource();
					var requester = new ForwardingRequester(TELEMETRY_PORT, receiver.Port);

					var receiveTask = receiver.ReceiveAsync(cancel.Token);
					var requestTask = requester.RequestForwardingAsync(cancel.Token);

					while (!cancel.IsCancellationRequested)
					{
						var done = await Task.WhenAny(receiveTask, requestTask, exitTask);
						if (done == receiveTask)
						{
							var datagram = await receiveTask; // Propagates exception
							await ProcessTelemetry(datagram, datagram.Length);

							receiveTask = receiver.ReceiveAsync(cancel.Token);
						}
						else if (done == requestTask)
						{
							var result = await requestTask; // Propagates exception
							if (result == ForwardingRequester.Result.NoProcessBoundToPort)
							{
								Console.WriteLine("No process bound to telemetry port");

								// This is when we can attempt to become the main consumer by binding the telemetry
								// port.
								cancel.Cancel();
							}

							requestTask = Task.Run(async () =>
							{
								await Task.Delay(Protocol.REQUEST_INTERVAL, cancel.Token);
								return await requester.RequestForwardingAsync(cancel.Token);
							});
						}
						else if (done == exitTask)
						{
							cancel.Cancel();
						}
						else
						{
							throw new NotImplementedException(); // Unreachable
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
