using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace CSharpExample
{
	internal class Utils
	{
		public static string ProcessNameBoundToUdpPort(int port)
		{
			try
			{
				return ProcessBoundToUdpPort(port)?.ProcessName;
			}
			catch
			{
				return null;
			}
		}

		public static Process ProcessBoundToUdpPort(int port)
		{
			ushort nPort = (ushort)IPAddress.HostToNetworkOrder((short)port);

			int size = 0;
			uint rc = NativeMethods.GetExtendedUdpTable(IntPtr.Zero, ref size, false,
				AddressFamily.InterNetwork, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);
			if (rc != ERROR_INSUFFICIENT_BUFFER)
			{
				throw new Win32Exception(Marshal.GetLastWin32Error());
			}

			IntPtr tablePtr = Marshal.AllocHGlobal(size);
			try
			{
				rc = NativeMethods.GetExtendedUdpTable(tablePtr, ref size, false,
					AddressFamily.InterNetwork, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);
				if (rc != NO_ERROR)
				{
					throw new Win32Exception(Marshal.GetLastWin32Error());
				}

				var table = Marshal.PtrToStructure<MIB_UDPTABLE_OWNER_PID>(tablePtr);
				var firstRowPtr = tablePtr + TableOffset;
				for (int i = 0; i < table.dwNumEntries; i++)
				{
					IntPtr rowPtr = firstRowPtr + RowSize * i;
					var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(rowPtr);
					if (row.dwLocalPort == nPort)
					{
						return Process.GetProcessById((int)row.dwOwningPid);
					}
				}
			}
			finally
			{
				Marshal.FreeHGlobal(tablePtr);
			}

			return null;
		}

		private static readonly int RowSize = Marshal.SizeOf(typeof(MIB_UDPROW_OWNER_PID));
		private static readonly int TableOffset =
			(int)Marshal.OffsetOf<MIB_UDPTABLE_OWNER_PID>(nameof(MIB_UDPTABLE_OWNER_PID.table));

		private const int NO_ERROR = 0;
		private const int ERROR_INSUFFICIENT_BUFFER = 122;

		private enum UDP_TABLE_CLASS
		{
			UDP_TABLE_BASIC,
			UDP_TABLE_OWNER_PID,
			UDP_TABLE_OWNER_MODULE
		};

		[StructLayout(LayoutKind.Sequential)]
		private struct MIB_UDPTABLE_OWNER_PID
		{
			public uint dwNumEntries;
			[MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.Struct, SizeConst = 1)]
			public MIB_UDPROW_OWNER_PID[] table;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct MIB_UDPROW_OWNER_PID
		{
			public uint dwLocalAddr;
			public uint dwLocalPort;
			public uint dwOwningPid;
		};

		private static class NativeMethods
		{
			[DllImport("Iphlpapi.dll", SetLastError = true)]
			public static extern uint GetExtendedUdpTable(
			   IntPtr pUdpTable,
			   ref int pdwSize,
			   bool bOrder,
			   AddressFamily ulAf,
			   UDP_TABLE_CLASS TableClass,
			   uint Reserved
			);
		}
	}
}
