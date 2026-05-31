using System;
using OrcaSql.Core.Engine.Pages;
using OrcaSql.Core.Engine.Records.LobStructures;

namespace OrcaSql.Core.Engine.Records
{
	public class TextRecord : Record
	{
		public bool HasVersioningInformation { get; private set; }
		public bool IsGhostForwardedRecord { get; private set; }
		public long BlobID { get; private set; }
		public LobStructureType LobType { get; private set; }

		public TextRecord(byte[] bytes, Page page)
			: this(bytes, 0, page)
		{
		}

		public TextRecord(byte[] bytes, int recordOffset, Page page)
			: base(page)
		{
			var startOffset = recordOffset;
			var offset = recordOffset;
			
			// Parse status bits, even though we currently ignore their values.
			// Only one I can currently imagine being relevant is HasVersioningInformation.
			parseStatusBitsA(bytes[offset++]);
			parseStatusBitsB(bytes[offset++]);

			// Read the fixed length portion
			short fixedLengthSize = LittleEndian.ReadInt16(bytes, offset);
            if (fixedLengthSize == 0) return;
			fixedLengthSize -= 4;
			offset += 2;

			FixedLengthData = CopyBytes(bytes, offset, fixedLengthSize);
			offset += fixedLengthSize;

			// No matter the text structure, they all have a common 14 byte header
			parseCommonTextStructureHeader();

			// Save complete record raw bytes
			RawBytes = CopyBytes(bytes, startOffset, offset - startOffset);
		}

		private void parseCommonTextStructureHeader()
		{
			/* In all observed text structures, I've seen the following common header. Until
			 * I see one with a different format, I'll assume they all share this 10 byte header structure.
			 * The status bits + fixed data length are common for all records, so the only special
			 * fields in the header is the blob ID as well as the type. Note that byte 0 is offset from
			 * the fixed data start, thus it'll be byte index 4 in the actual record.
			 * 
			 * Byte		Content
			 * 0-7		Small blob ID (long)
			 * 8-9		Type (short)
			 */

			BlobID = LittleEndian.ReadInt64(FixedLengthData, 0);
			short type = LittleEndian.ReadInt16(FixedLengthData, 8);

			if(Enum.IsDefined(typeof(LobStructureType), type))
				LobType = (LobStructureType)type;
			else
				throw new ArgumentException("Invalid LOB record type encountered: " + type);
		}

		private void parseStatusBitsA(byte bits)
		{
			// Bit 0 (versioning bit) we don't care about as it's always 0 in 2k8+

			// Bits 1-3 represents record type
			Type = (RecordType)((bits >> 1) & 7);

			// Bit 4 determines whether a null bitmap is present
			HasNullBitmap = (bits & 0x10) != 0;

			// Bit 5 determines whether there are variable length columns
			HasVariableLengthColumns = (bits & 0x20) != 0;

			// Bit 6 determines whether the row contains versioning information
			HasVersioningInformation = (bits & 0x40) != 0;

			// Bit 7 isn't used in 2k8+
		}

		private void parseStatusBitsB(byte bits)
		{
			// As the 'Ghost forwarded record' bit is the only one stored in the second byte,
			// we can simply read the whole byte value instead of extracting the first
			// bit explicitly.
			IsGhostForwardedRecord = bits == 1;
		}
	}
}
