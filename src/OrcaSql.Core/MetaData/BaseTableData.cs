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

			if (db.IsSqlServer2000)
			{
				parseSqlServer2000BaseTables();
				return;
			}

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

        private void parseSqlServer2000BaseTables()
        {
            var bootPage = _db.GetBootPage();

            var sql2000Indexes = _scanner
                .ScanLinkedDataPages<Sql2000SysIndex>(bootPage.FirstSysIndexes, CompressionContext.NoCompression)
                .Where(i => i["id"] != null && i["indid"] != null && i.id > 0 && i.indid >= 0)
                .GroupBy(i => new { i.id, i.indid, name = i.name ?? string.Empty })
                .Select(g => g.First())
                .ToList();

            var sysColumnsIndex = getSql2000TableIndex(sql2000Indexes, 3);
            var sql2000Columns = _scanner
                .ScanIamDataPages<Sql2000SysColumn>(new PagePointer(sysColumnsIndex.FirstIAM), CompressionContext.NoCompression)
                .Where(c => c["id"] != null
                            && c["colid"] != null
                            && c["xtype"] != null
                            && c["xusertype"] != null
                            && c["length"] != null
                            && c["xprec"] != null
                            && c["xscale"] != null
                            && c.id > 0
                            && c.colid > 0
                            && !string.IsNullOrWhiteSpace(c.name))
                .GroupBy(c => new { c.id, c.colid })
                .Select(g => g.First())
                .ToList();

            var sql2000Objects = scanSql2000Table(sql2000Indexes, sql2000Columns, 1)
                .GroupBy(r => getValue<int>(r, "id"))
                .Select(g => g.First())
                .ToList();

            var sql2000Types = scanSql2000Table(sql2000Indexes, sql2000Columns, 4)
                .GroupBy(r => getValue<short>(r, "xusertype"))
                .Select(g => g.First())
                .ToList();

            SysAllocUnits = sql2000Indexes
                .Where(i => i.indid < 255)
                .Select(toSysAllocUnit)
                .ToList();

            SysRowsets = sql2000Indexes
                .Where(i => i.indid < 255)
                .Select(i => toSysRowset(i, sql2000Columns))
                .ToList();

            SysColPars = sql2000Columns
                .Select(toSysColPar)
                .ToList();

            SysSchObjs = sql2000Objects
                .Select(o => toSysSchObj(o, sql2000Columns))
                .ToList();

            SysScalarTypes = sql2000Types
                .Select(toSysScalarType)
                .ToList();

            foreach (var column in sql2000Columns
                         .Where(c => SysScalarTypes.All(t => t.id != c.xusertype))
                         .GroupBy(c => c.xusertype)
                         .Select(g => g.First()))
                SysScalarTypes.Add(toSysScalarType(column));

            SysRsCols = new List<sysrscol>();
            SysSingleObjRefs = new List<syssingleobjref>();

            _sysidxstats = sql2000Indexes
                .Where(i => i.indid < 255)
                .Select(toSysIdxStat)
                .ToList();

            _sysclsobjs = new List<sysclsobj>();
            _sysiscols = new List<sysiscol>();
            _sysobjvalues = new List<sysobjvalue>();
            _sysowners = new List<sysowner>();
        }

        private IList<Row> scanSql2000Table(IList<Sql2000SysIndex> indexes, IList<Sql2000SysColumn> columns, int objectId)
        {
            var index = getSql2000TableIndex(indexes, objectId);
            var schema = new DataRow(getSql2000Columns(columns, objectId));

            return _scanner
                .ScanIamDataPages(new PagePointer(index.FirstIAM), new DataExtractorHelper(schema), CompressionContext.NoCompression, true)
                .ToList();
        }

        private static Sql2000SysIndex getSql2000TableIndex(IEnumerable<Sql2000SysIndex> indexes, int objectId)
        {
            return indexes
                .Where(i => i.id == objectId && (i.indid == 1 || i.indid == 0))
                .OrderByDescending(i => i.indid)
                .First();
        }

        private static IEnumerable<DataColumn> getSql2000Columns(IEnumerable<Sql2000SysColumn> columns, int objectId)
        {
            var result = columns
                .Where(c => c.id == objectId && isPhysicalSql2000Column(c) && (objectId != 1 || c.colid <= 9))
                .GroupBy(c => c.colid)
                .Select(g => g.OrderBy(c => c.name).First())
                .OrderBy(c => c.colid)
                .Select(toDataColumn)
                .ToList();

            if (objectId == 1 && result.All(c => c.Name != "name"))
                result.Insert(0, new DataColumn("name", "sysname", true));

            return result;
        }

        private static bool isPhysicalSql2000Column(Sql2000SysColumn column)
        {
            return column.xoffset != 0;
        }

        private static DataColumn toDataColumn(Sql2000SysColumn column)
        {
            return new DataColumn(column.name, getSql2000TypeName(column.xtype, column.length, column.xprec, column.xscale), true)
            {
                ColumnID = column.colid
            };
        }

        private static string getSql2000TypeName(byte typeId, short length, byte precision, byte scale)
        {
            switch (typeId)
            {
                case 34:
                    return "image";
                case 35:
                    return "text";
                case 48:
                    return "tinyint";
                case 52:
                    return "smallint";
                case 56:
                    return "int";
                case 59:
                    return "real";
                case 60:
                    return "money";
                case 61:
                    return "datetime";
                case 62:
                    return "float(" + (precision == 0 ? 53 : precision) + ")";
                case 104:
                    return "bit";
                case 106:
                    return "decimal(" + precision + ", " + scale + ")";
                case 108:
                    return "numeric(" + precision + ", " + scale + ")";
                case 122:
                    return "smallmoney";
                case 127:
                    return "bigint";
                case 165:
                    return "varbinary(" + length + ")";
                case 167:
                    return "varchar(" + length + ")";
                case 173:
                    return "binary(" + length + ")";
                case 175:
                    return "char(" + length + ")";
                case 231:
                    return length == 256 ? "sysname" : "nvarchar(" + (length / 2) + ")";
                case 239:
                    return "nchar(" + (length / 2) + ")";
                default:
                    throw new ArgumentException("Unsupported SQL Server 2000 type ID: " + typeId);
            }
        }

        private static string getSql2000BaseTypeName(byte typeId)
        {
            switch (typeId)
            {
                case 34:
                    return "image";
                case 35:
                    return "text";
                case 48:
                    return "tinyint";
                case 52:
                    return "smallint";
                case 56:
                    return "int";
                case 59:
                    return "real";
                case 60:
                    return "money";
                case 61:
                    return "datetime";
                case 62:
                    return "float";
                case 104:
                    return "bit";
                case 106:
                    return "decimal";
                case 108:
                    return "numeric";
                case 122:
                    return "smallmoney";
                case 127:
                    return "bigint";
                case 165:
                    return "varbinary";
                case 167:
                    return "varchar";
                case 173:
                    return "binary";
                case 175:
                    return "char";
                case 231:
                    return "nvarchar";
                case 239:
                    return "nchar";
                default:
                    return "type_" + typeId;
            }
        }

        private static long makeSql2000RowsetId(int objectId, int indexId)
        {
            return ((long)objectId << 16) + (ushort)indexId;
        }

        private static sysallocunit toSysAllocUnit(Sql2000SysIndex index)
        {
            var row = new sysallocunit();
            row["auid"] = makeSql2000RowsetId(index.id, index.indid);
            row["type"] = (byte)1;
            row["ownerid"] = makeSql2000RowsetId(index.id, index.indid);
            row["status"] = 0;
            row["fgid"] = index.groupid;
            row["pgfirst"] = index.first;
            row["pgroot"] = index.root;
            row["pgfirstiam"] = index.FirstIAM;
            row["pcused"] = (long)index.used;
            row["pcdata"] = (long)index.dpages;
            row["pcreserved"] = (long)index.reserved;
            row["dbfragid"] = 1;
            return row;
        }

        private static sysrowset toSysRowset(Sql2000SysIndex index, IEnumerable<Sql2000SysColumn> columns)
        {
            var row = new sysrowset();
            row["rowsetid"] = makeSql2000RowsetId(index.id, index.indid);
            row["ownertype"] = (byte)1;
            row["idmajor"] = index.id;
            row["idminor"] = (int)index.indid;
            row["numpart"] = 1;
            row["status"] = (index.status & 2) != 0 ? 4 : 0;
            row["fgidfs"] = index.groupid;
            row["rcrows"] = index.rowcnt;
            row["cmprlevel"] = (byte)0;
            row["fillfact"] = index.OrigFillFactor;
            row["maxnullbit"] = columns.Where(c => c.id == index.id).Select(c => c.colid).DefaultIfEmpty((short)0).Max();
            row["maxleaf"] = (int)index.xmaxlen;
            row["maxint"] = (short)0;
            row["minleaf"] = index.minlen;
            row["minint"] = (short)0;
            row["rsguid"] = null;
            row["lockres"] = null;
            row["dbfragid"] = 1;
            return row;
        }

        private static syscolpar toSysColPar(Sql2000SysColumn column)
        {
            var row = new syscolpar();
            row["id"] = column.id;
            row["number"] = (short)0;
            row["colid"] = (int)column.colid;
            row["name"] = cleanSql2000String(column.name);
            row["xtype"] = column.xtype;
            row["utype"] = (int)column.xusertype;
            row["length"] = column.length;
            row["prec"] = column.xprec;
            row["scale"] = column.xscale;
            row["collationid"] = column["collationid"] == null ? 0 : column.collationid;
            row["status"] = (int)column.colstat;
            row["maxinrow"] = column.xoffset;
            row["xmlns"] = 0;
            row["dflt"] = column.cdefault;
            row["chk"] = column.domain;
            row["idtval"] = column["autoval"] == null ? null : column.autoval;
            return row;
        }

        private static sysschobj toSysSchObj(Row source, IEnumerable<Sql2000SysColumn> columns)
        {
            var id = getValue<int>(source, "id");
            var type = cleanSql2000String(getValue<string>(source, "xtype"));
            var row = new sysschobj();
            row["id"] = id;
            row["name"] = cleanSql2000String(getValue<string>(source, "name"));
            row["nsid"] = 1;
            row["nsclass"] = (byte)0;
            row["status"] = type == "S" ? 1 : 0;
            row["type"] = type;
            row["pid"] = hasColumn(source, "parent_obj") ? getValue<int>(source, "parent_obj") : 0;
            row["pclass"] = (byte)1;
            row["intprop"] = columns.Where(c => c.id == id).Select(c => (int)c.colid).DefaultIfEmpty(0).Max();
            row["created"] = hasColumn(source, "crdate") ? getValue<DateTime>(source, "crdate") : DateTime.MinValue;
            row["modified"] = hasColumn(source, "refdate") ? getValue<DateTime>(source, "refdate") : DateTime.MinValue;
            row["status2"] = 0;
            return row;
        }

        private static sysscalartype toSysScalarType(Row source)
        {
            var row = new sysscalartype();
            row["id"] = (int)getValue<short>(source, "xusertype");
            row["schid"] = 1;
            row["name"] = cleanSql2000String(getValue<string>(source, "name"));
            row["xtype"] = getValue<byte>(source, "xtype");
            row["length"] = getValue<short>(source, "length");
            row["prec"] = getValue<byte>(source, "xprec");
            row["scale"] = getValue<byte>(source, "xscale");
            row["collationid"] = hasColumn(source, "collationid") ? getValue<int>(source, "collationid") : 0;
            row["status"] = hasColumn(source, "status") ? (int)getValue<byte>(source, "status") : 0;
            row["created"] = DateTime.MinValue;
            row["modified"] = DateTime.MinValue;
            row["dflt"] = hasColumn(source, "tdefault") ? getValue<int>(source, "tdefault") : 0;
            row["chk"] = hasColumn(source, "domain") ? getValue<int>(source, "domain") : 0;
            return row;
        }

        private static sysscalartype toSysScalarType(Sql2000SysColumn column)
        {
            var row = new sysscalartype();
            row["id"] = (int)column.xusertype;
            row["schid"] = 1;
            row["name"] = getSql2000BaseTypeName(column.xtype);
            row["xtype"] = column.xtype;
            row["length"] = column.length;
            row["prec"] = column.xprec;
            row["scale"] = column.xscale;
            row["collationid"] = column["collationid"] == null ? 0 : column.collationid;
            row["status"] = 0;
            row["created"] = DateTime.MinValue;
            row["modified"] = DateTime.MinValue;
            row["dflt"] = column.cdefault;
            row["chk"] = column.domain;
            return row;
        }

        private static sysidxstat toSysIdxStat(Sql2000SysIndex index)
        {
            var modernStatus = 1;
            if ((index.status & 2) != 0)
                modernStatus |= 0x8;
            if ((index.status & 0x800) != 0)
                modernStatus |= 0x20;

            var row = new sysidxstat();
            row["id"] = index.id;
            row["indid"] = (int)index.indid;
            row["name"] = cleanSql2000String(index.name);
            row["status"] = modernStatus;
            row["intprop"] = 0;
            row["fillfact"] = index.OrigFillFactor;
            row["type"] = getSql2000IndexType(index.indid);
            row["tinyprop"] = (byte)0;
            row["dataspace"] = (int)index.groupid;
            row["lobds"] = 0;
            row["rowset"] = makeSql2000RowsetId(index.id, index.indid);
            return row;
        }

        private static byte getSql2000IndexType(short indexId)
        {
            if (indexId == 0)
                return 0;

            return indexId == 1 ? (byte)1 : (byte)2;
        }

        private static bool hasColumn(Row row, string name)
        {
            return row.Columns.Any(c => c.Name == name);
        }

        private static T getValue<T>(Row row, string name)
        {
            var value = row[name];
            if (value == null)
                return default(T);

            if (value is T typed)
                return typed;

            return (T)Convert.ChangeType(value, typeof(T));
        }

        private static string cleanSql2000String(string value)
        {
            return value?.TrimEnd('\0', ' ', '†');
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

        private class Sql2000SysIndex : Row
        {
            private static readonly ISchema schema = new Schema(new[]
            {
                new DataColumn("id", "int"),
                new DataColumn("status", "int"),
                new DataColumn("first", "binary(6)"),
                new DataColumn("indid", "smallint"),
                new DataColumn("root", "binary(6)"),
                new DataColumn("minlen", "smallint"),
                new DataColumn("keycnt", "smallint"),
                new DataColumn("groupid", "smallint"),
                new DataColumn("dpages", "int"),
                new DataColumn("reserved", "int"),
                new DataColumn("used", "int"),
                new DataColumn("rowcnt", "bigint"),
                new DataColumn("rowmodctr", "int"),
                new DataColumn("reserved3", "tinyint"),
                new DataColumn("reserved4", "tinyint"),
                new DataColumn("xmaxlen", "smallint"),
                new DataColumn("maxirow", "smallint"),
                new DataColumn("OrigFillFactor", "tinyint"),
                new DataColumn("StatVersion", "tinyint"),
                new DataColumn("reserved2", "int"),
                new DataColumn("FirstIAM", "binary(6)"),
                new DataColumn("impid", "smallint"),
                new DataColumn("lockflags", "smallint"),
                new DataColumn("pgmodctr", "int"),
                new DataColumn("keys", "varbinary", true),
                new DataColumn("name", "sysname", true)
            });

            public Sql2000SysIndex() : base(schema)
            { }

            public override Row NewRow()
            {
                return new Sql2000SysIndex();
            }

            internal int id => Field<int>("id");
            internal int status => Field<int>("status");
            internal byte[] first => Field<byte[]>("first");
            internal short indid => Field<short>("indid");
            internal byte[] root => Field<byte[]>("root");
            internal short minlen => Field<short>("minlen");
            internal short groupid => Field<short>("groupid");
            internal int dpages => Field<int>("dpages");
            internal int reserved => Field<int>("reserved");
            internal int used => Field<int>("used");
            internal long rowcnt => Field<long>("rowcnt");
            internal short xmaxlen => Field<short>("xmaxlen");
            internal byte OrigFillFactor => Field<byte>("OrigFillFactor");
            internal byte[] FirstIAM => Field<byte[]>("FirstIAM");
            internal string name => Field<string>("name");
        }

        private class Sql2000SysColumn : Row
        {
            private static readonly ISchema schema = new Schema(new[]
            {
                new DataColumn("name", "sysname", true),
                new DataColumn("id", "int"),
                new DataColumn("xtype", "tinyint"),
                new DataColumn("typestat", "tinyint"),
                new DataColumn("xusertype", "smallint"),
                new DataColumn("length", "smallint"),
                new DataColumn("xprec", "tinyint"),
                new DataColumn("xscale", "tinyint"),
                new DataColumn("colid", "smallint"),
                new DataColumn("xoffset", "smallint"),
                new DataColumn("bitpos", "tinyint"),
                new DataColumn("reserved", "tinyint"),
                new DataColumn("colstat", "smallint"),
                new DataColumn("cdefault", "int"),
                new DataColumn("domain", "int"),
                new DataColumn("number", "smallint"),
                new DataColumn("colorder", "smallint"),
                new DataColumn("autoval", "varbinary", true),
                new DataColumn("offset", "smallint"),
                new DataColumn("collationid", "int"),
                new DataColumn("language", "int")
            });

            public Sql2000SysColumn() : base(schema)
            { }

            public override Row NewRow()
            {
                return new Sql2000SysColumn();
            }

            internal string name => Field<string>("name");
            internal int id => Field<int>("id");
            internal byte xtype => Field<byte>("xtype");
            internal short xusertype => Field<short>("xusertype");
            internal short length => Field<short>("length");
            internal byte xprec => Field<byte>("xprec");
            internal byte xscale => Field<byte>("xscale");
            internal short colid => Field<short>("colid");
            internal short xoffset => Field<short>("xoffset");
            internal short colstat => Field<short>("colstat");
            internal int cdefault => Field<int>("cdefault");
            internal int domain => Field<int>("domain");
            internal byte[] autoval => Field<byte[]>("autoval");
            internal int collationid => Field<int>("collationid");
        }
	}
}
