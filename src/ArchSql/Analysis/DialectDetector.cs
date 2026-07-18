namespace ArchSql.Analysis;

/// <summary>Heuristic dialect detection by marker-token scoring — a linear scan, no
/// backtracking-capable regex (Hard Constraint 6). v1 only deep-analyzes "tsql"; files scored as
/// mysql/postgres are still detected (so a mixed folder doesn't silently mis-scan) but are
/// recorded with ParsedCleanly=false and a diagnostic explaining only T-SQL is analyzed in v1.</summary>
public static class DialectDetector
{
    public static string Detect(string content, string forced)
    {
        if (forced is "tsql" or "mysql" or "postgres") { return forced; }

        var tsql = Score(content, "GO\n", "[dbo]", "NVARCHAR", "@@", "PRINT ", "NOLOCK", "IDENTITY(");
        var mysql = Score(content, "ENGINE=", "AUTO_INCREMENT", "DELIMITER ", "UNSIGNED", "`");
        var postgres = Score(content, "$$", "SERIAL", "RETURNS TRIGGER", "::", "public.", "SECURITY DEFINER");

        if (tsql == 0 && mysql == 0 && postgres == 0) { return "tsql"; } // default: most common in this shop

        if (tsql >= mysql && tsql >= postgres) { return "tsql"; }
        return mysql >= postgres ? "mysql" : "postgres";
    }

    private static int Score(string content, params string[] markers)
    {
        var score = 0;
        foreach (var m in markers)
        {
            var idx = 0;
            while ((idx = content.IndexOf(m, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                score++;
                idx += m.Length;
            }
        }
        return score;
    }
}
