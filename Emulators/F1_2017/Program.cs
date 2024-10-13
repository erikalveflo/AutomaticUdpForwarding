using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace F1_2017
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
			Console.WriteLine($"F1 2017 UDP Telemetry Emulator");

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

			var packet = new Packet();
			packet.m_time = _time++;
			packet.m_x = x;
			packet.m_y = x;
			packet.m_z = x;
			packet.m_speed = x;
			packet.m_xv = x;
			packet.m_yv = x;
			packet.m_zv = x;
			packet.m_xr = x;
			packet.m_yr = x;
			packet.m_zr = x;
			packet.m_xd = x;
			packet.m_yd = x;
			packet.m_zd = x;
			packet.m_gforce_lat = x;
			packet.m_gforce_lon = x;
			packet.m_car_data = new CarInfo[Packet.CAR_DATA_LENGTH];
			packet.m_car_data[0].m_currentLapNum = (byte)(_time / 90000.0f);

			int size = Marshal.SizeOf<Packet>();
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
	struct Vector3<T>
	{
		public T X, Y, Z;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct Corners<T>
	{
		public T RearLeft, RearRight, FrontLeft, FrontRight;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct Packet
	{
		public float m_time;
		public float m_lapTime;
		public float m_lapDistance;
		public float m_totalDistance;
		public float m_x;
		public float m_y;
		public float m_z;
		public float m_speed;
		public float m_xv;
		public float m_yv;
		public float m_zv;
		public float m_xr;
		public float m_yr;
		public float m_zr;
		public float m_xd;
		public float m_yd;
		public float m_zd;
		public Corners<float> m_susp_pos;
		public Corners<float> m_susp_vel;
		public Corners<float> m_wheel_speed;
		public float m_throttle;
		public float m_steer;
		public float m_brake;
		public float m_clutch;
		public float m_gear;
		public float m_gforce_lat;
		public float m_gforce_lon;
		public float m_lap;
		public float m_engineRate;
		public float m_sli_pro_native_support;
		public float m_car_position;
		public float m_kers_level;
		public float m_kers_max_level;
		public float m_drs;
		public float m_traction_control;
		public float m_anti_lock_brakes;
		public float m_fuel_in_tank;
		public float m_fuel_capacity;
		public float m_in_pits;
		public float m_sector;
		public float m_sector1_time;
		public float m_sector2_time;
		public Corners<float> m_brakes_temp;
		public Corners<float> m_tyres_pressure;
		public float m_team_info;
		public float m_total_laps;
		public float m_track_size;
		public float m_last_lap_time;
		public float m_max_rpm;
		public float m_idle_rpm;
		public float m_max_gears;
		public float m_sessionType;
		public float m_drsAllowed;
		public float m_track_number;
		public float m_vehicleFIAFlags;
		public float m_era;
		public float m_engine_temperature;
		public float m_gforce_vert;
		public float m_ang_vel_x;
		public float m_ang_vel_y;
		public float m_ang_vel_z;
		public Corners<byte> m_tyres_temperature;
		public Corners<byte> m_tyres_wear;
		public byte m_tyre_compound;
		public byte m_front_brake_bias;
		public byte m_fuel_mix;
		public byte m_currentLapInvalid;
		public Corners<byte> m_tyres_damage;
		public byte m_front_left_wing_damage;
		public byte m_front_right_wing_damage;
		public byte m_rear_wing_damage;
		public byte m_engine_damage;
		public byte m_gear_box_damage;
		public byte m_exhaust_damage;
		public byte m_pit_limiter_status;
		public byte m_pit_speed_limit;
		public float m_session_time_left;
		public byte m_rev_lights_percent;
		public byte m_is_spectating;
		public byte m_spectator_car_index;
		public byte m_num_cars;
		public byte m_player_car_index;

		public const int CAR_DATA_LENGTH = 20;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = CAR_DATA_LENGTH)]
		public CarInfo[] m_car_data;

		public float m_yaw;
		public float m_pitch;
		public float m_roll;
		public float m_x_local_velocity;
		public float m_y_local_velocity;
		public float m_z_local_velocity;
		public Corners<float> m_susp_acceleration;
		public float m_ang_acc_x;
		public float m_ang_acc_y;
		public float m_ang_acc_z;
	};

	[StructLayout(LayoutKind.Sequential)]
	internal struct CarInfo
	{
		public Vector3<float> m_worldPosition;
		public float m_lastLapTime;
		public float m_currentLapTime;
		public float m_bestLapTime;
		public float m_sector1Time;
		public float m_sector2Time;
		public float m_lapDistance;
		public byte m_driverId;
		public byte m_teamId;
		public byte m_carPosition;
		public byte m_currentLapNum;
		public byte m_tyreCompound;
		public byte m_inPits;
		public byte m_sector;
		public byte m_currentLapInvalid;
		public byte m_penalties;
	};
}
