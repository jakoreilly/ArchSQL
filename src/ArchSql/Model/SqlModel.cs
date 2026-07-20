namespace ArchSql.Model;

/// <summary>model.json schema version constants. The single place the current contract version
/// bumps; ModelUpgrader.Upgrade backfills anything older.</summary>
public static class SchemaVersions
{
    public const int Current = 3;
}

/// <summary>Root of everything ArchSql learned about a folder of SQL. Serialized verbatim to
/// model.json (round-trippable, drives --from-model), same contract as ArchDiagram's ProjectModel.</summary>
public sealed record SqlModel
{
    public required string RootName { get; init; }
    public required string SourcePath { get; init; }
    public List<SqlFile> Files { get; init; } = [];
    public List<DbObject> Objects { get; init; } = [];
    public List<ForeignKey> ForeignKeys { get; init; } = [];
    public List<ObjectDep> Dependencies { get; init; } = [];
    /// <summary>Populated in Phase 4; the field lives here now (additive interfaces) so the
    /// site/JSON layers need no change when linting lands.</summary>
    public List<LintFinding> Findings { get; init; } = [];
    public List<string> Diagnostics { get; init; } = [];
    public Dictionary<string, int> DialectLoc { get; init; } = [];
    /// <summary>Overall dialect the scan concluded ("tsql"|"mysql"|"postgres"|"mixed"|"unknown").</summary>
    public string Dialect { get; init; } = "unknown";
    /// <summary>Object x actor CRUD projection, reduced from typed ObjectDep (v2 CrudMatrix.Build).
    /// Sorted by (Target, Actor) for determinism.</summary>
    public List<CrudEntry> Crud { get; init; } = [];
    /// <summary>Runtime facts from a live connection (execution stats, index usage, missing
    /// indexes). Available=false for file scans and for logins lacking DMV permission. Appended
    /// after Crud and before SchemaVersion to keep declaration order stable for round-tripping.</summary>
    public RuntimeStats Runtime { get; init; } = new();
    /// <summary>model.json contract version. Absent/0 in v1 files; ModelJsonReader upgrades on load
    /// via ModelUpgrader. Appended LAST on every record so byte-identical round-tripping
    /// (reflection serialization preserves declaration order) is never disturbed by future fields.</summary>
    public int SchemaVersion { get; init; }
}

/// <summary>One actor-&gt;target CRUD relationship. Ops is a subset of the letters "CRUD", or "?" for
/// a dynamic-SQL blind spot where the target could not be resolved statically.</summary>
public sealed record CrudEntry
{
    public required string Actor { get; init; }
    public string Target { get; init; } = "";
    public required string Ops { get; init; }
    public bool IsBlindSpot { get; init; }
}

/// <summary>Live-server runtime facts gathered from DMVs. Absent for file scans and for logins
/// without VIEW DATABASE STATE. Counters are cumulative since the statistics last reset (not a
/// fixed window) and clear on server restart.</summary>
public sealed record RuntimeStats
{
    /// <summary>"" for file scans; "live-mssql" when populated from a SQL Server connection.
    /// A string (not a bool) so future "live-pg"/"live-mysql" sources slot in with no model change.</summary>
    public string Source { get; init; } = "";
    /// <summary>False for file scans and when the login lacked DMV permission.</summary>
    public bool Available { get; init; }
    /// <summary>UI sentence: what was captured plus the volatility caveat, or why data is missing.</summary>
    public string Note { get; init; } = "";
    public List<ObjectStat> ObjectStats { get; init; } = [];
    public List<IndexStat> IndexStats { get; init; } = [];
    public List<MissingIndex> MissingIndexes { get; init; } = [];
}

/// <summary>Per-object execution counters (procedures/functions) from sys.dm_exec_procedure_stats.</summary>
public sealed record ObjectStat
{
    public required string ObjectId { get; init; }
    public long ExecutionCount { get; init; }
    public long TotalWorkerTimeMs { get; init; }
    public long TotalLogicalReads { get; init; }
}

/// <summary>Per-index read/write usage from sys.dm_db_index_usage_stats.</summary>
public sealed record IndexStat
{
    public required string ObjectId { get; init; }
    public required string IndexName { get; init; }
    public long UserSeeks { get; init; }
    public long UserScans { get; init; }
    public long UserLookups { get; init; }
    public long UserUpdates { get; init; }
    /// <summary>Index that is maintained on writes but never read (seeks+scans+lookups == 0 with
    /// updates &gt; 0) — a drop candidate, pending workload-cycle confirmation.</summary>
    public bool IsUnused { get; init; }
}

/// <summary>A missing-index recommendation from the sys.dm_db_missing_index_* DMVs.</summary>
public sealed record MissingIndex
{
    public required string ObjectId { get; init; }
    public string EqualityColumns { get; init; } = "";
    public string InequalityColumns { get; init; } = "";
    public string IncludedColumns { get; init; } = "";
    /// <summary>Server's own benefit estimate: avg_total_user_cost * avg_user_impact/100 * (seeks+scans).</summary>
    public double ImpactScore { get; init; }
}

/// <summary>One scanned .sql file. Slug de-duped exactly like ArchDiagram (Pipeline.MakeSlug).</summary>
public sealed record SqlFile
{
    public required string RelPath { get; init; }
    public required string Slug { get; init; }
    /// <summary>Detected per-file; may differ from model.Dialect in a mixed folder.</summary>
    public required string Dialect { get; init; }
    public long SizeBytes { get; init; }
    public int Loc { get; init; }
    public int StatementCount { get; init; }
    /// <summary>False when only the Tier-1 fallback ran, or the deep parse hit errors.</summary>
    public bool ParsedCleanly { get; init; }
    public List<string> ObjectIds { get; init; } = [];
    /// <summary>Secret FACT only — never the value (Hard Constraint 2).</summary>
    public bool HasCredential { get; init; }
    public string Purpose { get; init; } = "";
}

/// <summary>A schema object. Kind is one of table|view|procedure|function|trigger|index|sequence|
/// type|schema. Id is dialect-normalized "schema.name" (see IdentifierRules.NormalizeId) so
/// cross-file references resolve regardless of bracket/quote/case conventions.</summary>
public sealed record DbObject
{
    public required string Id { get; init; }
    public required string Schema { get; init; }
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required string Dialect { get; init; }
    public string DefinedInSlug { get; init; } = "";
    public int DefinedAtLine { get; init; }
    public List<Column> Columns { get; init; } = [];
    /// <summary>Column names forming the primary key; empty = no PK (a lint signal).</summary>
    public List<string> PrimaryKey { get; init; } = [];
    public List<string> Indexes { get; init; } = [];
    public List<string> ReferencesObjectIds { get; init; } = [];
    public int Cyclomatic { get; init; }
    public int StatementCount { get; init; }
    /// <summary>Verbatim source slice, for the formatter and the detail page.</summary>
    public string Body { get; init; } = "";
}

public sealed record Column
{
    public required string Name { get; init; }
    public required string DataType { get; init; }
    public bool Nullable { get; init; } = true;
    public bool IsIdentity { get; init; }
    public string Default { get; init; } = "";
}

public sealed record ForeignKey
{
    public required string FromObjectId { get; init; }
    /// <summary>"" when the target table was not present in the scan.</summary>
    public string ToObjectId { get; init; } = "";
    public List<string> FromColumns { get; init; } = [];
    public List<string> ToColumns { get; init; } = [];
    public string Name { get; init; } = "";
    public string OnDelete { get; init; } = "";
}

/// <summary>Object-to-object dependency (proc/view/trigger referencing a table/view/proc).
/// ToObjectId is "" when the referenced object is external/unresolved.</summary>
public sealed record ObjectDep
{
    public required string FromObjectId { get; init; }
    public string ToObjectId { get; init; } = "";
    public string ExternalTarget { get; init; } = "";
    /// <summary>"read"|"insert"|"update"|"delete"|"exec"|"exec-dynamic"|"fk"|"select-star"|
    /// "update-nowhere"|"delete-nowhere".</summary>
    public required string Kind { get; init; }
}

/// <summary>A lint finding (Phase 4).</summary>
public sealed record LintFinding
{
    public required string RuleId { get; init; }
    /// <summary>0 Critical, 1 High, 2 Medium, 3 Low.</summary>
    public required int Severity { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public string ObjectId { get; init; } = "";
    public string Slug { get; init; } = "";
    public int Line { get; init; }
}
