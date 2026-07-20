namespace ArchSql.Live;

/// <summary>Formats a SQL Server column type into the text a CREATE TABLE would use
/// (e.g. "nvarchar(100)", "decimal(18,2)", "nvarchar(max)", "int"). Pure and DB-free.</summary>
public static class SqlTypeText
{
    // Types whose length is meaningful in DDL. For n-prefixed string types sys.columns.max_length
    // is in BYTES (2 per character), and -1 means (max).
    private static readonly HashSet<string> LengthTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "varchar", "char", "nvarchar", "nchar", "varbinary", "binary",
    };
    private static readonly HashSet<string> PrecisionScaleTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "decimal", "numeric",
    };
    private static readonly HashSet<string> DoubleByteTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "nvarchar", "nchar",
    };

    public static string Format(string baseType, int maxLength, byte precision, byte scale)
    {
        var t = baseType.ToLowerInvariant();
        if (PrecisionScaleTypes.Contains(t)) { return $"{t}({precision},{scale})"; }
        if (LengthTypes.Contains(t)) { return $"{t}({LengthText(t, maxLength)})"; }
        return t;
    }

    private static string LengthText(string type, int maxLength)
    {
        if (maxLength == -1) { return "max"; }
        var chars = DoubleByteTypes.Contains(type) ? maxLength / 2 : maxLength;
        return chars.ToString();
    }
}
