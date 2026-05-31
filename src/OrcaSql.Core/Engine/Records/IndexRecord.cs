using System;
using OrcaSql.Core.Engine.Pages;

namespace OrcaSql.Core.Engine.Records
{
	public class IndexRecord : Record
	{
		public IndexRecord(byte[] bytes, Page page)
			: this(bytes, 0, bytes.Length, page)
		{
		}

		public IndexRecord(byte[] bytes, int recordStart, int recordLength, Page page)
			: base(page)
		{
			parseStatusBitsA(bytes[recordStart]);

			// Index records don't contain fixed length header - it's stored in the page header
			FixedLengthData = CopyBytes(bytes, recordStart + 1, Page.Header.Pminlen - 1);

            PageId = new PagePointer(FixedLengthData, FixedLengthData.Length - 6);

			var offset = Page.Header.Pminlen;

			NumberOfColumns = LittleEndian.ReadInt16(bytes, recordStart + offset);
			offset += 2;

			if (HasNullBitmap)
				offset = ParseNullBitmap(bytes, recordStart, ref offset);

			if (HasVariableLengthColumns)
				ParseVariableLengthColumns(bytes, recordStart, ref offset);
		}

        public PagePointer PageId { get; set; }

        private void parseStatusBitsA(byte bits)
		{
			// Bit 0 unknown - probably versioning bit as in primary records

			// Bits 1-3 represents record type
			Type = (RecordType)((bits >> 1) & 7);

			// Bit 4 determines whether a null bitmap is present
			HasNullBitmap = (bits & 0x10) != 0;

			// Bit 5 determines whether there are variable length columns
			HasVariableLengthColumns = (bits & 0x20) != 0;

			// Bits 6-7 not used
		}
	}
}
