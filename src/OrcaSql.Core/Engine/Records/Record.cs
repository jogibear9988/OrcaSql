using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using OrcaSql.Core.Engine.Pages;
using OrcaSql.Core.Engine.Records.VariableLengthDataProxies;

namespace OrcaSql.Core.Engine.Records
{
	public abstract class Record
	{
		public RecordType Type { get; protected set; }
		public bool HasNullBitmap { get; protected set; }
		public bool HasVariableLengthColumns { get; protected set; }
		public byte[] FixedLengthData { get; protected set; }
		public short NumberOfColumns { get; protected set; }
		public BitArray NullBitmap { get; protected set; }
		public short NumberOfVariableLengthColumns { get; protected set; }
		public byte[] RawBytes { get; protected set; }
		public SparseVectorParser SparseVector { get; private set; }
		public IDictionary<int, IVariableLengthDataProxy> VariableLengthColumnData { get; set; }

		protected Page Page;

		protected Record(Page page)
		{
			Page = page;

			// Initialize variable length data dictionaries
			VariableLengthColumnData = new Dictionary<int, IVariableLengthDataProxy>();
		}

		protected void ParseVariableLengthColumns(byte[] bytes, int recordStart, ref short offset)
		{
			// If there is no fixed length data and no null bitmap, only the number of variable length columns is stored.
			if (FixedLengthData.Length == 0 && !HasNullBitmap)
				NumberOfVariableLengthColumns = NumberOfColumns;
			else
			{
				NumberOfVariableLengthColumns = LittleEndian.ReadInt16(bytes, recordStart + offset);
				offset += 2;
			}

			VariableLengthColumnData = new Dictionary<int, IVariableLengthDataProxy>(NumberOfVariableLengthColumns);

			short[] variableLengthColumnLengths = new short[NumberOfVariableLengthColumns];
			for (int i = 0; i < NumberOfVariableLengthColumns; i++)
			{
				variableLengthColumnLengths[i] = LittleEndian.ReadInt16(bytes, recordStart + offset);
				offset += 2;
			}

			// Loop variable length columns
			for(int i=0; i<NumberOfVariableLengthColumns; i++)
			{
				// The high order bit is used to indicate a complex column (or a row-overflow pointer).
				bool complexColumn = false;
				if ((variableLengthColumnLengths[i] & 32768) == 32768)
				{
					// Flip the sign bit && remember that this is a complex column
					variableLengthColumnLengths[i] = (short)(variableLengthColumnLengths[i] & Int16.MaxValue);
					complexColumn = true;
				}

				var rawOffset = recordStart + offset;
				var rawLength = variableLengthColumnLengths[i] - offset;
				offset = variableLengthColumnLengths[i];
				if (rawLength <= 0)
				{
					if (!complexColumn)
						VariableLengthColumnData[i] = new RawByteProxy(Array.Empty<byte>());

					continue;
				}

				// Complex columns store special values and may need to be read elsewhere. In this case I'm using somewhat of a hack to detect
				// row-overflow pointers the same way as normal complex columns. See http://improve.dk/archive/2011/07/15/identifying-complex-columns-in-records.aspx
				// for a better description of the issue. Currently there are three cases:
				// - Back pointers (two-byte value of 1024)
				// - Sparse vectors (two-byte value of 5)
				// - BLOB Inline Root (one-byte value of 4)
				// - Row-overflow pointer (one-byte value of 2)
				// First we'll try to read just the very first pointer - hitting case values like 5 and 2. 1024 will result in a value of 0. In that specific
				// case we then try to read a two-byte value.
				// Finally complex columns also store LOB pointers. Since these do not store a complex column type ID,
				// we'll use the known 16-byte and 24-byte pointer lengths to detect them and retrieve the referenced data.
				if (complexColumn)
				{
					// SQL Server 2000 text/image pointers are 24 bytes, Yukon+ pointers are 16 bytes.
					if (rawLength == 16 || rawLength == 24)
						VariableLengthColumnData[i] = new TextPointerProxy(Page, CopyBytes(bytes, rawOffset, rawLength));
					else
					{
						short complexColumnID = bytes[rawOffset];

						if (complexColumnID == 0)
							complexColumnID = LittleEndian.ReadInt16(bytes, rawOffset);

						switch (complexColumnID)
						{
							// Row-overflow pointer, get referenced data
							case 2:
								VariableLengthColumnData[i] = new BlobInlineRootProxy(Page, CopyBytes(bytes, rawOffset, rawLength));
								break;

							// BLOB Inline Root
							case 4:
								VariableLengthColumnData[i] = new BlobInlineRootProxy(Page, CopyBytes(bytes, rawOffset, rawLength));
								break;

							// Sparse vectors will be processed at a later stage - no public option for accessing raw bytes
							case 5:
								SparseVector = new SparseVectorParser(CopyBytes(bytes, rawOffset, rawLength));
								break;

							// Forwarded record back pointer (http://improve.dk/archive/2011/06/09/anatomy-of-a-forwarded-record-ndash-the-back-pointer.aspx)
							// Ensure we expect a back pointer at this location. For forwarding stubs, the data stems from the referenced forwarded record. For the forwarded record,
							// the last varlength column is a backpointer. No public option for accessing raw bytes.
							case 1024:
								if ((Type == RecordType.ForwardingStub || Type == RecordType.BlobFragment) && i != NumberOfVariableLengthColumns - 1)
									throw new ArgumentException("Unexpected back pointer found at column index " + i);
								break;

							default:
								throw new ArgumentException("Invalid complex column ID encountered: 0x" + LittleEndian.ReadInt16(bytes, rawOffset).ToString("X"));
						}
					}
				}
				else
					VariableLengthColumnData[i] = new RawByteProxy(bytes, rawOffset, rawLength);
			}
		}

		protected short ParseNullBitmap(byte[] bytes, int recordStart, ref short offset)
		{
			NullBitmap = new BitArray(CopyBytes(bytes, recordStart + offset, (NumberOfColumns + 7)/8));
			offset += (short)((NumberOfColumns + 7) / 8);
			return offset;
		}

		protected static byte[] CopyBytes(byte[] source, int offset, int length)
		{
			if (length <= 0 || offset >= source.Length)
				return Array.Empty<byte>();

			var available = Math.Min(length, source.Length - offset);
			var result = new byte[available];
			Buffer.BlockCopy(source, offset, result, 0, available);
			return result;
		}
	}
}
