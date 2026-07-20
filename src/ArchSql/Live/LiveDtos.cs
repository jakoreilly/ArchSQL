namespace ArchSql.Live;

/// <summary>Plain row shapes read from the catalog/DMV queries. They form the test seam: the pure
/// reconstruction/aggregation helpers take these lists, so they can be exercised without a database.</summary>
public sealed record ObjectRow(string Schema, string Name, string TypeCode);

public sealed record ColumnRow(
    string Schema, string Table, string Column, string TypeName,
    int MaxLength, byte Precision, byte Scale, bool IsNullable, bool IsIdentity, int Ordinal);

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
