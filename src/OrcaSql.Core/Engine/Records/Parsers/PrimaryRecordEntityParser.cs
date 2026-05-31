using System.Collections.Generic;
using System.Linq;
using System;
using OrcaSql.Core.Engine.Pages;
using OrcaSql.Core.Engine.Records.VariableLengthDataProxies;
using OrcaSql.Core.Engine.SqlTypes;
using OrcaSql.Core.MetaData;

namespace OrcaSql.Core.Engine.Records.Parsers
{
	internal class PrimaryRecordEntityParser : RecordEntityParser
	{
		private readonly PrimaryRecordPage page;
		private readonly CompressionContext compression;
        private static readonly HashSet<RecordType> _recordsToSkip;

        static PrimaryRecordEntityParser()
        {
            _recordsToSkip = new HashSet<RecordType>(new[]
            {
                RecordType.BlobFragment,
                RecordType.GhostIndex,
                RecordType.GhostData,
                RecordType.GhostVersion
            });
        }

		internal PrimaryRecordEntityParser(PrimaryRecordPage page, CompressionContext compression)
		{
			this.page = page;
			this.compression = compression;
		}

        internal override IEnumerable<Row> GetEntities(DataExtractorHelper schema)
        {
            var columns = schema.Columns.ToArray();
            var nonSparseIndexes = new int[columns.Length];
            var isDroppedColumn = new bool[columns.Length];
            var sqlTypes = new ISqlType[columns.Length];

            for (var i = 0; i < columns.Length; i++)
            {
                var col = columns[i];
                nonSparseIndexes[i] = col.IsSparse ? -1 : schema.NonSparseIndexes[col.Name];
                isDroppedColumn[i] = schema.IsDroppedColumn(col);

                if (col.UnderlyingType != ColumnType.Bit)
                    sqlTypes[i] = SqlTypeFactory.Create(col, null, compression);
            }

            foreach (var record in page.Records)
            {
                // Don't process forwarded blob fragments as they should only be processed from the referenced record
                if (_recordsToSkip.Contains(record.Type) || record.IsGhostForwardedRecord)
                    continue;

                short fixedOffset = 0;
                short variableColumnIndex = 0;
                var dataRow = schema.NewRow();
                var readState = new RecordReadState(schema.BitColumnsCount);
                var bitColumnBytes = Array.Empty<byte>();

                for (var columnIndex = 0; columnIndex < columns.Length; columnIndex++)
                {
                    var col = columns[columnIndex];
                    var sqlType = sqlTypes[columnIndex] ?? SqlTypeFactory.Create(col, readState, compression);
                    object columnValue = null;

                    // Sparse columns needs to retrieve their values from the sparse vector, contained in the very last
                    // variable length column in the record.
                    if (col.IsSparse)
                    {
                        // We may encounter records that don't have any sparse vectors, for instance if no sparse columns have values
                        if (record.SparseVector != null)
                        {
                            // Column ID's are stored as ints in general. In the sparse vector though, they're stored as shorts.
                            if (record.SparseVector.ColumnValues.ContainsKey((short)col.ColumnID))
                                columnValue = sqlType.GetValue(record.SparseVector.ColumnValues[(short)col.ColumnID]);
                        }
                    }
                    else
                    {
                        var nonSparseIndex = nonSparseIndexes[columnIndex];
                        // Before we even try to parse the column & make a null bitmap lookup, ensure that it's present in the record.
                        // There may be columns > record.NumberOfColumns caused by nullable columns added to the schema after the record was written.
                        if (nonSparseIndex < record.NumberOfColumns && col.UnderlyingType != ColumnType.Computed)
                        {
                            if (sqlType.IsVariableLength)
                            {
                                // If there's either no null bitmap, or the null bitmap defines the column as non-null.
                                if (!record.HasNullBitmap || !record.NullBitmap[nonSparseIndex])
                                {
                                    // If the current variable length column index exceeds the number of stored
                                    // variable length columns, the value is empty by definition (that is, 0 bytes, but not null).
                                    if (variableColumnIndex < record.NumberOfVariableLengthColumns)
                                    {
                                        if (record.VariableLengthColumnData.TryGetValue(variableColumnIndex, out var proxy))
                                        {
                                            if (TryReadVariableLengthValue(col, proxy, out var fastValue))
                                                columnValue = fastValue;
                                            else
                                            {
                                                var data = proxy.GetBytes();
                                                columnValue = sqlType.GetValue(data ?? Array.Empty<byte>());
                                            }
                                        }
                                        else
                                            columnValue = sqlType.GetValue(Array.Empty<byte>());
                                    }
                                    else
                                        columnValue = sqlType.GetValue(Array.Empty<byte>());
                                }

                                variableColumnIndex++;
                            }
                            else
                            {
                                // Must cache type FixedLength as it may change after getting a value (e.g. SqlBit)
                                var fixedLength = sqlType.FixedLength.Value;

                                if ((!record.HasNullBitmap || !record.NullBitmap[nonSparseIndex]) && col.UnderlyingType != ColumnType.Bit)
                                {
                                    // We may run out of fixed length bytes. In certain conditions a null integer may have been added without
                                    // there being a null bitmap. In such a case, we detect the null condition by there not being enough fixed
                                    // length bytes to process.
                                    if (TryReadFixedLengthValue(col, record.FixedLengthData, fixedOffset, fixedLength, compression, out var fastValue))
                                    {
                                        columnValue = fastValue;
                                    }
                                    else
                                    {
                                        var valueBytes = ReadBytes(record.FixedLengthData, fixedOffset, fixedLength);

                                        if (valueBytes.Length == fixedLength || (compression.CompressionLevel != CompressionLevel.None && valueBytes.Length > 0))
                                        {
                                            columnValue = GetFixedLengthValue(col, sqlType, valueBytes);
                                        }
                                    }
                                }
                                else if(col.UnderlyingType == ColumnType.Bit && !isDroppedColumn[columnIndex])
                                {
                                    if (readState.IsFirstBit)
                                        bitColumnBytes = ReadBytes(record.FixedLengthData, fixedOffset, fixedLength);

                                    var value = sqlType.GetValue(bitColumnBytes);
                                    columnValue = !record.HasNullBitmap || !record.NullBitmap[nonSparseIndex] ? value : null;
                                }

                                fixedOffset += fixedLength;
                            }
                        }
                        else if (col.UnderlyingType == ColumnType.Computed)
                        {
                            columnValue = sqlType.GetValue(null);
                        }
                        else if(!col.IsNullable)
                        {
                            columnValue = schema.GetDefaultValue(col, sqlType);
                        }
                    }

                    if(!isDroppedColumn[columnIndex])
                        dataRow.SetValueUnchecked(col, columnValue);
                }

                yield return dataRow;
            }
        }

        internal override PagePointer NextPage => page.Header.NextPage;

        private static byte[] ReadBytes(byte[] source, int offset, int length)
        {
            if (length <= 0 || offset >= source.Length)
                return Array.Empty<byte>();

            var available = Math.Min(length, source.Length - offset);
            var result = new byte[available];
            Buffer.BlockCopy(source, offset, result, 0, available);
            return result;
        }

        private static bool TryReadFixedLengthValue(DataColumn col, byte[] source, int offset, int length,
            CompressionContext compression, out object value)
        {
            value = null;

            if (compression.CompressionLevel != CompressionLevel.None || offset + length > source.Length)
                return false;

            switch (col.UnderlyingType)
            {
                case ColumnType.BigInt:
                    if (length != 8) return false;
                    value = LittleEndian.ReadInt64(source, offset);
                    return true;

                case ColumnType.Int:
                    if (length != 4) return false;
                    value = LittleEndian.ReadInt32(source, offset);
                    return true;

                case ColumnType.SmallInt:
                    if (length != 2) return false;
                    value = LittleEndian.ReadInt16(source, offset);
                    return true;

                case ColumnType.TinyInt:
                    if (length != 1) return false;
                    value = source[offset];
                    return true;

                case ColumnType.DateTime:
                    if (length != 8) return false;
                    try
                    {
                        value = new DateTime(1900, 1, 1)
                            .AddMilliseconds(LittleEndian.ReadInt32(source, offset) * (10d / 3d))
                            .AddDays(LittleEndian.ReadInt32(source, offset + 4));
                    }
                    catch (ArgumentOutOfRangeException) when (col.IsNullable)
                    {
                        value = null;
                    }
                    return true;

                case ColumnType.Date:
                    if (length != 3) return false;
                    value = DateTime.MinValue.AddDays(source[offset] + (source[offset + 1] << 8) + (source[offset + 2] << 16));
                    return true;

                case ColumnType.Money:
                    if (length != 8) return false;
                    value = LittleEndian.ReadInt64(source, offset) / 10000m;
                    return true;

                case ColumnType.SmallMoney:
                    if (length != 4) return false;
                    value = LittleEndian.ReadInt32(source, offset) / 10000m;
                    return true;

                case ColumnType.UniqueIdentifier:
                    if (length != 16) return false;
                    value = new Guid(
                        LittleEndian.ReadInt32(source, offset),
                        LittleEndian.ReadInt16(source, offset + 4),
                        LittleEndian.ReadInt16(source, offset + 6),
                        source[offset + 8],
                        source[offset + 9],
                        source[offset + 10],
                        source[offset + 11],
                        source[offset + 12],
                        source[offset + 13],
                        source[offset + 14],
                        source[offset + 15]);
                    return true;

                case ColumnType.NChar:
                    value = System.Text.Encoding.Unicode.GetString(source, offset, length);
                    return true;

                case ColumnType.Char:
                    if (col.Encoding == null) return false;
                    value = col.Encoding.GetString(source, offset, length);
                    return true;

                default:
                return false;
            }
        }

        private static bool TryReadVariableLengthValue(DataColumn col, IVariableLengthDataProxy proxy, out object value)
        {
            value = null;

            var raw = proxy as RawByteProxy;
            if (raw == null)
                return false;

            switch (col.UnderlyingType)
            {
                case ColumnType.NVarchar:
                    value = System.Text.Encoding.Unicode.GetString(raw.Source, raw.Offset, raw.Length);
                    return true;

                case ColumnType.Varchar:
                    if (col.Encoding == null) return false;
                    value = col.Encoding.GetString(raw.Source, raw.Offset, raw.Length);
                    return true;

                default:
                    return false;
            }
        }

        private static object GetFixedLengthValue(DataColumn col, ISqlType sqlType, byte[] valueBytes)
        {
            try
            {
                return sqlType.GetValue(valueBytes);
            }
            catch (ArgumentOutOfRangeException) when (col.IsNullable && col.UnderlyingType == ColumnType.DateTime)
            {
                return null;
            }
        }
    }
}
