using System;
using System.Diagnostics;

namespace OrcaSql.Core.Engine
{
	public class SlotPointer
	{
		public short FileID;
		public int PageID;
		public short SlotID;

		[DebuggerStepThrough]
		public SlotPointer(short fileID, int pageID, short slotID)
		{
			FileID = fileID;
			PageID = pageID;
			SlotID = slotID;
		}

		public SlotPointer(byte[] bytes)
			: this(bytes, 0)
		{
		}

		public SlotPointer(byte[] bytes, int offset)
		{
			if (bytes.Length - offset < 8)
				throw new ArgumentException("Input must be 8 bytes in the format pageID(4)fileID(2)slotID(2)");

			PageID = LittleEndian.ReadInt32(bytes, offset);
			FileID = LittleEndian.ReadInt16(bytes, offset + 4);
			SlotID = LittleEndian.ReadInt16(bytes, offset + 6);
		}

		public PagePointer PagePointer
		{
			[DebuggerStepThrough]
			get { return new PagePointer(FileID, PageID); }
		}

		public static bool operator ==(SlotPointer a, SlotPointer b)
		{
			return a.Equals(b);
		}

		public static bool operator !=(SlotPointer a, SlotPointer b)
		{
			return !a.Equals(b);
		}

		public override bool Equals(object obj)
		{
			var b = (SlotPointer)obj;

			return b.PageID == PageID && b.FileID == FileID && b.SlotID == SlotID;
		}

		public override int GetHashCode()
		{
			// KISS
			return (PageID + "_" + FileID + "_" + SlotID).GetHashCode();
		}

		public override string ToString()
		{
			return "(" + FileID + ":" + PageID + ":" + SlotID + ")";
		}
	}
}
