//
// Copyright (c) 2024 Erik Alveflo
//
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CSharpExample
{
	internal static class Protocol
	{
		// Forwarding is enabled for one minute after the last forwarding request is received.
		public static readonly TimeSpan REGISTRATION_TIMEOUT = TimeSpan.FromMinutes(1);

		// Send forwarding requests every 15 seconds. Technically, we could wait 59 seconds between
		// requests, but this would lead to unnecessary delays if the main consumer is terminated
		// and another takes its place. We'd rather send too many requests and react quickly.
		public static readonly TimeSpan REQUEST_INTERVAL = TimeSpan.FromSeconds(15);

		// Allow only for a short timeout in pipe communications. Pipes are fast. There is no need
		// to wait for long.
		public static readonly TimeSpan PIPE_TIMEOUT = TimeSpan.FromMilliseconds(500);

		// This random number identifies a forwarding request. By using a unique ID we can identify
		// forwarding request and potentially expand the protocol in the future.
		public const int REQUEST_MAGIC = 0x3634B30B;

		// This is the forwarding request sent from a secondary consumer to the main consumer when
		// requesting that UDP forwarding be enabled.
		[StructLayout(LayoutKind.Sequential)]
		public struct Request
		{
			// Use `REQUEST_MAGIC` to identify this packet as a request to enable UDP forwarding.
			[MarshalAs(UnmanagedType.I4)] public int Magic;

			// This is the UDP port bound by the secondary consumer. The main consumer is requested
			// to enable UDP forwarding of telemetry packets to this port.
			[MarshalAs(UnmanagedType.I4)] public int Port;
		}

		// Gets the pipe name used by the main consumer for a given telemetry port.
		public static string GetPipeName(Process mainConsumer, int telemetryPort)
		{
			return $@"\\.\pipe\3aa45f85-9c74-41e0-b560-bd6b9e456275\{mainConsumer.Id}\{telemetryPort}";
		}
	}
}
