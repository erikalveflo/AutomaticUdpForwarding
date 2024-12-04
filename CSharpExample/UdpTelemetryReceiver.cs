using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
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

		public async Task<byte[]> ReceiveAsync(CancellationToken ct)
		{
			var receiveTask = _client.ReceiveAsync();

			using (var delayCts = new CancellationTokenSource())
			using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, delayCts.Token))
			{
				var delayTask = Task.Delay(Timeout.Infinite, linkedCts.Token);
				await Task.WhenAny(receiveTask, delayTask);
				ct.ThrowIfCancellationRequested();
				delayCts.Cancel(); // Cancel delayTask
			}

			var result = await receiveTask;
			return result.Buffer;
		}
	}
}
