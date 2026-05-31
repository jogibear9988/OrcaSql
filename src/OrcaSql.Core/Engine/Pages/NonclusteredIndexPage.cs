using System.Collections.Generic;
using System.Linq;
using OrcaSql.Core.Engine.Records;
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
						if (!record.HasNullBitmap || !record.NullBitmap[columnIndex])
						{
							// If a nullable varlength column does not have a value, it may be not even appear in the varlength column array if it's at the tail
							if (record.VariableLengthColumnData.Count <= variableColumnIndex)
								columnValue = sqlType.GetValue(new byte[] { });
							else
								columnValue = sqlType.GetValue(record.VariableLengthColumnData[variableColumnIndex].GetBytes());
						}

						variableColumnIndex++;
					}
					else
					{
						// Must cache type FixedLength as it may change after getting a value (e.g. SqlBit)
						short fixedLength = sqlType.FixedLength.Value;

						if (!record.HasNullBitmap || !record.NullBitmap[columnIndex])
							columnValue = sqlType.GetValue(record.FixedLengthData.Skip(fixedOffset).Take(fixedLength).ToArray());

						fixedOffset += fixedLength;
					}

					dataRow.SetValueUnchecked(col, columnValue);
				}

				yield return dataRow;
			}
		}
	}
}
