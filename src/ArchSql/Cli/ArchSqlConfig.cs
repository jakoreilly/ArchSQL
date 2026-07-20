using System.Text.Json;

namespace ArchSql.Cli;

/// <summary>Optional project-level configuration, e.g. archsql.config.json.</summary>
public sealed record ArchSqlConfig
{
    /// <summary>Glob patterns (matched against object name and schema.name id) excluded from the
    /// model before analysis-wide dedup. Empty by default — no built-in exclusions.</summary>
    public List<string> ExcludePatterns { get; init; } = [];
}

/// <summary>Loads ArchSqlConfig from an explicit path, or discovers archsql.config.json in the
/// current directory then the scan root. A missing file is not an error — it yields an empty
/// config, since exclusion is opt-in.</summary>
public static class ConfigLoader
{
    private const string FileName = "archsql.config.json";

    public static ArchSqlConfig Load(string? explicitPath, string scanRoot, out string? error)
    {
        error = null;
        var path = explicitPath ?? Discover(scanRoot);
        if (path is null) { return new ArchSqlConfig(); }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ArchSqlConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new ArchSqlConfig();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = $"could not read config '{path}': {ex.Message}";
            return new ArchSqlConfig();
        }
    }

    private static string? Discover(string scanRoot)
    {
        var inCwd = Path.Combine(Environment.CurrentDirectory, FileName);
        if (File.Exists(inCwd)) { return inCwd; }
        var inRoot = Path.Combine(scanRoot, FileName);
        return File.Exists(inRoot) ? inRoot : null;
    }
}
