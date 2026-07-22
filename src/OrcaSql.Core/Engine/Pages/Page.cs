using System;

namespace OrcaSql.Core.Engine.Pages
{
	public class Page
	{
		public Database Database { get; private set; }
		public PageHeader Header;

		// Raw content of the page (8192 bytes)
		public byte[] RawBytes { get; private set; }

		public Page(byte[] bytes, Database database)
		{
			if (bytes.Length != 8192)
				throw new ArgumentException("bytes");

			Database = database;
			RawBytes = bytes;
			Header = new PageHeader(RawBytes, 0);
			RestoreTornPageBits();
		}

		private void RestoreTornPageBits()
		{
			// With TORN_PAGE_DETECTION SQL Server stores the original low two
			// bits of each 512-byte sector's final byte in the page header, then
			// replaces those bits on disk with an alternating write signature.
			// Restore the displaced bits before record and slot parsing.
			if ((Header.FlagBits & 0x100) == 0)
				return;

			var tornBits = LittleEndian.ReadUInt32(RawBytes, 60);
			for (var sector = 1; sector < 16; sector++)
			{
				var byteOffset = sector * 512 + 511;
				var originalBits = (byte)(tornBits >> (sector * 2) & 0x03);
				RawBytes[byteOffset] = (byte)(RawBytes[byteOffset] & 0xfc | originalBits);
			}
		}

		public byte[] RawHeader
		{
			get
			{
				var header = new byte[96];
				Buffer.BlockCopy(RawBytes, 0, header, 0, header.Length);
				return header;
			}
		}

        public byte[] RawBody
        {
            get
            {
                var body = new byte[RawBytes.Length - 96];
                Buffer.BlockCopy(RawBytes, 96, body, 0, body.Length);
                return body;
            }
        }

        public override string  ToString()
		{
			return "{" + Header.Type + " (" + Header.Pointer.FileID + ":" + Header.Pointer.PageID + ")}";
		}
	}
}
