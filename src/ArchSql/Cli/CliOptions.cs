namespace ArchSql.Cli;

public sealed record CliOptions
{
    public required string SourcePath { get; init; }
    public string OutDir { get; init; } = "site";
    public bool Open { get; init; } = true;
    public int MaxNodes { get; init; } = 60;
    public List<string> Exclude { get; init; } = [];
    /// <summary>"tsql" | "mysql" | "postgres" | "auto". v1 only deep-analyzes "tsql".</summary>
    public string ForceDialect { get; init; } = "auto";
    /// <summary>CI gates to check after generation (comma-separated). A tripped gate exits 3
    /// (distinct from usage error 2 and crash 1) — the site is still written.</summary>
    public List<string> FailOn { get; init; } = [];
    /// <summary>Optional path to write a SARIF 2.1.0 log; null = don't write one.</summary>
    public string? SarifPath { get; init; }

    public static CliOptions? Parse(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Console.Error.WriteLine("Usage: archsql <path-to-folder> [--out <dir>] [--no-open] [--max-nodes <n>] [--exclude <dirname>]... [--dialect <tsql|mysql|postgres|auto>] [--fail-on <gate>[,<gate>...]] [--sarif <path>]");
            Console.Error.WriteLine($"  --fail-on gates: {string.Join(", ", Analysis.SqlCiGate.KnownGates.Keys.OrderBy(k => k, StringComparer.Ordinal))}. On a tripped gate the site is still written and the process exits 3 (2 = usage error, 1 = crash).");
            exitCode = args.Length == 0 ? 2 : 0;
            return null;
        }

        var source = Path.GetFullPath(args[0]);
        if (!Directory.Exists(source))
        {
            Console.Error.WriteLine($"error: '{source}' is not a directory.");
            exitCode = 2;
            return null;
        }

        string? outDir = null;
        var open = true;
        var maxNodes = 60;
        var exclude = new List<string>();
        var dialect = "auto";
        var failOn = new List<string>();
        string? sarifPath = null;

        // Flags grouped by shape (no-value boolean vs. single-value), one branch per SHAPE
        // rather than one per flag (keeps cognitive complexity low — copies CliOptions.Parse
        // in ArchDiagram). --max-nodes and --fail-on keep their own branch: both need extra
        // validation beyond "does a value follow".
        var boolFlags = new Dictionary<string, Action>(StringComparer.Ordinal)
        {
            ["--no-open"] = () => open = false,
        };
        var valueFlags = new Dictionary<string, Action<string>>(StringComparer.Ordinal)
        {
            ["--out"] = v => outDir = v,
            ["--exclude"] = v => exclude.Add(v),
            ["--dialect"] = v => dialect = v,
            ["--sarif"] = v => sarifPath = v,
        };

        for (var i = 1; i < args.Length; i++)
        {
            if (boolFlags.TryGetValue(args[i], out var setBool)) { setBool(); }
            else if (valueFlags.TryGetValue(args[i], out var setValue) && i + 1 < args.Length) { setValue(args[++i]); }
            else if (args[i] == "--max-nodes" && i + 1 < args.Length && int.TryParse(args[i + 1], out var n)) { maxNodes = Math.Max(10, n); i++; }
            else if (args[i] == "--fail-on" && i + 1 < args.Length)
            {
                if (!TryParseFailOn(args[++i], failOn, out exitCode)) { return null; }
            }
            else
            {
                Console.Error.WriteLine($"error: unknown argument '{args[i]}'.");
                exitCode = 2;
                return null;
            }
        }

        if (dialect is not ("auto" or "tsql" or "mysql" or "postgres"))
        {
            Console.Error.WriteLine($"error: unknown --dialect '{dialect}'. Valid values: auto, tsql, mysql, postgres.");
            exitCode = 2;
            return null;
        }

        outDir ??= "site-" + Slugify(Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));

        return new CliOptions
        {
            SourcePath = source,
            OutDir = Path.GetFullPath(outDir, Environment.CurrentDirectory),
            Open = open,
            MaxNodes = maxNodes,
            Exclude = exclude,
            ForceDialect = dialect,
            FailOn = failOn,
            SarifPath = sarifPath,
        };
    }

    private static bool TryParseFailOn(string arg, List<string> failOn, out int exitCode)
    {
        exitCode = 0;
        var requested = arg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var unknown = requested.Where(g => !Analysis.SqlCiGate.KnownGates.ContainsKey(g)).ToList();
        if (unknown.Count > 0)
        {
            Console.Error.WriteLine($"error: unknown --fail-on gate(s): {string.Join(", ", unknown)}. "
                + $"Valid gates: {string.Join(", ", Analysis.SqlCiGate.KnownGates.Keys.OrderBy(k => k, StringComparer.Ordinal))}.");
            exitCode = 2;
            return false;
        }
        failOn.AddRange(requested);
        return true;
    }

    private static string Slugify(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { return "project"; }
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-');
        }
        var slug = sb.ToString().Trim('-', '.');
        return slug.Length == 0 ? "project" : slug;
    }
}
