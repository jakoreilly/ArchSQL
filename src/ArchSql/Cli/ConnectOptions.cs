namespace ArchSql.Cli;

/// <summary>Parses the `connect` verb into a CliOptions carrying a connection string. The string
/// is read from a file (--conn-file) or the ARCHSQL_CONNECTION env var (--env), never from a bare
/// CLI argument, so it does not land in shell history (Hard Constraint: connection string is a
/// secret).</summary>
public static class ConnectOptions
{
    private const string EnvVar = "ARCHSQL_CONNECTION";

    public static CliOptions? Parse(string[] args, out int exitCode)
    {
        exitCode = 0;
        string? connFile = null;
        var useEnv = false;
        string? outDir = null;
        var open = true;
        var maxNodes = 60;
        var timeout = 30;
        var failOn = new List<string>();

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--conn-file" when i + 1 < args.Length: connFile = args[++i]; break;
                case "--env": useEnv = true; break;
                case "--out" when i + 1 < args.Length: outDir = args[++i]; break;
                case "--no-open": open = false; break;
                case "--max-nodes" when i + 1 < args.Length && int.TryParse(args[i + 1], out var n): maxNodes = Math.Max(10, n); i++; break;
                case "--timeout" when i + 1 < args.Length && int.TryParse(args[i + 1], out var t): timeout = Math.Clamp(t, 1, 600); i++; break;
                case "--fail-on" when i + 1 < args.Length: failOn.AddRange(args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)); break;
                default:
                    Console.Error.WriteLine($"error: unknown argument '{args[i]}'.");
                    exitCode = 2;
                    return null;
            }
        }

        if (connFile is null == !useEnv)
        {
            Console.Error.WriteLine("Usage: archsql connect (--conn-file <path> | --env) [--out <dir>] [--timeout <sec>] [--no-open] [--max-nodes <n>] [--fail-on <gate>...]");
            Console.Error.WriteLine($"  Provide the connection string via a file (--conn-file) or the {EnvVar} environment variable (--env) — never as a plain argument.");
            exitCode = 2;
            return null;
        }

        var connectionString = ReadConnectionString(connFile, useEnv, out var readError);
        if (connectionString is null)
        {
            Console.Error.WriteLine($"error: {readError}");
            exitCode = 2;
            return null;
        }

        return new CliOptions
        {
            SourcePath = "live-db",
            ConnectionString = connectionString,
            TimeoutSeconds = timeout,
            OutDir = Path.GetFullPath(outDir ?? "site-live", Environment.CurrentDirectory),
            Open = open,
            MaxNodes = maxNodes,
            ForceDialect = "tsql",
            FailOn = failOn,
        };
    }

    private static string? ReadConnectionString(string? connFile, bool useEnv, out string error)
    {
        error = "";
        if (useEnv)
        {
            var v = Environment.GetEnvironmentVariable(EnvVar);
            if (string.IsNullOrWhiteSpace(v)) { error = $"{EnvVar} is not set."; return null; }
            return v.Trim();
        }
        if (!File.Exists(connFile)) { error = $"connection file '{connFile}' not found."; return null; }
        var text = File.ReadAllText(connFile).Trim();
        if (text.Length == 0) { error = $"connection file '{connFile}' is empty."; return null; }
        return text;
    }
}
