using System;
using OrcaSql.Core.Engine.Pages;
using OrcaSql.Core.Engine.Records.VariableLengthDataProxies;

namespace OrcaSql.Core.Engine.Records.Compression
{
	internal class CompressedRecord
	{
		private static readonly byte[] TrueBitBytes = { 1 };
		internal CompressedRecordFormat RecordFormat { get; private set; }
		internal bool HasVersioningInformation { get; private set; }
		internal CompressedRecordType RecordType { get; private set; }
		internal short NumberOfColumns { get; private set; }

		private readonly Page page;
		private readonly byte[] record;
		private readonly int recordStart;
		private bool containsLongDataRegion;
		private CompressedRecordColumnCDIndicator[] columnValueIndicators;
		private short[] shortDataRegionClusterPointers;
		private short[] longDataColumnPointers;
		private short[] longDataColumnLengths;

		internal CompressedRecord(byte[] record, Page page)
			: this(record, 0, record.Length, page)
		{
		}

		internal CompressedRecord(byte[] record, int recordStart, int recordLength, Page page)
		{
			this.record = record;
			this.recordStart = recordStart;
			this.page = page;

			short recordPointer = 1;

			parseHeader();
			parseCDRegion(ref recordPointer);
			parseShortDataRegion(ref recordPointer);
			parseLongDataRegion(ref recordPointer);
		}

		/// <summary>
		/// Returns the byte value of a given column index in the record. The value may be compressed, meaning an
		/// integer value will only use up as many bytes as required to store the value. Normalization may be needed
		/// before the normal type parsers can read the value.
		/// </summary>
		internal IVariableLengthDataProxy GetPhysicalColumnBytes(int index)
		{
			// Get column compression indicator
			var colDescription = columnValueIndicators[index];

			// If it's null, we'll just return it
			if (colDescription == CompressedRecordColumnCDIndicator.Null)
				return null;

			// We don't support page compression yet
			if (colDescription == CompressedRecordColumnCDIndicator.DictionarySymbol)
				throw new NotSupportedException();

			// If it's a true bit, 
			if (colDescription == CompressedRecordColumnCDIndicator.TrueBit)
				return new RawByteProxy(TrueBitBytes);

			// If it's zero-length
			if (colDescription == CompressedRecordColumnCDIndicator.ZeroByte)
				return new RawByteProxy(Array.Empty<byte>());

			// Is the data long or short?
			if (colDescription == CompressedRecordColumnCDIndicator.LongData)
			{
				// What's the long-data column index?
				int longIndex = 0;
				for (int i=0; i<index; i++)
					if (columnValueIndicators[i] == CompressedRecordColumnCDIndicator.LongData)
						longIndex++;

				// Is data stored as a complex value or as raw bytes?
				if ((longDataColumnLengths[longIndex] & 32768) > 0)
				{
					short actualLength = (short)(longDataColumnLengths[longIndex] & 32767);
					var dataOffset = recordStart + longDataColumnPointers[longIndex];

					// For length 16 we know it'll be a text pointer
					if (actualLength == 16)
						return new TextPointerProxy(page, CopyBytes(record, dataOffset, actualLength));

					// For other lengths we'll have to determine the type of complex column.
					// TODO: https://github.com/improvedk/OrcaMDF/issues/2
					short complexColumnID = record[dataOffset];

					if (complexColumnID == 0)
						complexColumnID = LittleEndian.ReadInt16(record, dataOffset);

					switch (complexColumnID)
					{
						// Row-overflow pointer, get referenced data
						case 2:
							return new BlobInlineRootProxy(page, CopyBytes(record, dataOffset, actualLength));

						// BLOB Inline Root
						case 4:
							return new BlobInlineRootProxy(page, CopyBytes(record, dataOffset, actualLength));

						// Back pointer
						case 1024:
							return new RawByteProxy(record, dataOffset, actualLength);

						default:
							throw new ArgumentException("Invalid complex column ID encountered: 0x" + LittleEndian.ReadInt16(record, dataOffset).ToString("X"));
					}
				}
				else
					return new RawByteProxy(record, recordStart + longDataColumnPointers[longIndex], longDataColumnLengths[longIndex]);
			}
			else
			{
				// Which cluster is the value stored in?
				int clusterIndex = index / 30;
				short recordPointer = shortDataRegionClusterPointers[clusterIndex];

				// Traverse columns within the cluster until we reach the desired index
				for (int i=clusterIndex*30; i<index; i++)
					if (columnValueIndicators[i] != CompressedRecordColumnCDIndicator.LongData)
						recordPointer += getLengthFromColumnIndicator(columnValueIndicators[i]);

				// Return the column value
				return new RawByteProxy(record, recordStart + recordPointer, getLengthFromColumnIndicator(colDescription));
			}
		}

		/// <summary>
		/// Returns the value length in bytes of a given column indicator. Only to be used with
		/// actual byte-length values, all others will fail.
		/// </summary>
		private byte getLengthFromColumnIndicator(CompressedRecordColumnCDIndicator indicator)
		{
			switch(indicator)
			{
				case CompressedRecordColumnCDIndicator.ZeroByte:
					return 0;
				case CompressedRecordColumnCDIndicator.OneByte:
					return 1;
				case CompressedRecordColumnCDIndicator.TwoByte:
					return 2;
				case CompressedRecordColumnCDIndicator.ThreeByte:
					return 3;
				case CompressedRecordColumnCDIndicator.FourByte:
					return 4;
				case CompressedRecordColumnCDIndicator.FiveByte:
					return 5;
				case CompressedRecordColumnCDIndicator.SixByte:
					return 6;
				case CompressedRecordColumnCDIndicator.SevenByte:
					return 7;
				case CompressedRecordColumnCDIndicator.EightByte:
					return 8;
			}

			throw new ArgumentException();
		}

		private void parseHeader()
		{
			byte header = record[recordStart];

			// Bit 0
			RecordFormat = (header & 0x1) > 0 ? CompressedRecordFormat.CD : CompressedRecordFormat.Unknown;

			// Bit 1
			HasVersioningInformation = (header & 0x2) > 0;

			// Bits 2-4
			RecordType = (CompressedRecordType)((header << 3) >> 5);

			// Bit 5
			containsLongDataRegion = (header & 0x20) > 0;

			// Bits 6-7 unused in SQL Server 2008
		}

		private void parseCDRegion(ref short recordPointer)
		{
			// If the high order bit of the first byte is set, numColumns is a two-byte value,
			// otherwise it's a one-byte value.
			byte firstByte = record[recordStart + recordPointer];
			if((firstByte & 0x80) > 0)
			{
				NumberOfColumns = LittleEndian.ReadInt16(record, recordStart + 1);
				recordPointer += 2;
			}
			else
				NumberOfColumns = record[recordStart + recordPointer++];

			// Next up we have 4 bits per column in the record. Loop all columns, alternating between reading
			// the first 4 bits, then the last 4 bits.
			columnValueIndicators = new CompressedRecordColumnCDIndicator[NumberOfColumns];
			for(int i=0; i<NumberOfColumns; i++)
				columnValueIndicators[i] = (CompressedRecordColumnCDIndicator)(i % 2 == 0 ? record[recordStart + recordPointer] & 0xF : ((record[recordStart + recordPointer++] & 0xF0) >> 4));

			// Make sure to increase recordPointer if we end up reading the first 4 bits as the last
			// column, and thus need to pad up to nearest byte.
			if(NumberOfColumns % 2 == 1)
				recordPointer++;
		}

		private void parseShortDataRegion(ref short recordPointer)
		{
			// Calculate the number of clusters in the record
			int numClusters = (NumberOfColumns - 1) / 30;

			// If there's less than 30 columns, no cluster lengths are stored
			if(numClusters == 0)
			{
				shortDataRegionClusterPointers = new short[1];
				shortDataRegionClusterPointers[0] = recordPointer;

				// Loop all short data fields to advance the record pointer to the end of the fixed length data
				foreach (var col in columnValueIndicators)
					if (col >= CompressedRecordColumnCDIndicator.OneByte && col <= CompressedRecordColumnCDIndicator.EightByte)
						recordPointer += getLengthFromColumnIndicator(col);
			}
			else
			{
				shortDataRegionClusterPointers = new short[numClusters];

				// Read the length of each cluster
				short[] clusterLengths = new short[numClusters];
				for (int i = 0; i < numClusters; i++)
					clusterLengths[i] = record[recordStart + recordPointer++];

				// The first cluster always starts right after the cluster length array
				shortDataRegionClusterPointers[0] = recordPointer;

				// Each consecutive cluster starts after the sum length of all previous clusters
				short cumulativeLength = 0;
				for (int i = 1; i < numClusters; i++)
				{
					cumulativeLength += clusterLengths[i - 1];
					shortDataRegionClusterPointers[i] = (short)(shortDataRegionClusterPointers[0] + cumulativeLength);
				}

				// Once all the cluster lengths have been read, forward the record pointer to the end of the data
				recordPointer = (short)(shortDataRegionClusterPointers[numClusters - 1] + clusterLengths[numClusters - 1]);
			}
		}

		private void parseLongDataRegion(ref short recordPointer)
		{
			if(!containsLongDataRegion)
				return;

			// Read header
			bool containsTwoByteOffsets = Convert.ToBoolean(record[recordStart + recordPointer] & 0x1);
			bool containsComplexColumns = Convert.ToBoolean(record[recordStart + recordPointer] & 0x2);
			recordPointer++;

			// Read number of entries
			short numEntries = LittleEndian.ReadInt16(record, recordStart + recordPointer);
			longDataColumnPointers = new short[numEntries];
			longDataColumnLengths = new short[numEntries];
			recordPointer += 2;

			// Read offset entries
			short[] offsetEntries = new short[numEntries];
			for (int i=0; i<numEntries; i++)
			{
				offsetEntries[i] = LittleEndian.ReadInt16(record, recordStart + recordPointer);
				recordPointer += 2;
			}

			// Read cluster counts
			int numClusters = (NumberOfColumns - 1) / 30;
			byte[] clusterCount = new byte[numClusters];

			// We don't care about the clusters for now, so we'll just forward the pointer right past them
			recordPointer += (short)numClusters;

			// For each long data column, calculate its starting index and length
			for (int i=0; i<numEntries; i++)
			{
				longDataColumnPointers[i] = recordPointer;
				longDataColumnLengths[i] = (short)(offsetEntries[i] - (i > 0 ? offsetEntries[i - 1] : (short)0));

				recordPointer += longDataColumnLengths[i];
			}
		}

		private static byte[] CopyBytes(byte[] source, int offset, int length)
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
