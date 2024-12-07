//
// Copyright (c) 2024 Erik Alveflo
//
using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace DirtRally2
{
	internal class Program
	{
		const int DEFAULT_PORT = 20777;

		static IPEndPoint _endpoint = new IPEndPoint(IPAddress.Loopback, DEFAULT_PORT);
		static byte[] _buffer = null;
		static UInt32 _time = 0;

		static UdpClient _client;
		static Timer _timer;
		static ManualResetEvent _event;

		static void Main(string[] args)
		{
			Console.WriteLine($"DiRT Rally 2.0 UDP Telemetry Emulator");

			Console.WriteLine($"Staring UDP client");
			_client = new UdpClient();
			_event = new ManualResetEvent(false);
			_timer = new Timer(Callback, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(33));

			Console.WriteLine($"Running");

			Console.ReadKey(true);
			Console.WriteLine($"Shutdown");

			_timer.Dispose();
			_timer = null;
			_event.WaitOne(TimeSpan.FromMilliseconds(100));
		}

		static void Callback(object _)
		{
			if (_timer == null)
			{
				_event.Set();
				return;
			}

			float x = (float)Math.Sin(_time / 33.0f);

			var packet = new ExtraData0();
			packet.TotalTime = _time++;
			packet.AngularVelocityX = x;
			packet.AngularVelocityZ = x;
			packet.AngularVelocityY = x;
			packet.Yaw = x;
			packet.Pitch = x;
			packet.Roll = x;
			packet.AccelerationX = x;
			packet.AccelerationZ = x;
			packet.AccelerationY = x;
			packet.VelocityX = x;
			packet.VelocityZ = x;
			packet.VelocityY = x;
			packet.PositionX = (Int32)x;
			packet.PositionZ = (Int32)x;
			packet.PositionY = (Int32)x;

			int size = Marshal.SizeOf<ExtraData0>();
			IntPtr ptr = IntPtr.Zero;
			try
			{
				if (_buffer == null || _buffer.Length < size)
				{
					_buffer = new byte[size];
				}

				ptr = Marshal.AllocHGlobal(size);
				Marshal.StructureToPtr(packet, ptr, true);
				Marshal.Copy(ptr, _buffer, 0, size);
			}
			finally
			{
				Marshal.FreeHGlobal(ptr);
			}

			_client.Send(_buffer, size, _endpoint);
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct ExtraData0
	{
		public UInt32 TotalTime;
		public float AngularVelocityX;
		public float AngularVelocityZ;
		public float AngularVelocityY;
		public float Yaw;
		public float Pitch;
		public float Roll;
		public float AccelerationX;
		public float AccelerationZ;
		public float AccelerationY;
		public float VelocityX;
		public float VelocityZ;
		public float VelocityY;
		public Int32 PositionX;
		public Int32 PositionZ;
		public Int32 PositionY;
	}
}
