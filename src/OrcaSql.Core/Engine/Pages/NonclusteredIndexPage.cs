using System.Collections.Generic;
using System.Linq;
using OrcaSql.Core.Engine.Records;
using OrcaSql.Core.Engine.Records.VariableLengthDataProxies;
using OrcaSql.Core.Engine.SqlTypes;
using OrcaSql.Core.MetaData;

namespace OrcaSql.Core.Engine.Pages
{
	internal class NonclusteredIndexPage : IndexRecordPage
	{
		internal NonclusteredIndexPage(byte[] bytes, Database database)
			: base(bytes, database)
		{ }

		internal IEnumerable<Row> GetEntities(Row schema, CompressionContext compression)
		{
			var columns = schema.Columns.ToArray();
			var sqlTypes = new ISqlType[columns.Length];
			var bitColumnsCount = columns.Count(x => x.UnderlyingType == ColumnType.Bit);

			for (var columnIndex = 0; columnIndex < columns.Length; columnIndex++)
			{
				var col = columns[columnIndex];
				if (col.UnderlyingType != ColumnType.Bit)
					sqlTypes[columnIndex] = SqlTypeFactory.Create(col, null, compression);
			}

			for (int i = 0; i < Records.Length; i++)
			{
				var record = Records[i];

				short fixedOffset = 0;
				short variableColumnIndex = 0;
				var readState = new RecordReadState(bitColumnsCount);
				var dataRow = schema.NewRow();

				for (var columnIndex = 0; columnIndex < columns.Length; columnIndex++)
				{
					var col = columns[columnIndex];
					var sqlType = sqlTypes[columnIndex] ?? SqlTypeFactory.Create(col, readState, compression);
					object columnValue = null;

					if (sqlType.IsVariableLength)
					{
						if (!record.HasNullBitmap || !record.IsNull(columnIndex))
						{
							// If a nullable varlength column does not have a value, it may be not even appear in the varlength column array if it's at the tail
							if (record.VariableLengthColumnData.Length <= variableColumnIndex)
								columnValue = sqlType.GetValue(System.Array.Empty<byte>());
							else
								columnValue = GetVariableLengthValue(sqlType, record.VariableLengthColumnData[variableColumnIndex]);
						}

						variableColumnIndex++;
					}
					else
					{
						// Must cache type FixedLength as it may change after getting a value (e.g. SqlBit)
						short fixedLength = sqlType.FixedLength.Value;

						if (!record.HasNullBitmap || !record.IsNull(columnIndex))
							columnValue = sqlType.GetValue(new System.ReadOnlySpan<byte>(record.FixedLengthData, fixedOffset, fixedLength));

						fixedOffset += fixedLength;
					}

					dataRow.SetValueUnchecked(col, columnValue);
				}

				yield return dataRow;
			}
		}

		private static object GetVariableLengthValue(ISqlType sqlType, IVariableLengthDataProxy proxy)
		{
			var raw = proxy as RawByteProxy;
			return raw != null
				? sqlType.GetValue(new System.ReadOnlySpan<byte>(raw.Source, raw.Offset, raw.Length))
				: sqlType.GetValue(proxy.GetBytes());
		}
	}
}
