using System;

namespace OrcaSql.Core.Engine.Records.LobStructures
{
	/* SMALL_ROOT (type: 0)
	 * Used whenever the inline data length is <= 64. Fixed size of 84 bytes, including record overhead.
	 * 
	 * Byte		Content
	 * 0-7		Blob ID (long)
	 * 8-9		Type (short)
	 * 10-11	Length (short)
	 * 12-15	?
	 * 16-79	Data (everything above [Length] is to be considered garbage). Max SMALL_ROOT data storage = 64 bytes
	 */
	public class SmallRoot : LobStructureBase, ILobStructure
	{
		public long BlobID { get; private set; }
		public short Length { get; private set; }
		private byte[] data;

		public SmallRoot(byte[] bytes, Database database)
			: base(database)
		{
			short type = LittleEndian.ReadInt16(bytes, 8);
			if(type != (short)LobStructureType.SMALL_ROOT)
				throw new ArgumentException("Invalid byte structure. Expected SMALL_ROOT, found " + type);
			
			BlobID = LittleEndian.ReadInt64(bytes, 0);
			Length = LittleEndian.ReadInt16(bytes, 10);
			data = Length <= 0 ? Array.Empty<byte>() : new byte[Length];
			if (Length > 0)
				Buffer.BlockCopy(bytes, 16, data, 0, Length);
		}

		public byte[] GetData()
		{
			return data;
		}
	}
}
