using System;
namespace OrcaSql.Core.Engine.Records.LobStructures
{
	public class LobSlotPointer : SlotPointer
	{
		public int Size { get; private set; }

		/* Byte		Content
		 * 0-3		Size (int)
		 * 4-7		PageID (int)
		 * 8-9		FileID (short)
		 * 10-11	SlotID (short)
		 */
		public LobSlotPointer(byte[] bytes)
			: this(bytes, 0)
		{
		}

		public LobSlotPointer(byte[] bytes, int offset)
			: base(bytes, offset + 4)
		{
			Size = LittleEndian.ReadInt32(bytes, offset);
		}
	}
}
