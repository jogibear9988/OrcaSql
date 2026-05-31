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
