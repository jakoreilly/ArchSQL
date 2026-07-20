namespace ArchSql.Analysis;

/// <summary>Compares column base-type names for the schema differ. The model currently stores only
/// the base type identifier (see TSqlScriptDomAnalyzer's CreateTableStatement handling), not
/// length/precision/scale, so this compares base types only; length-narrowing detection is a
/// documented follow-up, not a silent gap.</summary>
public static class SqlTypeComparer
{
    public enum TypeChange { Same, Compatible, Narrowing, Incompatible }

    private static string Canon(string t) => t.Trim().ToLowerInvariant() switch
    {
        "numeric" => "decimal",
        "int" or "integer" => "int",
        var s => s,
    };

    public static TypeChange Compare(string oldType, string newType)
    {
        var o = Canon(oldType);
        var n = Canon(newType);
        if (o == n) { return TypeChange.Same; }
        if ((o, n) is ("int", "bigint") or ("smallint", "int") or ("varchar", "nvarchar")) { return TypeChange.Compatible; }
        if ((o, n) is ("bigint", "int") or ("nvarchar", "varchar") or ("int", "smallint")) { return TypeChange.Narrowing; }
        return TypeChange.Incompatible;
    }
}
