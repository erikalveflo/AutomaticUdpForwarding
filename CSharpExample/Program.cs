using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Threading;
using System.Runtime.Remoting.Messaging;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.Data;

namespace CSharpExample
{
	internal class Program
	{
		const int DEFAULT_PORT = 20777;

		// "UdpForwardingRequest" in bytes
		static readonly byte[] SETUP_PACKET = { 0x55, 0x64, 0x70, 0x46, 0x6F, 0x72, 0x77, 0x61,
			0x72, 0x64, 0x69, 0x6E, 0x67, 0x52, 0x65, 0x71, 0x75, 0x65, 0x73, 0x74 };

		// "UdpForwardingEnabled" in bytes
		static readonly byte[] ACCEPT_PACKET = { 0x55, 0x64, 0x70, 0x46, 0x6F, 0x72, 0x77, 0x61,
			0x72, 0x64, 0x69, 0x6E, 0x67, 0x45, 0x6E, 0x61, 0x62, 0x6C, 0x65, 0x64 };

		// Time between setup packets
		static readonly TimeSpan SETUP_INTERAL = TimeSpan.FromSeconds(15);

		static UdpClient _client;
		static List<IPEndPoint> _forwardingTargets = new List<IPEndPoint>();

		static bool IsMainConsumer =>
			_client != null &&
			((IPEndPoint)_client.Client.LocalEndPoint).Port == DEFAULT_PORT;

		static void Main(string[] args)
		{
			Console.WriteLine($"Telemetry receiver");

			var exitTask = ReadKey();
			while (!exitTask.IsCompleted)
			{
				Console.WriteLine($"Trying to bind telemetry port {DEFAULT_PORT}");

				Task consumerTask;
				try
				{
					_client = new UdpClient(DEFAULT_PORT);
					IgnoreDisconnects(_client);
					consumerTask = RunAsMainConsumer();
				}
				catch (SocketException)
				{
					_client = new UdpClient();
					_client.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
					IgnoreDisconnects(_client);
					consumerTask = RunAsSecondaryConsumer();
				}

				Task.WaitAny(consumerTask, exitTask);

				if (consumerTask.IsFaulted)
				{
					throw consumerTask.Exception;
				}
			}

			Console.WriteLine($"Shutdown");
		}

		static void IgnoreDisconnects(UdpClient client)
		{
			// UDP sockets on Windows have an interesting issue where `ReceiveAsync()` can thrown
			// an exception because a remote host refused to receive a previous DGRAM sent on the
			// same socket. This is nonsense, as UDP sockets are connection less. Configure the
			// socket to ignore these errors.
			// https://stackoverflow.com/questions/47779248/why-is-there-a-remote-closed-connection-exception-for-udp-sockets
			// https://learn.microsoft.com/en-us/windows/win32/winsock/winsock-ioctls#sio_udp_connreset-opcode-setting-i-t3
			uint IOC_IN = 0x80000000;
			uint IOC_VENDOR = 0x18000000;
			uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
			client.Client.IOControl((int)SIO_UDP_CONNRESET, new byte[] { 0 }, null);
		}

		static async Task ReadKey()
		{
			await Task.Run(() => Console.ReadKey(true));
		}

		static async Task RunAsMainConsumer()
		{
			Console.WriteLine($"Running as main consumer on port " +
				$"{((IPEndPoint)_client.Client.LocalEndPoint).Port}");

			while (true)
			{
				var result = await _client.ReceiveAsync();
				await PacketReceived(result.Buffer, result.RemoteEndPoint);
				await ForwardPacket(result.Buffer);
			}
		}

		static async Task RunAsSecondaryConsumer()
		{
			Console.WriteLine($"Running as secondary consumer on port " +
				$"{((IPEndPoint)_client.Client.LocalEndPoint).Port}");

			await SendSetupPacket();

			var receiveTask = _client.ReceiveAsync();
			var setupTask = Task.Delay(SETUP_INTERAL);
			while (true)
			{
				var done = await Task.WhenAny(receiveTask, setupTask);
				if (done == receiveTask)
				{
					var result = receiveTask.Result;
					await PacketReceived(result.Buffer, result.RemoteEndPoint);
					receiveTask = _client.ReceiveAsync();
				}
				else
				{
					await SendSetupPacket();
					setupTask = Task.Delay(SETUP_INTERAL);
				}
			}
		}

		static async Task PacketReceived(byte[] buffer, IPEndPoint remoteEp)
		{
			if (buffer.SequenceEqual(SETUP_PACKET))
			{
				if (IsMainConsumer)
				{
					Console.WriteLine($"SETUP_PACKET received from {remoteEp}");
					await AcceptForwardingRequest(remoteEp);
				}
				else
				{
					Console.WriteLine($"SETUP_PACKET received from {remoteEp} (and ignored)");
				}
			}
			else if (buffer.SequenceEqual(ACCEPT_PACKET))
			{
				if (IsMainConsumer)
				{
					Console.WriteLine($"ACCEPT_PACKET received from {remoteEp} (and ignored)");
				}
				else
				{
					Console.WriteLine($"ACCEPT_PACKET received from {remoteEp}");
				}
			}
			else
			{
				// There is where processing of telemetry would occur
			}
		}

		static async Task AcceptForwardingRequest(IPEndPoint remoteEp)
		{
			if (!_forwardingTargets.Contains(remoteEp))
			{
				_forwardingTargets.Add(remoteEp);
				Console.WriteLine($"Forwarding enabled for {remoteEp}");
			}
			await _client.SendAsync(ACCEPT_PACKET, ACCEPT_PACKET.Length, remoteEp);
		}

		static async Task ForwardPacket(byte[] buffer)
		{
			foreach (var remoteEp in _forwardingTargets)
			{
				await _client.SendAsync(buffer, buffer.Length, remoteEp);
			}
		}

		static async Task SendSetupPacket()
		{
			var mainConsumerEp = new IPEndPoint(IPAddress.Loopback, DEFAULT_PORT);
			Console.WriteLine($"Sending a SETUP_PACKET to {mainConsumerEp}");
			await _client.SendAsync(SETUP_PACKET, SETUP_PACKET.Length, mainConsumerEp);
		}
	}
}
