namespace OrcaSql.Core.Engine.Records.VariableLengthDataProxies
{
	public class RawByteProxy : IVariableLengthDataProxy
	{
		private readonly byte[] data;
		private readonly int offset;
		private readonly int length;

		public RawByteProxy(byte[] data)
			: this(data, 0, data.Length)
		{
		}

		public RawByteProxy(byte[] data, int offset, int length)
		{
			this.data = data;
			this.offset = offset;
			this.length = length;
		}

		public byte[] GetBytes()
		{
			if (offset == 0 && length == data.Length)
				return data;

			if (length == 0)
				return System.Array.Empty<byte>();

			var result = new byte[length];
			System.Buffer.BlockCopy(data, offset, result, 0, length);
			return result;
		}

		internal byte[] Source => data;

		internal int Offset => offset;

		internal int Length => length;
	}
}
