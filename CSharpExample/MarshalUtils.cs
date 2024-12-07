//
// Copyright (c) 2024 Erik Alveflo
//
using System;
using System.Runtime.InteropServices;

namespace CSharpExample
{
	internal static class MarshalUtils
	{
		public static T BytesToStruct<T>(byte[] bytes)
			where T : struct
		{
			if (bytes.Length != Marshal.SizeOf<T>())
			{
				throw new ArgumentException("Size mismatch between byte buffer and T. Cannot convert a byte buffer " +
					"to struct of type T when byte buffer is not the same length as sizeof(T).", nameof(bytes));
			}

			GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
			try
			{
				T structure = Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
				return structure;
			}
			finally
			{
				handle.Free();
			}
		}

		public static byte[] StructToBytes<T>(T structure)
		{
			byte[] bytes = new byte[Marshal.SizeOf<T>()];
			GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
			try
			{
				Marshal.StructureToPtr(structure, handle.AddrOfPinnedObject(), false);
				return bytes;
			}
			finally
			{
				handle.Free();
			}
		}
	}
}