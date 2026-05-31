using System;
namespace OrcaSql.Core.Engine.Records.LobStructures
{
	public class InternalLobSlotPointer : SlotPointer
	{
		public long Offset { get; private set; }

		/* Byte		Content
		 * 0-7		Offset (long)
		 * 8-11		PageID (int)
		 * 12-13	FileID (short)
		 * 14-15	SlotID (short)
		 */
		public InternalLobSlotPointer(byte[] bytes)
			: this(bytes, 0)
		{
		}

		public InternalLobSlotPointer(byte[] bytes, int offset)
			: base(bytes, offset + 8)
		{
			Offset = LittleEndian.ReadInt64(bytes, offset);
		}
	}
}
