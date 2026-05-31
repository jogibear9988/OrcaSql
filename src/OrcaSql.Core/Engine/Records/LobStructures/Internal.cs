using System;
using System.IO;

namespace OrcaSql.Core.Engine.Records.LobStructures
{
	/* INTERNAL (type: 2)
	 * 
	 * Byte		Content
	 * 0-7		Blob ID (long)
	 * 8-9		Type (short)
	 * 10-11	MaxLinks (short)
	 * 12-13	CurLinks (short)
	 * 14-15	Level (short)
	 * 16-23	Offset[0] (long)
	 * 24-27	PageID[0] (int)
	 * 28-29	FileID[0] (short)
	 * 30-31	SlotID[0] (short)
	 * ...
	*/
	public class Internal : LobStructureBase, ILobStructure
	{
		public long BlobID { get; private set; }
		public short MaxLinks { get; private set; }
		public short CurLinks { get; private set; }
		public short Level { get; private set; }
		public InternalLobSlotPointer[] DataSlotPointers { get; private set; }

		public Internal(byte[] bytes, Database database)
			: base(database)
		{
			short type = LittleEndian.ReadInt16(bytes, 8);
			if (type != (short)LobStructureType.INTERNAL)
				throw new ArgumentException("Invalid byte structure. Expected INTERNAL, found " + type);

			BlobID = LittleEndian.ReadInt64(bytes, 0);
			MaxLinks = LittleEndian.ReadInt16(bytes, 10);
			CurLinks = LittleEndian.ReadInt16(bytes, 12);
			Level = LittleEndian.ReadInt16(bytes, 14);
			DataSlotPointers = new InternalLobSlotPointer[CurLinks];

			short offset = 16;
			for (short i = 0; i < CurLinks; i++)
			{
				DataSlotPointers[i] = new InternalLobSlotPointer(bytes, offset);
				offset += 16;
			}
		}

		public byte[] GetData()
		{
			using (var result = new MemoryStream())
			{
				foreach (var lobSlot in DataSlotPointers)
				{
					var lobRecord = Database.GetTextRecord(lobSlot);
					var lobStructure = LobStructureFactory.Create(lobRecord.FixedLengthData, Database);
					var data = lobStructure.GetData();

					if (data != null && data.Length > 0)
						result.Write(data, 0, data.Length);
				}

				return result.ToArray();
			}
		}
	}
}
