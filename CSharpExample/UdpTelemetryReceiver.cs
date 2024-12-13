//
// Copyright (c) 2024 Erik Alveflo
//
using System;
using System.Diagnostics;
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
			_client = null;
		}

		public bool TryBind(int port)
		{
			if (_client != null && _client.Client.IsBound)
			{
				Dispose(); // Cannot bind again
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

			// In later versions of .NET we can call `_client.ReceiveAsync(ct)` but not here.
			using (var delayCts = new CancellationTokenSource())
			using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, delayCts.Token))
			{
				var delayTask = Task.Delay(Timeout.Infinite, linkedCts.Token);
				var completedTask = await Task.WhenAny(receiveTask, delayTask);
				if (completedTask == receiveTask)
				{
					delayCts.Cancel(); // Cancel `delayTask` to avoid leak
					var result = await receiveTask; // Finishes immediately
					return result.Buffer;
				}

				Debug.Assert(ct.IsCancellationRequested);
				Dispose(); // Otherwise `receiveTask` is leaked
				throw new OperationCanceledException(ct);
			}
		}
	}
}
