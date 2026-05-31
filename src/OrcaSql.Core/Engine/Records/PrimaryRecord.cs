using System;
using OrcaSql.Core.Engine.Pages;

namespace OrcaSql.Core.Engine.Records
{
	public class PrimaryRecord : Record
	{
		public bool HasVersioningInformation { get; private set; }
		public bool IsGhostForwardedRecord { get; private set; }

		public PrimaryRecord(byte[] bytes, Page page)
			: base(page)
		{
			short offset = 0;

			// Parse status bits A
			parseStatusBitsA(bytes[offset++]);

			if (SkipDataParsing(Type))
			{
				if (bytes.Length > offset)
					parseStatusBitsB(bytes[offset++]);

				FixedLengthData = new byte[0];
				RawBytes = CopyBytes(bytes, 0, offset);
				return;
			}

			// TODO: Strategize this stuff to avoid ifs, switches & impersonation
			if(Type == RecordType.ForwardingStub)
			{
				// Forwarding stub only has one status byte. Remaining 8 bytes are for (PageID, FileID, Slot)
				FixedLengthData = CopyBytes(bytes, 1, 8);

				if (FixedLengthData.Length < 8)
				{
					IsGhostForwardedRecord = true;
					RawBytes = bytes;
					return;
				}
				
				int pageID = LittleEndian.ReadInt32(bytes, 1);
				short fileID = LittleEndian.ReadInt16(bytes, 5);
				short slot = LittleEndian.ReadInt16(bytes, 7);

				if (fileID <= 0 || pageID <= 0 || slot < 0 || page?.Database == null || !page.Database.Files.ContainsKey(fileID))
				{
					IsGhostForwardedRecord = true;
					RawBytes = CopyBytes(bytes, 0, 9);
					return;
				}

				var forwardPage = page.Database.GetPrimaryRecordPage(new PagePointer(fileID, pageID), CompressionContext.NoCompression);
				if (slot >= forwardPage.Records.Length)
				{
					IsGhostForwardedRecord = true;
					RawBytes = CopyBytes(bytes, 0, 9);
					return;
				}

				byte[] forwardedRecordBytes = forwardPage.Records[slot].RawBytes;

				parseStatusBitsA(forwardedRecordBytes[0]);
				bytes = forwardedRecordBytes;

				// We'll impersonate the ForwardingStub record type that we originated from, this allows
				// the engine to distinguish BlobFragments and the records that actually reference them.
				Type = RecordType.ForwardingStub;
			}

			// Parse status bits B
			parseStatusBitsB(bytes[offset++]);

			// Parse fixed length size
			short fixedLengthOffset = LittleEndian.ReadInt16(bytes, offset);
			if (fixedLengthOffset < 4 || fixedLengthOffset + 2 > bytes.Length)
			{
				IsGhostForwardedRecord = true;
				FixedLengthData = new byte[0];
				RawBytes = CopyBytes(bytes, 0, offset + 2);
				return;
			}

			short fixedLengthSize = (short)(fixedLengthOffset - 4);
			offset += 2;

			// Parse fixed length data
			FixedLengthData = CopyBytes(bytes, offset, fixedLengthSize);
			offset += fixedLengthSize;

			// Parse number of columns
			NumberOfColumns = LittleEndian.ReadInt16(bytes, offset);
			offset += 2;

			try
			{
				// Parse null bitmap, if present
				if (HasNullBitmap)
					offset = ParseNullBitmap(bytes, ref offset);

				// Parse variable length columns, if present
				if (HasVariableLengthColumns)
					ParseVariableLengthColumns(bytes, ref offset);
			}
			catch (Exception ex) when (ex is ArgumentOutOfRangeException
			                           || ex is IndexOutOfRangeException
			                           || ex is OverflowException
			                           || ex is ArgumentException)
			{
				IsGhostForwardedRecord = true;
				FixedLengthData = new byte[0];
				RawBytes = CopyBytes(bytes, 0, Math.Min(bytes.Length, offset));
				return;
			}

			// Save complete record raw bytes
			RawBytes = CopyBytes(bytes, 0, offset);
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

		internal static bool SkipDataParsing(RecordType type)
		{
			return type == RecordType.BlobFragment
			       || type == RecordType.GhostIndex
			       || type == RecordType.GhostData
			       || type == RecordType.GhostVersion;
		}
	}
}
