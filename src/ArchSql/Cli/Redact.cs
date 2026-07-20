namespace ArchSql.Cli;

/// <summary>Scrubs connection-string secrets out of any text before it is printed or logged.
/// Connection strings carry passwords, so a driver exception that echoes one must never reach
/// stdout/stderr intact.</summary>
public static class Redact
{
    private static readonly string[] SensitiveKeys =
    [
        "password", "pwd", "user id", "uid", "server", "data source", "address", "addr", "network address",
    ];

    /// <summary>Replaces the value of any sensitive "key=value" pair (';'-delimited) with
    /// &lt;redacted&gt;. Leaves non-connection-string text unchanged.</summary>
    public static string Message(string text)
    {
        if (string.IsNullOrEmpty(text)) { return text; }
        var parts = text.Split(';');
        for (var i = 0; i < parts.Length; i++)
        {
            var eq = parts[i].IndexOf('=');
            if (eq <= 0) { continue; }
            var key = parts[i][..eq].Trim().ToLowerInvariant();
            if (Array.IndexOf(SensitiveKeys, key) >= 0)
            {
                parts[i] = parts[i][..eq] + "=<redacted>";
            }
        }
        return string.Join(';', parts);
    }
}
