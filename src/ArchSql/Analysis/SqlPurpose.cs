using ArchSql.Model;

namespace ArchSql.Analysis;

/// <summary>Heuristic one-line purpose text for objects and files, table-driven via a
/// name-conventions table evaluated as a priority cascade.</summary>
public static class SqlPurpose
{
    private static readonly (string Prefix, string Purpose)[] NamePrefixConventions =
    [
        ("usp_", "Stored procedure"),
        ("sp_", "Stored procedure"),
        ("vw_", "View"),
        ("fn_", "Function"),
        ("udf_", "User-defined function"),
        ("trg_", "Trigger"),
    ];

    private static readonly (string Suffix, string Purpose)[] NameSuffixConventions =
    [
        ("_audit", "Audit table"),
        ("_log", "Log table"),
        ("_history", "History table"),
        ("_v", "View"),
    ];

    public static string ForObject(DbObject o)
    {
        foreach (var (prefix, purpose) in NamePrefixConventions)
        {
            if (o.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { return purpose; }
        }
        foreach (var (suffix, purpose) in NameSuffixConventions)
        {
            if (o.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { return purpose; }
        }
        return o.Kind switch
        {
            "table" => "Table",
            "view" => "View",
            "procedure" => "Stored procedure",
            "function" => "Function",
            "trigger" => "Trigger",
            _ => "Database object",
        };
    }

    /// <summary>A file's purpose is its dominant object kind ("2 tables, 1 view", etc.), or a
    /// generic fallback for files that defined nothing this scan could parse.</summary>
    public static string ForFile(SqlFile file, IReadOnlyList<DbObject> objectsInFile)
    {
        if (objectsInFile.Count == 0)
        {
            return file.ParsedCleanly ? "No schema objects found in this file." : "Could not be parsed; see Diagnostics.";
        }
        var byKind = objectsInFile.GroupBy(o => o.Kind, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal);
        return string.Join(", ", byKind.Select(g => $"{g.Count()} {Pluralize(g.Key, g.Count())}"));
    }

    private static string Pluralize(string kind, int count) => count == 1 ? kind : kind + "s";
}
