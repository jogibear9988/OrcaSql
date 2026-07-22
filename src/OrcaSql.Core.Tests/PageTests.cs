using NUnit.Framework;
using OrcaSql.Core.Engine.Pages;

namespace OrcaSql.Core.Tests
{
	public class PageTests
	{
		[Test]
		public void RestoresBitsDisplacedByTornPageDetection()
		{
			var bytes = new byte[8192];
			bytes[4] = 0x00;
			bytes[5] = 0x01;

			uint tornBits = 0;
			for (var sector = 1; sector < 16; sector++)
			{
				var originalBits = (byte)(sector % 4);
				tornBits |= (uint)originalBits << (sector * 2);
				bytes[sector * 512 + 511] = 0xa9;
			}

			bytes[60] = (byte)tornBits;
			bytes[61] = (byte)(tornBits >> 8);
			bytes[62] = (byte)(tornBits >> 16);
			bytes[63] = (byte)(tornBits >> 24);

			var page = new Page(bytes, null);

			for (var sector = 1; sector < 16; sector++)
				Assert.AreEqual(0xa8 | sector % 4, page.RawBytes[sector * 512 + 511]);
		}

		[Test]
		public void LeavesSectorBytesUnchangedWithoutTornPageDetection()
		{
			var bytes = new byte[8192];
			for (var sector = 1; sector < 16; sector++)
				bytes[sector * 512 + 511] = 0xa9;

			var page = new Page(bytes, null);

			for (var sector = 1; sector < 16; sector++)
				Assert.AreEqual(0xa9, page.RawBytes[sector * 512 + 511]);
		}
	}
}
