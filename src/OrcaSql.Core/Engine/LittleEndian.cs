using System;
using System.Buffers.Binary;

namespace OrcaSql.Core.Engine
{
	internal static class LittleEndian
	{
		public static short ReadInt16(byte[] bytes, int offset)
		{
			return BinaryPrimitives.ReadInt16LittleEndian(new ReadOnlySpan<byte>(bytes, offset, sizeof(short)));
		}

		public static int ReadInt32(byte[] bytes, int offset)
		{
			return BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(bytes, offset, sizeof(int)));
		}

		public static long ReadInt64(byte[] bytes, int offset)
		{
			return BinaryPrimitives.ReadInt64LittleEndian(new ReadOnlySpan<byte>(bytes, offset, sizeof(long)));
		}

		public static uint ReadUInt32(byte[] bytes, int offset)
		{
			return BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(bytes, offset, sizeof(uint)));
		}
	}
}
