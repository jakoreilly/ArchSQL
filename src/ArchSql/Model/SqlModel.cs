namespace ArchSql.Model;

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
    /// <summary>"select"|"insert"|"update"|"delete"|"exec"|"fk".</summary>
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
