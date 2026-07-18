using ArchSql.Model;

namespace ArchSql.Analysis;

/// <summary>One per dialect. Tier-2 (TSqlScriptDomAnalyzer) parses a full AST; a future Tier-1
/// tokenizer-based analyzer (MySQL/Postgres, Phase 2b, deferred) will implement this same
/// interface. All implementations are stateless and thread-safe (called from the parallel map,
/// exactly like ArchDiagram's ILanguageAnalyzer).</summary>
public interface ISqlDialectAnalyzer
{
    /// <summary>"tsql" | "mysql" | "postgres".</summary>
    string Dialect { get; }
    bool CanHandle(string dialect);

    /// <summary>Parse one file's text into objects, FKs, and intra-file deps. Pure; no shared
    /// state; must not throw on malformed SQL — record a diagnostic and return what parsed.</summary>
    SqlFileFacts Analyze(string relPath, string content);

    /// <summary>Re-emit canonical formatted SQL (Phase 5). Statements that don't parse are
    /// returned unchanged. Empty string return = "formatting unsupported for this dialect"
    /// (degradation contract) — the caller then leaves the file untouched with a warning.</summary>
    string Format(string content);
}

/// <summary>Everything Analyze learns about one file, before slug/id assignment (assigned
/// serially in the reduce pass). No shared mutable state — safe on any thread. Mirrors
/// ArchDiagram's FileResult.</summary>
public sealed record SqlFileFacts
{
    public string Dialect { get; init; } = "";
    public bool ParsedCleanly { get; init; }
    public int StatementCount { get; init; }
    public bool HasCredential { get; init; }
    public List<DbObject> Objects { get; init; } = [];
    public List<ForeignKey> ForeignKeys { get; init; } = [];
    public List<ObjectDep> Dependencies { get; init; } = [];
    public List<string> Diagnostics { get; init; } = [];
}
