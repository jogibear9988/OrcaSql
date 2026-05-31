using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace OrcaSql.Core.Engine
{
	internal static class LittleEndian
	{
		public static short ReadInt16(byte[] bytes, int offset)
		{
			return BinaryPrimitives.ReadInt16LittleEndian(new ReadOnlySpan<byte>(bytes, offset, sizeof(short)));
		}

		public static short ReadInt16(ReadOnlySpan<byte> bytes)
		{
			return BinaryPrimitives.ReadInt16LittleEndian(bytes);
		}

		public static int ReadInt32(byte[] bytes, int offset)
		{
			return BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(bytes, offset, sizeof(int)));
		}

		public static int ReadInt32(ReadOnlySpan<byte> bytes)
		{
			return BinaryPrimitives.ReadInt32LittleEndian(bytes);
		}

		public static long ReadInt64(byte[] bytes, int offset)
		{
			return BinaryPrimitives.ReadInt64LittleEndian(new ReadOnlySpan<byte>(bytes, offset, sizeof(long)));
		}

		public static long ReadInt64(ReadOnlySpan<byte> bytes)
		{
			return BinaryPrimitives.ReadInt64LittleEndian(bytes);
		}

		public static uint ReadUInt32(byte[] bytes, int offset)
		{
			return BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(bytes, offset, sizeof(uint)));
		}

		public static float ReadSingle(ReadOnlySpan<byte> bytes)
		{
			if (BitConverter.IsLittleEndian)
				return MemoryMarshal.Read<float>(bytes);

			var value = BinaryPrimitives.ReadInt32LittleEndian(bytes);
			return BitConverter.ToSingle(BitConverter.GetBytes(value), 0);
		}

		public static double ReadDouble(ReadOnlySpan<byte> bytes)
		{
			if (BitConverter.IsLittleEndian)
				return MemoryMarshal.Read<double>(bytes);

			var value = BinaryPrimitives.ReadInt64LittleEndian(bytes);
			return BitConverter.ToDouble(BitConverter.GetBytes(value), 0);
		}
	}
}
