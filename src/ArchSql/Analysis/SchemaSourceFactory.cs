using ArchSql.Cli;
using ArchSql.Live;

namespace ArchSql.Analysis;

/// <summary>Chooses the schema source from the options: a live SQL Server connection when a
/// connection string is present (the `connect` verb), otherwise the folder-of-.sql-files source.
/// This is the single place source selection happens; the pipeline is otherwise source-agnostic.</summary>
public static class SchemaSourceFactory
{
    public static ISchemaSource Create(CliOptions options, List<string> diagnostics)
    {
        if (options.ConnectionString is { Length: > 0 } conn)
        {
            return new SqlServerSchemaSource(conn, options.TimeoutSeconds, diagnostics);
        }
        return new SqlFileSchemaSource(options.SourcePath, options.Exclude, options.ForceDialect, diagnostics);
    }
}
