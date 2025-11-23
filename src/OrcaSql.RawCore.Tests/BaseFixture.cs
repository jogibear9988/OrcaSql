using NUnit.Framework;
using System;

namespace OrcaSql.RawCore.Tests
{
	public class BaseFixture
	{
		protected const string AW2012Path = @"TestDatabases\AWLT2012.mdf";
		protected const string AW2008R2Path = @"TestDatabases\AWLT2008R2.mdf";
		protected const string AW2008Path = @"TestDatabases\AWLT2008.mdf";
		protected const string AW2005Path = @"TestDatabases\AWLT2005.mdf";

        [SetUp]
        public void SetUp()
        {
            Environment.CurrentDirectory = TestContext.CurrentContext.TestDirectory;
        }
    }
}