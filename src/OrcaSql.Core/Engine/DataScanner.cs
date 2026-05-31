using System;
using System.Collections.Generic;
using System.Linq;
using OrcaSql.Core.Engine.Pages.PFS;
using OrcaSql.Core.Engine.Records.Parsers;
using OrcaSql.Core.MetaData;
using OrcaSql.Core.MetaData.BaseTables;
using OrcaSql.Core.MetaData.DMVs;
using OrcaSql.Core.MetaData.Enumerations;

namespace OrcaSql.Core.Engine
{
	public class DataScanner : Scanner
	{
		private readonly Dictionary<short, long> _filePageCounts;
		private Dictionary<string, sysschobj> _tableObjectsByName;
		private Dictionary<int, Partition[]> _partitionsByObjectId;
		private Dictionary<long, Partition> _partitionsById;
		private Dictionary<long, SystemInternalsAllocationUnit> _inRowAllocationUnitsByContainerId;
		private Dictionary<int, MetaData.DMVs.Index> _clusteredIndexesByObjectId;
		private Dictionary<long, SystemInternalsPartitionColumn[]> _partitionColumnsByPartitionId;
		private Dictionary<int, SysDefaultConstraint[]> _defaultConstraintsByParentObjectId;

		public DataScanner(Database database)
			: base(database)
		{
			_filePageCounts = database.Files.ToDictionary(x => x.Key, x => x.Value.Length / 8192);
		}

		/// <summary>
		/// Will scan any table - heap or clustered - and return an IEnumerable of generic rows with data & schema
		/// </summary>
		public IEnumerable<Row> ScanTable(string tableName, int? schemaId = null, bool isSysTable = true)
		{
			var schema = MetaData.GetEmptyDataRow(tableName, schemaId);

			return ScanTable(tableName, schema, isSysTable);
		}

        public DataRow GetEmptyDataRow(string tableName, int? schemaId = null)
        {
            var schema = MetaData.GetEmptyDataRow(tableName, schemaId);

            return schema;
        }

        /// <summary>
        /// Will scan any table - heap or clustered - and return an IEnumerable of typed rows with data & schema
        /// </summary>
        internal IEnumerable<TDataRow> ScanTable<TDataRow>(string tableName) where TDataRow : Row, new()
		{
			var schema = new TDataRow();

			return ScanTable(tableName, schema).Cast<TDataRow>();
		}

		/// <summary>
		/// Scans a linked list of pages returning an IEnumerable of typed rows with data & schema
		/// </summary>
		internal IEnumerable<TDataRow> ScanLinkedDataPages<TDataRow>(PagePointer loc, CompressionContext compression) where TDataRow : Row, new()
		{
			return ScanLinkedDataPages(loc, new DataExtractorHelper(new TDataRow()), compression, true).Cast<TDataRow>();
		}

		/// <summary>
		/// Scans pages found via IAM page chain, returning an IEnumerable of typed rows with data & schema.
		/// This is more reliable than ScanLinkedDataPages when pgfirst in sysallocunits is stale.
		/// </summary>
		internal IEnumerable<TDataRow> ScanIamDataPages<TDataRow>(PagePointer iamPageLoc, CompressionContext compression) where TDataRow : Row, new()
		{
			return ScanHeap(iamPageLoc, new DataExtractorHelper(new TDataRow()), compression, true).Cast<TDataRow>();
		}

		internal IEnumerable<Row> ScanIamDataPages(PagePointer iamPageLoc, DataExtractorHelper schema,
            CompressionContext compression, bool isSysTable)
		{
			return ScanHeap(iamPageLoc, schema, compression, isSysTable);
		}

		/// <summary>
		/// Starts at the data page (loc) and follows the NextPage pointer chain till the end.
		/// </summary>
		internal IEnumerable<Row> ScanLinkedDataPages(PagePointer loc, DataExtractorHelper schema,
            CompressionContext compression, bool isSysTable)
		{
			while (PagePointer.Zero != loc && loc != null && loc.PageID > 0)
			{
				var recordParser = RecordEntityParser.CreateEntityParserForPage(loc, compression, Database, isSysTable);

				foreach (var dr in recordParser.GetEntities(schema))
					yield return dr;

				loc = recordParser.NextPage;
			}
		}

		private IEnumerable<Row> ScanTable(string tableName, Row schema, bool isSysTable = true)
		{
			// Get object
			var tableObjects = GetTableObjectsByName();
			tableObjects.TryGetValue(tableName, out var tableObject);

			if (tableObject == null)
				throw new ArgumentException("Table does not exist.");

			// Get rowset, prefer clustered index if exists
			var partitionsByObjectId = GetPartitionsByObjectId();
			var partitions = partitionsByObjectId.TryGetValue(tableObject.id, out var objectPartitions)
				? objectPartitions
				: Array.Empty<Partition>();

			if (partitions.Length == 0)
				return Enumerable.Empty<Row>();

			// Loop all partitions and return results one by one
			return partitions.SelectMany(partition => ScanPartition(partition.PartitionID, partition.PartitionNumber, schema, isSysTable));
		}

		private IEnumerable<Row> ScanPartition(long partitionID, int partitionNumber, Row schema, bool isSysTable = true)
		{
			// Lookup partition
			var partitionsById = GetPartitionsById();
			partitionsById.TryGetValue(partitionID, out var partition);

			if(partition == null || partition.PartitionNumber != partitionNumber)
				throw new ArgumentException("Partition (" + partitionID + "." + partitionNumber + " does not exist.");

			// Get allocation unit for in-row data
			var allocationUnits = GetInRowAllocationUnitsByContainerId();
			allocationUnits.TryGetValue(partition.PartitionID, out var au);

			if (au == null)
				throw new ArgumentException("Partition (" + partition.PartitionID + "." + partition.PartitionNumber + " has no HOBT allocation unit.");

			// Before we can scan either heaps or indices, we need to know the compression level as that's set at the partition level, and not at the record/page level.
			// We also need to know whether the partition is using vardecimals.
			var compression = new CompressionContext((CompressionLevel)partition.DataCompression, MetaData.PartitionHasVardecimalColumns(partition.PartitionID));

            var clusteredIndex = isSysTable
                ? null
                : GetClusteredIndexesByObjectId().TryGetValue(partition.ObjectID, out var index) ? index : null;

            var useClusteredIndex = isSysTable || clusteredIndex != null;

            var partitionColumns = isSysTable || Database.UsesLegacyPartitionMetadata
                ? null
                : GetPartitionColumnsByPartitionId().TryGetValue(partition.PartitionID, out var columns) ? columns : Array.Empty<SystemInternalsPartitionColumn>();

            var defaultConstraints = isSysTable || Database.UsesLegacyPartitionMetadata
                ? null
                : GetDefaultConstraintsByParentObjectId().TryGetValue(partition.ObjectID, out var constraints) ? constraints : Array.Empty<SysDefaultConstraint>();

            var schemaWrapper = new DataExtractorHelper(schema, Database.Dmvs, null, partitionColumns, defaultConstraints);

            // For system tables and SQL Server 2000 tables, use IAM-based scanning since
            // pgfirst/root pointers can become stale or use older index layouts.
            if (isSysTable || Database.UsesLegacyPartitionMetadata)
            {
                foreach (var row in ScanHeap(au.FirstIamPagePointer, schemaWrapper, compression, isSysTable))
                    yield return row;
            }
            // Heap tables won't have root pages, thus we can check whether a root page is defined for the HOBT allocation unit
            else if (au.RootPagePointer != PagePointer.Zero && useClusteredIndex)
            {
                var currentPage = au.RootPagePointer;

                if (currentPage != au.FirstPagePointer)
                {
                    while (true)
                    {
                        var ciPage = Database.GetClusteredIndexPage(currentPage, isSysTable);

                        currentPage = ciPage.Records.Select(x => x.PageId).FirstOrDefault();

                        if (ciPage.Header.Level <= 1)
                        {
                            break;
                        }
                    }
                }

                // Index
                foreach (var row in ScanLinkedDataPages(currentPage, schemaWrapper, compression, isSysTable))
                    yield return row;
            }
            else
            {
				// Heap
				foreach (var row in ScanHeap(au.FirstIamPagePointer, schemaWrapper, compression, isSysTable))
					yield return row;
			}
		}

		/// <summary>
		/// Scans a heap beginning from the provided IAM page and onwards.
		/// </summary>
		private IEnumerable<Row> ScanHeap(PagePointer loc, DataExtractorHelper schema, CompressionContext compression,
            bool isSysTable)
		{
			var pfsPages = new Dictionary<long, PfsPage>();

			// Traverse the linked list of IAM pages until the tail pointer is zero
			while (loc != PagePointer.Zero)
			{
				if (!PageExists(loc))
					yield break;

				// Before scanning, check that the IAM page itself is allocated
				var pfsPage = GetPfsPage(PfsPage.GetPfsPointerForPage(loc), pfsPages);

				// If IAM page isn't allocated, there's nothing to return
				if (!pfsPage.GetPageDescription(loc.PageID).IsAllocated)
					yield break;

				var iamPage = Database.GetIamPage(loc, isSysTable);

				// Create an array with all of the header slot pointers
				var iamPageSlots = new []
					{
						iamPage.Slot0,
						iamPage.Slot1,
						iamPage.Slot2,
						iamPage.Slot3,
						iamPage.Slot4,
						iamPage.Slot5,
						iamPage.Slot6,
						iamPage.Slot7
					};

				// Loop each header slot and yield the results, provided the header slot is allocated
				foreach (var slot in iamPageSlots.Where(x => x != PagePointer.Zero && PageExists(x)))
				{
					// Skip non-Data pages (e.g. Index pages in system table allocation units)
					var slotPageBytes = Database.GetPageBytes(slot, isSysTable);
					var slotPageHeader = new Pages.PageHeader(slotPageBytes, 0);
					if (slotPageHeader.Type != Pages.PageType.Data)
						continue;

					var recordParser = RecordEntityParser.CreateEntityParserForPage(slot, slotPageBytes, compression, Database);

					foreach (var dr in recordParser.GetEntities(schema))
						yield return dr;
				}

				// Then loop through allocated extents and yield results
				foreach (var extent in iamPage.GetAllocatedExtents().Where(extent => PageExists(extent.StartPage)))
				{
					// Get PFS page that tracks this extent
					var pfs = GetPfsPage(PfsPage.GetPfsPointerForPage(extent.StartPage), pfsPages);

					foreach (var pageLoc in extent.GetPagePointers())
					{
						if (!PageExists(pageLoc))
							continue;

						// Check if page is allocated according to PFS page
						var pfsDescription = pfs.GetPageDescription(pageLoc.PageID);

						if(!pfsDescription.IsAllocated)
							continue;

						// Skip non-Data pages (e.g. Index pages in system table allocation units)
						var pageBytes = Database.GetPageBytes(pageLoc, !isSysTable);
						var pageHeader = new Pages.PageHeader(pageBytes, 0);
						if (pageHeader.Type != Pages.PageType.Data)
							continue;

						var recordParser = RecordEntityParser.CreateEntityParserForPage(pageLoc, pageBytes, compression, Database);

						foreach (var dr in recordParser.GetEntities(schema))
							yield return dr;
					}
				}

				// Update current IAM chain location to the tail pointer
				loc = iamPage.Header.NextPage;
			}
		}

		private bool PageExists(PagePointer page)
		{
			if (page == null || page.FileID <= 0 || page.PageID < 0)
				return false;

			if (!Database.Files.TryGetValue(page.FileID, out var file))
				return false;

			return page.PageID < _filePageCounts[page.FileID];
		}

		private PfsPage GetPfsPage(PagePointer loc, IDictionary<long, PfsPage> pfsPages)
		{
			var key = ((long)(ushort)loc.FileID << 48) | loc.PageID;

			if (!pfsPages.TryGetValue(key, out var pfsPage))
			{
				pfsPage = Database.GetPfsPage(loc);
				pfsPages.Add(key, pfsPage);
			}

			return pfsPage;
		}

		private Dictionary<string, sysschobj> GetTableObjectsByName()
		{
			return _tableObjectsByName ?? (_tableObjectsByName = Database.BaseTables.SysSchObjs
				.Where(x => x.type.Trim() == ObjectType.INTERNAL_TABLE || x.type.Trim() == ObjectType.SYSTEM_TABLE || x.type.Trim() == ObjectType.USER_TABLE)
				.GroupBy(x => x.name)
				.ToDictionary(x => x.Key, x => x.First()));
		}

		private Dictionary<int, Partition[]> GetPartitionsByObjectId()
		{
			return _partitionsByObjectId ?? (_partitionsByObjectId = Database.Dmvs.Partitions
				.Where(x => x.IndexID <= 1)
				.GroupBy(x => x.ObjectID)
				.ToDictionary(x => x.Key, x => x.OrderBy(p => p.PartitionNumber).ToArray()));
		}

		private Dictionary<long, Partition> GetPartitionsById()
		{
			return _partitionsById ?? (_partitionsById = Database.Dmvs.Partitions.ToDictionary(x => x.PartitionID));
		}

		private Dictionary<long, SystemInternalsAllocationUnit> GetInRowAllocationUnitsByContainerId()
		{
			return _inRowAllocationUnitsByContainerId ?? (_inRowAllocationUnitsByContainerId = Database.Dmvs.SystemInternalsAllocationUnits
				.Where(x => x.Type == 1)
				.GroupBy(x => x.ContainerID)
				.ToDictionary(x => x.Key, x => x.First()));
		}

		private Dictionary<int, MetaData.DMVs.Index> GetClusteredIndexesByObjectId()
		{
			return _clusteredIndexesByObjectId ?? (_clusteredIndexesByObjectId = Database.Dmvs.Indexes
				.Where(x => x.Type == 1)
				.GroupBy(x => x.ObjectID)
				.ToDictionary(x => x.Key, x => x.First()));
		}

		private Dictionary<long, SystemInternalsPartitionColumn[]> GetPartitionColumnsByPartitionId()
		{
			return _partitionColumnsByPartitionId ?? (_partitionColumnsByPartitionId = Database.Dmvs.SystemInternalsPartitionColumns
				.GroupBy(x => x.PartitionID)
				.ToDictionary(x => x.Key, x => x.ToArray()));
		}

		private Dictionary<int, SysDefaultConstraint[]> GetDefaultConstraintsByParentObjectId()
		{
			return _defaultConstraintsByParentObjectId ?? (_defaultConstraintsByParentObjectId = Database.Dmvs.SysDefaultConstraints
				.GroupBy(x => x.ParentObjectId)
				.ToDictionary(x => x.Key, x => x.ToArray()));
		}
    }
}
