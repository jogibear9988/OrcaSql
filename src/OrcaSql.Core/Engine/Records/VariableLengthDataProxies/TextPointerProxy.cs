using System;
using System.Collections.Generic;
using System.Linq;
using OrcaSql.Core.Engine.Pages;
using OrcaSql.Core.Engine.Records.LobStructures;

namespace OrcaSql.Core.Engine.Records.VariableLengthDataProxies
{
	public class TextPointerProxy : DataProxy, IVariableLengthDataProxy
	{
		private byte[] bytes;
		private int timestamp;
		private SlotPointer lobRootSlot;

		public TextPointerProxy(Page page, byte[] bytes)
			: base(page)
		{
			this.bytes = bytes;

			/* 16 byte LOB Textpointer:
			 * 
			 * Bytes	Content
			 * 0-3		Timestamp (int)
			 * 4-7		?
			 * 8-16		Slot pointer
			*/

			timestamp = LittleEndian.ReadInt32(bytes, 0);
			lobRootSlot = new SlotPointer(bytes, bytes.Length == 24 ? 16 : 8);
		}
		
		public byte[] GetBytes()
		{
			// Get root lob structure bytes
			var rootLobRecord = OriginPage.Database.GetTextRecord(lobRootSlot);
			var rootLobStructure = LobStructureFactory.Create(rootLobRecord.FixedLengthData, OriginPage.Database);

			return rootLobStructure.GetData();
		}
	}
}
