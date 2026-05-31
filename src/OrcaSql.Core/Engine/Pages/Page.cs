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
