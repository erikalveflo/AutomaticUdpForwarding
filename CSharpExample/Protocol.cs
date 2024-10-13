using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CSharpExample
{
	internal static class Protocol
	{
		public static readonly TimeSpan REQUEST_INTERVAL = TimeSpan.FromSeconds(15);
		public static readonly TimeSpan REGISTRATION_TIMEOUT = TimeSpan.FromMinutes(1);
		public static readonly TimeSpan PIPE_TIMEOUT = TimeSpan.FromMilliseconds(500);

		public const int REQUEST_MAGIC = 0x3634B30B;

		[StructLayout(LayoutKind.Sequential)]
		public struct RequestPacket
		{
			[MarshalAs(UnmanagedType.I4)] public int Magic;
			[MarshalAs(UnmanagedType.I4)] public int Port;
		}

		// Gets the pipe name used by the ForwardingAcceptor at process `p`.
		public static string GetPipeName(Process p)
		{
			return $@"\\.\pipe\3aa45f85-9c74-41e0-b560-bd6b9e456275\{p.Id}";
		}
	}
}
