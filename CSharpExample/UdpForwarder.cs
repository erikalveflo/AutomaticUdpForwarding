using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpExample
{
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
			Task[] tasks;
			lock (_targets)
			{
				tasks = _targets
					.Select(t => _client.SendAsync(datagram, numBytes, t.RemoteEp))
					.ToArray();
			}
			await Task.WhenAll(tasks);
		}

		public void AddOrRenewTarget(IPEndPoint targetEp)
		{
			var targetName = UdpUtils.ProcessNameBoundToPort(targetEp.Port);

			lock (_targets)
			{
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
		}

		public void RemoveInactiveTargets()
		{
			lock (_targets)
			{
				_targets.RemoveAll(x => DateTime.Now - x.LastSeen > Protocol.REGISTRATION_TIMEOUT);
			}
		}

		public async Task PeriodicallyRemoveInactiveTargetsAsync(CancellationToken ct)
		{
			while (true)
			{
				ct.ThrowIfCancellationRequested();

				RemoveInactiveTargets();
				await Task.Delay(Protocol.REQUEST_INTERVAL, ct);
			}
		}
	}
}
