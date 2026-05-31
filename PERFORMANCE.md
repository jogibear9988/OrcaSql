# Performance Notes

## Heideblume WinCC flexible scan

The current scan benchmark uses:

`C:\Data\test\winccflexible\Heideblume\Heideblume_Prod_Touch_002.hmi`

The workload scans every table through `DataScanner.ScanTable`, counts rows, and reports table-level errors. The database contains 114 discovered tables and 43,696 scanned rows after the partitionless-table and nullable-datetime fixes.

## Page, variable-column, LOB, and metadata-cache optimization

This pass targeted allocations left after the initial page/header and fixed-value parser optimizations:

- primary records are parsed from the original 8 KB page buffer with an offset instead of copying the page tail for each slot;
- raw variable-length column proxies can reference a slice of the page buffer, and common `nvarchar`/`varchar` values decode directly from that buffer;
- LOB/image structures avoid LINQ `Skip`/`Take`/`ToArray` copies and use direct copies or stream assembly for multi-fragment values;
- `DataScanner` caches common metadata lookups inside a scanner instance: table objects, partitions, in-row allocation units, clustered indexes, partition columns, and default constraints.

Observed all-table scan timings against commit `90fbf97`:

```text
Before:
1089 ms, 1110 ms, 1033 ms, 699 ms, 932 ms, 906 ms

After:
966 ms, 708 ms, 716 ms, 486 ms, 468 ms, 454 ms
```

Ignoring the first run, the average moved from about 936 ms to about 566 ms. Comparing the last three runs, the scan moved from about 846 ms to about 469 ms.

Hot-table allocation changes:

```text
HmiTextTable:              147.79 MB -> 128.97 MB
HmiLogFilePropertiesTable: 248.63 MB -> 207.15 MB
HmiBasicTable:              99.14 MB ->  45.30 MB
HmiAddressTable:            16.88 MB ->  15.29 MB
```

Verification:

```powershell
dotnet build src\OrcaSql.Core\OrcaSql.Core.csproj -c Release
dotnet test OrcaSql.slnx -c Release --no-restore
```

## Row, null-bitmap, variable-column, and lazy LOB optimization

The next pass targeted per-row and per-record overhead that remained after page-buffer parsing:

- `Row` stores values in an ordinal `object[]` instead of a per-row `Dictionary<string, object>`, while preserving the existing name-based indexer and `Field<T>` API;
- `Schema` caches column ordinals and exposes a stable `ReadOnlyCollection<DataColumn>` instead of creating one on each access;
- variable-length record data is stored in an array instead of a per-record dictionary;
- record null bitmap checks use raw bitmap bytes instead of `BitArray`;
- `ISqlType` now exposes a span-shaped parsing entry point, and raw string values continue to decode directly from page-buffer slices without intermediate `byte[]` copies on the current `netstandard2.0` target;
- `DataScanner` reuses empty schema rows and partition-specific extractor helpers;
- `DataScanner.LoadLobData` can be set to `false` to return LOB proxies for off-row `image`/`text`/`ntext` data instead of materializing payloads during the scan.

Observed all-table scan timings after this pass:

```text
Full LOB loading:
877 ms, 749 ms, 802 ms, 554 ms, 388 ms, 384 ms

LoadLobData = false:
200 ms
```

Compared with the previous documented pass, the last-three-run full-LOB scan moved from about 469 ms to about 442 ms, and the lazy LOB mode provides a much faster path for callers that only need row metadata or can defer LOB reads.

Hot-table allocation changes compared with the previous documented pass:

```text
HmiTextTable:              128.97 MB -> 120.07 MB
HmiLogFilePropertiesTable: 207.15 MB -> 199.69 MB
HmiBasicTable:              45.30 MB ->  31.70 MB
HmiAddressTable:            15.29 MB ->  14.77 MB
```
