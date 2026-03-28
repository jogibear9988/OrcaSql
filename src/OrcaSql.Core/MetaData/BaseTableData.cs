using System;
using System.Collections.Generic;
using System.Linq;
using OrcaSql.Core.Engine;
using OrcaSql.Core.MetaData.BaseTables;
using OrcaSql.Core.MetaData.Enumerations;

namespace OrcaSql.Core.MetaData
{
	public class BaseTableData
	{
		private readonly Database _db;
		private readonly DataScanner _scanner;

		// These are crucial base tables that are eagerly scanned on instantiation
		public IList<sysallocunit> SysAllocUnits { get; private set; }
        public IList<syscolpar> SysColPars { get; private set; }
        public IList<sysschobj> SysSchObjs { get; private set; }
        public IList<sysscalartype> SysScalarTypes { get; private set; }
        public IList<sysrowset> SysRowsets { get; private set; }
        public IList<sysrscol> SysRsCols { get; private set; }
        public IList<syssingleobjref> SysSingleObjRefs { get; private set; }

		private IList<sysidxstat> _sysidxstats;
        public IList<sysidxstat> SysIdxStats => _sysidxstats ?? (_sysidxstats = _scanner.ScanTable<sysidxstat>(nameof(SysIdxStats).ToLowerInvariant()).ToList());

        private IList<sysclsobj> _sysclsobjs;
        public IList<sysclsobj> SysClsObjs => _sysclsobjs ?? (_sysclsobjs = _scanner.ScanTable<sysclsobj>(nameof(SysClsObjs).ToLowerInvariant()).ToList());

        private IList<syspalvalue> _syspalvalues;
        public IList<syspalvalue> SysPalValues => _syspalvalues ?? (_syspalvalues = syspalvalue.GetServer2008R2HardcodedValues());

        private IList<syspalname> _syspalnames;
        public IList<syspalname> SysPalNames => _syspalnames ?? (_syspalnames = syspalname.GetServer2008R2HardcodedValues());

        private IList<sysiscol> _sysiscols;
        public IList<sysiscol> SysIsCols => _sysiscols ?? (_sysiscols = _scanner.ScanTable<sysiscol>(nameof(SysIsCols).ToLowerInvariant()).ToList());

        private IList<sysobjvalue> _sysobjvalues;
        public IList<sysobjvalue> SysObjValues => _sysobjvalues ?? (_sysobjvalues = _scanner.ScanTable<sysobjvalue>(nameof(SysObjValues).ToLowerInvariant()).ToList());

        private IList<sysowner> _sysowners;
        public IList<sysowner> SysOwners => _sysowners ?? (_sysowners = _scanner.ScanTable<sysowner>(nameof(SysOwners).ToLowerInvariant()).ToList());

        public BaseTableData(Database db)
		{
			this._db = db;
			_scanner = new DataScanner(db);

			// These are the very core base tables that we'll need to dynamically construct the schema of any other
			// required tables. By aggresively parsing these, we can do lazy loading of the rest.
			parseSysallocunits();
			parseSysrowsets();
			parseSyscolpars();
			parseSysobjects();
			parseSysscalartypes();
			parseSysrscols();
			parseSyssingleobjrefs();
		}

        public Row GetSchemaRow<T>() where T : Row, new()
        {
            return new T();
        }

        private void parseSyssingleobjrefs()
		{
			long rowsetID = SysRowsets
				.Where(x => x.idmajor == (int)SystemObject.syssingleobjrefs && x.idminor == 1)
				.Single()
				.rowsetid;

			var au = SysAllocUnits
				.Where(x => x.auid == rowsetID && x.type == 1)
				.Single();

			SysSingleObjRefs = _scanner.ScanIamDataPages<syssingleobjref>(new PagePointer(au.pgfirstiam), CompressionContext.NoCompression).ToList();
		}

		private void parseSysrscols()
		{
			long rowsetID = SysRowsets
				.Where(x => x.idmajor == (int)SystemObject.sysrscols && x.idminor == 1)
				.Single()
				.rowsetid;

			var au = SysAllocUnits
				.Where(x => x.auid == rowsetID && x.type == 1)
				.Single();

			SysRsCols = _scanner.ScanIamDataPages<sysrscol>(new PagePointer(au.pgfirstiam), CompressionContext.NoCompression).ToList();
		}

		private void parseSysscalartypes()
		{
			long rowsetID = SysRowsets
				.Where(x => x.idmajor == (int)SystemObject.sysscalartypes && x.idminor == 1)
				.Single()
				.rowsetid;

			var au = SysAllocUnits
				.Where(x => x.auid == rowsetID && x.type == 1)
				.Single();

			SysScalarTypes = _scanner.ScanIamDataPages<sysscalartype>(new PagePointer(au.pgfirstiam), CompressionContext.NoCompression).ToList();
		}

		private void parseSysobjects()
		{
			long rowsetID = SysRowsets
				.Where(x => x.idmajor == (int)SystemObject.sysschobjs && x.idminor == 1)
				.Single()
				.rowsetid;

			var au = SysAllocUnits
				.Where(x => x.auid == rowsetID && x.type == 1)
				.Single();

			SysSchObjs = _scanner.ScanIamDataPages<sysschobj>(new PagePointer(au.pgfirstiam), CompressionContext.NoCompression).ToList();
		}

		private void parseSyscolpars()
		{
			long rowsetID = SysRowsets
				.Where(x => x.idmajor == (int)SystemObject.syscolpars && x.idminor == 1)
				.Single()
				.rowsetid;

			var au = SysAllocUnits
				.Where(x => x.auid == rowsetID && x.type == 1)
				.Single();

			SysColPars = _scanner.ScanIamDataPages<syscolpar>(new PagePointer(au.pgfirstiam), CompressionContext.NoCompression).ToList();
		}

		private void parseSysrowsets()
		{
			var au = SysAllocUnits
			        .Where(x => x.auid == FixedSystemObjectAllocationUnits.sysrowsets)
			        .Single();

            SysRowsets = _scanner.ScanIamDataPages<sysrowset>(new PagePointer(au.pgfirstiam), CompressionContext.NoCompression).ToList();
		}

		private void parseSysallocunits()
		{
			// Though this has a fixed first-page location at (1:16) we'll read it from the boot page to be sure
			var bootPage = _db.GetBootPage();
			SysAllocUnits = _scanner.ScanLinkedDataPages<sysallocunit>(bootPage.FirstSysIndexes, CompressionContext.NoCompression).ToList();
		}
	}
}
