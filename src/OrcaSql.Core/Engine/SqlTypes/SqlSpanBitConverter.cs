using System;
using OrcaSql.Framework;

namespace OrcaSql.Core.Engine.SqlTypes
{
	internal static class SqlSpanBitConverter
	{
		public static short ToInt16FromBigEndian(ReadOnlySpan<byte> input, Offset offset)
		{
			if (input.Length == 0)
				return 0;

			var offsetValue = (short)(offset == Offset.Zero ? 0 : (-1 * (1 << Math.Min(input.Length, sizeof(short)) * 8 - 1)));

			switch (input.Length)
			{
				case 1:
					return (short)(offsetValue + input[0]);

				default:
					return (short)(offsetValue + (input[0] << 8 | input[1]));
			}
		}

		public static int ToInt32FromBigEndian(ReadOnlySpan<byte> input, Offset offset)
		{
			if (input.Length == 0)
				return 0;

			var offsetValue = offset == Offset.Zero ? 0 : (-1 * (1 << Math.Min(input.Length, sizeof(int)) * 8 - 1));

			switch (input.Length)
			{
				case 1:
					return offsetValue + input[0];

				case 2:
					return offsetValue + (input[0] << 8 | input[1]);

				case 3:
					return offsetValue + (input[0] << 16 | input[1] << 8 | input[2]);

				default:
					return offsetValue + (input[0] << 24 | input[1] << 16 | input[2] << 8 | input[3]);
			}
		}

		public static long ToInt64FromBigEndian(ReadOnlySpan<byte> input, Offset offset)
		{
			if (input.Length == 0)
				return 0;

			var offsetValue = offset == Offset.Zero ? 0 : (-1 * ((long)1 << Math.Min(input.Length, sizeof(long)) * 8 - 1));

			switch (input.Length)
			{
				case 1:
					return offsetValue + input[0];

				case 2:
					return offsetValue + (input[0] << 8 | input[1]);

				case 3:
					return offsetValue + (input[0] << 16 | input[1] << 8 | input[2]);

				case 4:
					return (int)offsetValue + (input[0] << 24 | input[1] << 16 | input[2] << 8 | input[3]);

				case 5:
					return offsetValue + ((long)input[0] << 32 | (long)input[1] << 24 | (long)input[2] << 16 | (long)input[3] << 8 | input[4]);

				case 6:
					return offsetValue + ((long)input[0] << 40 | (long)input[1] << 32 | (long)input[2] << 24 | (long)input[3] << 16 | (long)input[4] << 8 | input[5]);

				case 7:
					return offsetValue + ((long)input[0] << 48 | (long)input[1] << 40 | (long)input[2] << 32 | (long)input[3] << 24 | (long)input[4] << 16 | (long)input[5] << 8 | input[6]);

				default:
					return offsetValue + ((long)input[0] << 56 | (long)input[1] << 48 | (long)input[2] << 40 | (long)input[3] << 32 | (long)input[4] << 24 | (long)input[5] << 16 | (long)input[6] << 8 | input[7]);
			}
		}
	}
}
