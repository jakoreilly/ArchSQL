using ArchSql.Scanning;

namespace ArchSql.Analysis;

/// <summary>v1's only ISchemaSource implementation: reads .sql files from a folder on disk.</summary>
public sealed class SqlFileSchemaSource(string root, IReadOnlyList<string> exclude, string forceDialect, List<string> diagnostics) : ISchemaSource
{
    public IEnumerable<(string RelPath, string Content, string Dialect)> Read()
    {
        foreach (var entry in SqlFileScanner.Scan(root, exclude, diagnostics))
        {
            string content;
            try { content = File.ReadAllText(entry.AbsPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add($"Could not read {entry.RelPath}: {ex.Message}");
                continue;
            }
            var dialect = DialectDetector.Detect(content, forceDialect);
            yield return (entry.RelPath, content, dialect);
        }
    }
}
