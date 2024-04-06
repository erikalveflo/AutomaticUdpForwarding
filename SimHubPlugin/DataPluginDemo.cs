using GameReaderCommon;
using SimHub.Plugins;
using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace User.PluginSdkDemo
{
	[PluginAuthor("Erik Alveflo")]
	[PluginName("Automatic UDP forwarding")]
	[PluginDescription("Automatically configures UDP forwarding")]
	public class DataPluginDemo : IPlugin
	{
		// "UdpForwardingRequest" in bytes
		static readonly byte[] SETUP_PACKET = { 0x55, 0x64, 0x70, 0x46, 0x6F, 0x72, 0x77, 0x61, 0x72, 0x64, 0x69,
			0x6E, 0x67, 0x52, 0x65, 0x71, 0x75, 0x65, 0x73, 0x74 };

		// "UdpForwardingEnabled" in bytes
		static readonly byte[] ACCEPT_PACKET = { 0x55, 0x64, 0x70, 0x46, 0x6F, 0x72, 0x77, 0x61, 0x72, 0x64, 0x69,
			0x6E, 0x67, 0x45, 0x6E, 0x61, 0x62, 0x6C, 0x65, 0x64 };

		// Time between setup packets.
		static readonly TimeSpan SETUP_INTERAL = TimeSpan.FromSeconds(15);

		private object m_lock = new object();
		private Socket m_socket;
		private int m_simhubPort;
		private int m_telemetryPort;
		private byte[] m_packetBuffer = new byte[4096];
		private EndPoint m_packetEndpoint = new IPEndPoint(IPAddress.Any, 0);
		private IAsyncResult m_asyncResult;
		private Timer m_setupTimer;
		private DateTime m_lastActivity = DateTime.MinValue;

		// The main consumer is bound to the game's default telemetry port.
		private bool IsMainConsumer => (m_socket?.LocalEndPoint as IPEndPoint)?.Port == m_telemetryPort;

		/// <summary>
		/// Instance of the current plugin manager
		/// </summary>
		public PluginManager PluginManager { get; set; }

		/// <summary>
		/// Called at plugin manager stop, close/dispose anything needed here !
		/// Plugins are rebuilt at game change
		/// </summary>
		/// <param name="pluginManager"></param>
		public void End(PluginManager pluginManager)
		{
			SimHub.Logging.Current.Info($"{nameof(DataPluginDemo)}: Stopping plugin");

			try
			{
				lock (m_lock)
				{
					m_socket?.Close();
					m_socket?.Dispose();
					m_socket = null;
					m_packetBuffer = null;
					m_asyncResult = null;
					m_setupTimer?.Dispose();
					m_setupTimer = null;
				}
			}
			catch { }
		}

		/// <summary>
		/// Called once after plugins startup
		/// Plugins are rebuilt at game change
		/// </summary>
		/// <param name="pluginManager"></param>
		public void Init(PluginManager pluginManager)
		{
			try
			{
				SimHub.Logging.Current.Info($"{nameof(DataPluginDemo)}: Starting plugin");

				// We need to inspect incoming packets. We also need to know the game's telemetry port. Both of these
				// things are possibly by abusing the SimHub API.
				if (!RedirectUdpGame(out m_telemetryPort, out m_simhubPort))
				{
					return;
				}

				// Attempt to bind to the game's telemetry port. Only the main consumer will succeed.
				if (CreateAndBindSocket(m_telemetryPort, out m_socket))
				{
					// We managed to bind to the game's telemetry port: we are the main consumer.
					SimHub.Logging.Current.Info($"{nameof(DataPluginDemo)}: We are the main consumer");

					// We can receive telemetry on this port and send it to SimHub.
					ReadNextPacket();
				}
				else
				{
					// Someone else is already bound to the game's telemetry port: we are a secondary consumer.
					SimHub.Logging.Current.Info($"{nameof(DataPluginDemo)}: We are a secondary consumer");

					// Bind to an unused port and issue a setup packet to the main consumer.
					if (CreateAndBindSocket(0, out m_socket))
					{
						BeginSendingSetupPackets();
						ReadNextPacket();
					}
				}
			}
			catch { }
		}

		private bool RedirectUdpGame(out int telemetryPort, out int simhubPort)
		{
			telemetryPort = 0;
			simhubPort = 0;

			var udpGame = PluginManager.GameManager as IUDPGame;
			if (udpGame == null || PluginManager.GameManager as IUDPForward == null)
			{
				SimHub.Logging.Current.Info($"{nameof(DataPluginDemo)}: Game does not support UDP");
				return false;
			}

			telemetryPort = udpGame.UDPPort;
			SimHub.Logging.Current.Info($"{nameof(DataPluginDemo)}: Game's default UDP port: {telemetryPort}");

			if (!FindUnusedPort(telemetryPort, out simhubPort))
			{
				SimHub.Logging.Current.Info($"{nameof(DataPluginDemo)}: Unable to find an unused port");
				return false;
			}

			SimHub.Logging.Current.Info($"{nameof(DataPluginDemo)}: Redirecting UDP receiver to port: {simhubPort}");
			udpGame.UDPPort = simhubPort;
			return true;
		}

		private bool FindUnusedPort(int avoidPort, out int unusedPort)
		{
			var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners();

			var rng = new Random();
			for (int attempt = 0; attempt < 40; attempt++)
			{
				// The OS uses ports in the 0-11000 range. Random ports are assigned in the 49152-65536 range.
				int port = rng.Next(12000, 49000);
				bool used = port == avoidPort || listeners.Any(x => port == x.Port);
				if (!used)
				{
					unusedPort = port;
					return true;
				}
			}

			unusedPort = 0;
			return false;
		}

		private bool CreateAndBindSocket(int port, out Socket socket)
		{
			socket = null;

			try
			{
				socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
				socket.Bind(new IPEndPoint(IPAddress.Any, port));
				SimHub.Logging.Current.Info($"{nameof(DataPluginDemo)}: " +
					$"Listening on port: {((IPEndPoint)socket.LocalEndPoint).Port}");
				return true;
			}
			catch (Exception ex)
			{
				SimHub.Logging.Current.Error($"{nameof(DataPluginDemo)}: " +
					$"Failed to create listening socket for port {port}. " +
					$"The given error message was: {ex}");

				socket?.Dispose();
				socket = null;
			}

			return false;
		}

		private void AddForwardingTarget(IPEndPoint endpoint)
		{
			var udp = PluginManager.GameManager as IUDPGame;
			var forwarder = PluginManager.GameManager as IUDPForward;
			if (forwarder != null)
			{
				if (!forwarder.UDPForwardTargets.Contains(endpoint))
				{
					forwarder.UDPForwardTargets.Add(endpoint);
					SimHub.Logging.Current.Info($"{nameof(DataPluginDemo)}: " +
						$"Successfully configured automatic forwarding on endpoint {endpoint}");
				}
			}
		}

		private void ReadNextPacket()
		{
			lock (m_lock)
			{
				if (m_socket == null)
				{
					return;
				}

				m_asyncResult = m_socket.BeginReceiveFrom(m_packetBuffer, 0, m_packetBuffer.Length, SocketFlags.None,
					ref m_packetEndpoint, PacketReceived, null);
			}
		}

		private void PacketReceived(object state)
		{
			lock (m_lock)
			{
				if (m_socket == null || m_asyncResult == null)
				{
					return;
				}

				try
				{
					int read = m_socket.EndReceiveFrom(m_asyncResult, ref m_packetEndpoint);
					if (m_packetBuffer == null || m_packetEndpoint == null)
					{
						return;
					}

					if (read == 0)
					{
						ReadNextPacket();
						return;
					}

					if (m_packetBuffer.Take(read).SequenceEqual(SETUP_PACKET))
					{
						SimHub.Logging.Current.Info($"{nameof(DataPluginDemo)}: Got SETUP_PACKET");

						if (IsMainConsumer)
						{
							// A setup packet was received. We should now setup forwarding of all telemetry packets for
							// this endpoint and respond with an accept packet.
							AddForwardingTarget((IPEndPoint)m_packetEndpoint);
							m_socket.SendTo(ACCEPT_PACKET, (IPEndPoint)m_packetEndpoint);

							SimHub.Logging.Current.Info($"{nameof(DataPluginDemo)}: ACCEPT_PACKET sent");
						}
					}
					else if (m_packetBuffer.Take(read).SequenceEqual(ACCEPT_PACKET))
					{
						SimHub.Logging.Current.Info($"{nameof(DataPluginDemo)}: Got ACCEPT_PACKET");

						if (!IsMainConsumer)
						{
							m_lastActivity = DateTime.Now;

							SimHub.Logging.Current.Info($"{nameof(DataPluginDemo)}: " +
								$"Our request for forwarding was accepted");
						}
					}
					else
					{
						m_lastActivity = DateTime.Now;
						m_socket.SendTo(m_packetBuffer, read, SocketFlags.None,
							new IPEndPoint(IPAddress.Loopback, m_simhubPort));
					}

					ReadNextPacket();
 				}
				catch (Exception ex)
				{
					SimHub.Logging.Current.Error($"{nameof(DataPluginDemo)}: " +
						$"Failed to process UDP packet. The error given was: {ex}");
				}
			}
		}

		private void BeginSendingSetupPackets()
		{
			// Setup packets should be sent more than once per minute.
			m_lastActivity = DateTime.Now;
			m_setupTimer = new Timer(SendSetupPacket, null, TimeSpan.Zero, SETUP_INTERAL);
		}

		private void SendSetupPacket(object state)
		{
			lock (m_lock)
			{
				if (m_socket == null)
				{
					return;
				}

				if (DateTime.Now - m_lastActivity > SETUP_INTERAL + TimeSpan.FromSeconds(3))
				{
					m_lastActivity = DateTime.Now;

					// The main consumer has not sent any telemetry our way for some time. Let us check if the main
					// consumer is still around.
					var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners();
					if (!listeners.Any(x => x.Port == m_telemetryPort))
					{
						if (CreateAndBindSocket(m_telemetryPort, out var socket))
						{
							// We managed to bind to the game's telemetry port: we are the main consumer.
							SimHub.Logging.Current.Info($"{nameof(DataPluginDemo)}: We are the main consumer");

							// We can receive telemetry on this port and send it to SimHub.
							m_socket.Close();
							m_socket.Dispose();
							m_socket = socket;
							ReadNextPacket();

							// Stop sending setup packets.
							m_setupTimer.Dispose();
							m_setupTimer = null;
							return;
						}
					}
				}

				// Send a setup packet to the game's telemetry port. The main consumer is bound and listening to this
				// port. If they support this protocol they will accept our request and start forwarding telemetry to
				// our local endpoint.
				m_socket.SendTo(SETUP_PACKET, new IPEndPoint(IPAddress.Loopback, m_telemetryPort));

				SimHub.Logging.Current.Info($"{nameof(DataPluginDemo)}: SETUP_PACKET sent");
			}
		}
	}
}