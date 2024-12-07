using System.Runtime.InteropServices;

namespace CSharpExample
{
	internal static class MarshalUtils
	{
		public static T BytesToStruct<T>(byte[] bytes)
			where T : struct
		{
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