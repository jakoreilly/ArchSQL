namespace ArchSql.Analysis;

/// <summary>The single place cross-dialect identifier normalization happens. Every caller that
/// needs to key or look up a DbObject.Id MUST go through NormalizeId — never concatenate
/// schema+name directly.
///
/// The three engines disagree on identifier case: T-SQL is case-insensitive by collation and
/// delimits with [brackets] or "quotes"; PostgreSQL folds UNQUOTED identifiers to lowercase and
/// "Quoted" is case-sensitive, delimiter is "double quotes"; MySQL case-sensitivity is
/// filesystem-dependent, delimiter is `backticks`. We lowercase for the KEY only — DbObject.Schema
/// and DbObject.Name keep their original casing for display.</summary>
public static class IdentifierRules
{
    public static string NormalizeId(string schema, string name, string dialect)
    {
        schema = StripDelims(schema);
        name = StripDelims(name);
        if (schema.Length == 0) { schema = DefaultSchema(dialect); }
        return (schema + "." + name).ToLowerInvariant();
    }

    private static string StripDelims(string s) => s.Trim().Trim('[', ']', '"', '`').Trim();

    private static string DefaultSchema(string dialect) => dialect switch
    {
        "tsql" => "dbo",
        "postgres" => "public",
        _ => "", // mysql: schema == database, often absent
    };
}
