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
using System.Security;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

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
		static List<IPEndPoint> _forwardingTargets;
		static IPEndPoint _mainConsumerEp;

		static bool IsMainConsumer =>
			_client != null &&
			((IPEndPoint)_client.Client.LocalEndPoint).Port == DEFAULT_PORT;

		static void Main(string[] args)
		{
			Console.WriteLine($"Telemetry receiver");

			var exitTask = ReadKey();
			while (!exitTask.IsCompleted)
			{
				string remoteName = Utils.ProcessNameBoundToUdpPort(DEFAULT_PORT);
				Console.WriteLine($"Trying to bind telemetry port {DEFAULT_PORT} (bound by '{remoteName}')");

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
					Exception innerEx = consumerTask.Exception;
					while (innerEx.InnerException != null)
					{
						innerEx = innerEx.InnerException;
					}

					if (innerEx is TryToBindTelemetryPortException)
					{
						continue;
					}

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

			_forwardingTargets = new List<IPEndPoint>();
			_mainConsumerEp = null;

			while (true)
			{
				var result = await _client.ReceiveAsync();
				var type = IdentifyPacket(result.Buffer);
				LogPacket(type, result.RemoteEndPoint);
				switch (type)
				{
					case PacketType.Telemetry:
						await ExtractTelemetry(result.Buffer);
						await ForwardPacket(result.Buffer);
						break;

					case PacketType.Setup:
						await AcceptForwardingRequest(result.RemoteEndPoint);
						break;
				}
			}
		}

		static async Task RunAsSecondaryConsumer()
		{
			Console.WriteLine($"Running as secondary consumer on port " +
				$"{((IPEndPoint)_client.Client.LocalEndPoint).Port}");

			_forwardingTargets = null;
			_mainConsumerEp = new IPEndPoint(IPAddress.Loopback, DEFAULT_PORT);

			DateTime lastAcceptAt = DateTime.MinValue;
			await SendSetupPacket();

			var receiveTask = _client.ReceiveAsync();
			var setupTask = Task.Delay(SETUP_INTERAL);
			while (true)
			{
				var done = await Task.WhenAny(receiveTask, setupTask);
				if (done == receiveTask)
				{
					var result = receiveTask.Result;
					var type = IdentifyPacket(result.Buffer);
					LogPacket(type, result.RemoteEndPoint);
					switch (type)
					{
						case PacketType.Telemetry:
							await ExtractTelemetry(result.Buffer);
							break;

						case PacketType.Accept:
							lastAcceptAt = DateTime.Now;
							break;
					}
					receiveTask = _client.ReceiveAsync();
				}
				else
				{
					ThrowIfTelemetryPortIsFree();
					ThrowIfAcceptTimeout(lastAcceptAt);
					await SendSetupPacket();
					setupTask = Task.Delay(SETUP_INTERAL);
				}
			}
		}

		enum PacketType { Setup, Accept, Telemetry }
		static PacketType IdentifyPacket(byte[] buffer)
		{
			if (buffer.SequenceEqual(SETUP_PACKET))
			{
				return PacketType.Setup;
			}
			else if (buffer.SequenceEqual(ACCEPT_PACKET))
			{
				return PacketType.Accept;
			}
			else
			{
				return PacketType.Telemetry;
			}
		}

		static void LogPacket(PacketType type, IPEndPoint remoteEp)
		{
			string remoteName = Utils.ProcessNameBoundToUdpPort(remoteEp.Port);

			switch (type)
			{
				case PacketType.Setup:
					if (IsMainConsumer)
					{
						Console.WriteLine($"SETUP_PACKET received from '{remoteName}' {remoteEp}");
					}
					else
					{
						Console.WriteLine($"SETUP_PACKET received from '{remoteName}' {remoteEp} (and ignored)");
					}
					break;

				case PacketType.Accept:
					if (IsMainConsumer)
					{
						Console.WriteLine($"ACCEPT_PACKET received from '{remoteName}' {remoteEp} (and ignored)");
					}
					else
					{
						Console.WriteLine($"ACCEPT_PACKET received from '{remoteName}' {remoteEp}");
					}
					break;
			}
		}

		static async Task ExtractTelemetry(byte[] buffer)
		{
			// There is where processing of telemetry would occur
			await Task.Delay(0);
		}

		static async Task AcceptForwardingRequest(IPEndPoint remoteEp)
		{
			if (!_forwardingTargets.Contains(remoteEp))
			{
				_forwardingTargets.Add(remoteEp);

				string remoteName = Utils.ProcessNameBoundToUdpPort(remoteEp.Port);
				Console.WriteLine($"Forwarding enabled for '{remoteName}' {remoteEp}");
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
			var remoteEp = _mainConsumerEp;
			string remoteName = Utils.ProcessNameBoundToUdpPort(remoteEp.Port);
			Console.WriteLine($"Sending a SETUP_PACKET to '{remoteName}' {remoteEp}");
			await _client.SendAsync(SETUP_PACKET, SETUP_PACKET.Length, remoteEp);
		}

		static void ThrowIfTelemetryPortIsFree()
		{
			var remoteEp = _mainConsumerEp;
			if (remoteEp.Address == IPAddress.Loopback)
			{
				string remoteName = Utils.ProcessNameBoundToUdpPort(remoteEp.Port);
				if (remoteName == null)
				{
					Console.WriteLine($"No process bound to telemetry port {remoteEp.Port}");

					// We can try to bind the telemetry port right now since the main consumer was
					// running on the local computer (loopback address) but is no longer and we
					// trust the Win32 `GetExtendedUdpTable` API.
					throw new TryToBindTelemetryPortException();
				}
			}
		}

		static void ThrowIfAcceptTimeout(DateTime lastAcceptAt)
		{
			if (DateTime.Now - lastAcceptAt < SETUP_INTERAL)
			{
				return;
			}

			Console.WriteLine($"No ACCPET_PACKET received within timeout");

			var remoteEp = _mainConsumerEp;
			if (remoteEp.Address == IPAddress.Loopback)
			{
				string remoteName = Utils.ProcessNameBoundToUdpPort(remoteEp.Port);
				if (remoteName == null)
				{
					Console.WriteLine($"No process bound to telemetry port {remoteEp.Port}");

					// We can try to bind the telemetry port right now since the main consumer was
					// running on the local computer (loopback address) but is no longer and we
					// trust the Win32 `GetExtendedUdpTable` API.
					throw new TryToBindTelemetryPortException();
				}
				else
				{
					Console.WriteLine($"Telemetry port {remoteEp.Port} is bound to '{remoteName}'");
				}
			}

			Console.WriteLine($"Main consumer does not implement the forwarding protocol?");
		}

		// This exception is thrown when there is good reason to try to bind the telemetry port and
		// transition from a secondary consumer to the main consumer.
		class TryToBindTelemetryPortException : Exception
		{
		}
	}
}
