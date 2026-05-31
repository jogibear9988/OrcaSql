using System;
using System.IO;
using OrcaSql.Core.Engine.Pages;
using OrcaSql.Core.Engine.Records.LobStructures;

namespace OrcaSql.Core.Engine.Records.VariableLengthDataProxies
{
	public class BlobInlineRootProxy : DataProxy, IVariableLengthDataProxy
	{
		private byte complexColumnType;
		private short indexLevel;
		private byte unused;
		private int sequence;
		private long timestamp;
		private byte[] data;

		public BlobInlineRootProxy(Page page, byte[] data)
			: base(page)
		{
			this.data = data;

			// Parsed according to table 7-1 (p. 378) in [SQL Server 2008 Internals]
			complexColumnType = data[0];
			indexLevel = LittleEndian.ReadInt16(data, 1);
			unused = data[3];
			sequence = LittleEndian.ReadInt32(data, 4);

			// Technically a 6-byte long value. Low two bytes always zero, thus not stored (http://bit.ly/mdAQpm)
			timestamp = LittleEndian.ReadUInt32(data, 8) << 16;
		}

		public byte[] GetBytes()
		{
			using (var fieldData = new MemoryStream())
			{
				for (int i = 12; i < data.Length; i += 12)
				{
					int length = LittleEndian.ReadInt32(data, i);
					int pageID = LittleEndian.ReadInt32(data, i + 4);
					short fileID = LittleEndian.ReadInt16(data, i + 8);
					short slot = LittleEndian.ReadInt16(data, i + 10);

					// Get referenced page data
					var referencedData = OriginPage.Database.GetTextRecord(new SlotPointer(fileID, pageID, slot)).FixedLengthData;

					// Get lob structure and retrieve data
					var lobStructure = LobStructureFactory.Create(referencedData, OriginPage.Database);
					var bytes = lobStructure.GetData();

					if (bytes != null && bytes.Length > 0)
						fieldData.Write(bytes, 0, bytes.Length);
				}

				return fieldData.ToArray();
			}
		}
	}
}
