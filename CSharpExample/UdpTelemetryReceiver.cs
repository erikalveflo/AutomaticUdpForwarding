using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace CSharpExample
{
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
}
