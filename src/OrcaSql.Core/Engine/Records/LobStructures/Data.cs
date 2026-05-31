using System;

namespace OrcaSql.Core.Engine.Records.LobStructures
{
	/* DATA (type: 3)
	 * Used to store data. Variable length size, in practice always > 64 + overhead bytes
	 * as it'll otherwise be stored in a SMALL_ROOT.
	 * 
	 * Byte		Content
	 * 0-7		Blob ID (long)
	 * 8-9		Type (short)
	 * 10-X		Data
	 */
	public class Data : LobStructureBase, ILobStructure
	{
		public long BlobID { get; private set; }
		private byte[] data;

		public Data(byte[] bytes, Database database)
			: base(database)
		{
			short type = LittleEndian.ReadInt16(bytes, 8);
			if (type != (short)LobStructureType.DATA)
				throw new ArgumentException("Invalid byte structure. Expected DATA, found " + type);

			BlobID = LittleEndian.ReadInt64(bytes, 0);
			var length = bytes.Length - 10;
			data = length <= 0 ? Array.Empty<byte>() : new byte[length];
			if (length > 0)
				Buffer.BlockCopy(bytes, 10, data, 0, length);
		}

		public byte[] GetData()
		{
			return data;
		}
	}
}
