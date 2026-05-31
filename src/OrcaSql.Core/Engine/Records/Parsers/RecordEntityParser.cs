using System;
using System.Collections.Generic;
using OrcaSql.Core.Engine.Pages;
using OrcaSql.Core.MetaData;

namespace OrcaSql.Core.Engine.Records.Parsers
{
	internal abstract class RecordEntityParser
	{
		internal abstract IEnumerable<Row> GetEntities(DataExtractorHelper schema);
		internal abstract PagePointer NextPage { get; }
		
		internal static RecordEntityParser CreateEntityParserForPage(PagePointer loc, CompressionContext compression, Database database, bool isSysTable = true)
		{
			return CreateEntityParserForPage(loc, null, compression, database, isSysTable);
		}

		internal static RecordEntityParser CreateEntityParserForPage(PagePointer loc, byte[] pageBytes, CompressionContext compression, Database database, bool isSysTable = true)
		{
			switch (compression.CompressionLevel)
			{
				case CompressionLevel.Page:
					throw new NotImplementedException("Page compression not yet supported.");

				case CompressionLevel.Row:
					return new CompressedRecordEntityParser(pageBytes == null
						? database.GetCompressedRecordPage(loc, compression)
						: new CompressedRecordPage(pageBytes, compression, database));

				case CompressionLevel.None:
					return new PrimaryRecordEntityParser(pageBytes == null
						? database.GetPrimaryRecordPage(loc, compression, isSysTable)
						: new PrimaryRecordPage(pageBytes, compression, database), compression);

				default:
					throw new ArgumentException("Unsupported compression level: " + compression.CompressionLevel);
			}
		}
	}
}
