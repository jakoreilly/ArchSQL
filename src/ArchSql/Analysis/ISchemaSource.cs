namespace ArchSql.Analysis;

/// <summary>Where schema comes from. v1 ships ONLY SqlFileSchemaSource (folder of .sql). A future
/// LiveDbSchemaSource (MSSQL/MySQL/Postgres via information_schema / catalog views) would
/// implement this same interface and slot into Pipeline with zero changes to the analysis/site
/// layers. NOT implemented in v1 — see plan.md Scope decision D (live DB connections excluded).</summary>
public interface ISchemaSource
{
    /// <summary>Raw SQL text units to analyze, each with a logical path. For the file source
    /// these are files; for a future live source these would be per-object DDL fetched from the
    /// catalog.</summary>
    IEnumerable<(string RelPath, string Content, string Dialect)> Read();
}
