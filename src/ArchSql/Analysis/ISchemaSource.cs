namespace ArchSql.Analysis;

/// <summary>Where schema comes from. Implementations include a folder-of-.sql source and a live
/// SQL Server catalog source; both implement this same interface and slot into Pipeline with zero
/// changes to the analysis/site layers. Additional live sources (MySQL/Postgres via
/// information_schema) can be added the same way.</summary>
public interface ISchemaSource
{
    /// <summary>Raw SQL text units to analyze, each with a logical path. For the file source
    /// these are files; for a future live source these would be per-object DDL fetched from the
    /// catalog.</summary>
    IEnumerable<(string RelPath, string Content, string Dialect)> Read();
}
