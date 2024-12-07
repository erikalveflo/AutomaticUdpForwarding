﻿using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpExample
{
	internal class Program
	{
		static async Task Main(string[] args)
		{
			const int TELEMETRY_PORT = 20777;

			Console.WriteLine(
				$"This program simulates a telemetry receiver such as SimHub or Simrig Control Center. It listens " +
				$"for telemetry on UDP port {TELEMETRY_PORT} (used by DiRT Rally 2.0). The purpose of the program " +
				$"is to validate an automatic UDP forwarding protocol.");
			Console.WriteLine();
			Console.WriteLine();

			var receiver = new UdpTelemetryReceiver();

			var exitTask = Task.Run(() => Console.ReadKey(true));
			while (!exitTask.IsCompleted)
			{
				Process portOwner = UdpUtils.ProcessBoundToPort(TELEMETRY_PORT);
				Console.WriteLine($"Trying to bind telemetry port {TELEMETRY_PORT}");

				if (receiver.TryBind(TELEMETRY_PORT))
				{
					Console.WriteLine($"Running as main consumer on port {receiver.Port}");

					var cancel = new CancellationTokenSource();
					var accepter = new ForwardingAccepter(TELEMETRY_PORT);
					var forwarder = new UdpForwarder();

					var receiveTask = receiver.ReceiveAsync(cancel.Token);
					var acceptTask = accepter.WaitForRequestAsync(cancel.Token);
					var removeTask = forwarder.PeriodicallyRemoveInactiveTargetsAsync(cancel.Token);

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
						}
						else // cancelTask
						{
							cancel.Cancel();
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
								//cancel.Cancel();
							}
							else
							{
								requestTask = requester.DelayedRequestForwardingAsync(cancel.Token);
							}
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
