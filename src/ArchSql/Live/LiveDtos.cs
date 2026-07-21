namespace ArchSql.Live;

/// <summary>Plain row shapes read from the catalog/DMV queries. They form the test seam: the pure
/// reconstruction/aggregation helpers take these lists, so they can be exercised without a database.</summary>
public sealed record ObjectRow(string Schema, string Name, string TypeCode);

public sealed record ColumnRow(
    string Schema, string Table, string Column, string TypeName,
    int MaxLength, byte Precision, byte Scale, bool IsNullable, bool IsIdentity, int Ordinal,
    string Collation = "");

public sealed record PkRow(string Schema, string Table, string Column, int KeyOrdinal);

public sealed record FkRow(
    string Schema, string Table, string RefSchema, string RefTable,
    string FkName, string OnDelete, string FromColumn, string ToColumn, int Ordinal);

public sealed record ModuleRow(string Schema, string Name, string Definition);

public sealed record ProcStatRow(string Schema, string Name, long ExecCount, long TotalWorkerMs, long TotalLogicalReads);

public sealed record IndexUsageRow(
    string Schema, string Table, string IndexName,
    long Seeks, long Scans, long Lookups, long Updates);

public sealed record MissingIndexRow(
    string Schema, string Table, string EqualityColumns,
    string InequalityColumns, string IncludedColumns, double ImpactScore);

/// <summary>One row per (index, key/included column) from the static catalog — grouped into
/// IndexDef by IndexInventory.Build.</summary>
public sealed record IndexColumnRow(
    string Schema, string Table, string IndexName, bool IsUnique, bool IsPrimaryKey,
    string TypeDesc, bool IsDisabled, string ColumnName, int KeyOrdinal, bool IsIncluded);

/// <summary>Row count and reserved storage for one table, from partition statistics.</summary>
public sealed record TableStatsRow(string Schema, string Table, long RowCount, long ReservedKb);

/// <summary>One completed backup of any type for the connected database, from msdb backup history.</summary>
public sealed record BackupRow(DateTime BackupFinishDate);

/// <summary>One statistics object's last-updated timestamp, from sys.dm_db_stats_properties.</summary>
public sealed record StatsAgeRow(string Schema, string Table, string StatsName, DateTime LastUpdated);

/// <summary>One index's fragmentation reading, from sys.dm_db_index_physical_stats.</summary>
public sealed record FragmentationRow(string Schema, string Table, string IndexName, double FragmentationPercent, long PageCount);
