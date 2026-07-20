using System.Text;

namespace ArchSql.Live;

/// <summary>Reconstructs CREATE DDL text from catalog rows so the live schema flows through the
/// existing T-SQL analyzer unchanged. Tables are rebuilt from columns/PK/FK rows; module objects
/// (views, procs, functions, triggers) pass through their sys.sql_modules definition verbatim.
/// Pure and DB-free — the DTO lists are the test seam.</summary>
public static class LiveDdl
{
    /// <summary>One text unit per object: (logical path, DDL). Logical path is "live/schema.name"
    /// so it reads sensibly as a SqlFile.RelPath.</summary>
    public static List<(string RelPath, string Content)> BuildUnits(
        IReadOnlyList<ObjectRow> objects,
        IReadOnlyList<ColumnRow> columns,
        IReadOnlyList<PkRow> primaryKeys,
        IReadOnlyList<FkRow> foreignKeys,
        IReadOnlyList<ModuleRow> modules)
    {
        var units = new List<(string, string)>();

        var colsByTable = columns.GroupBy(c => (c.Schema, c.Table)).ToDictionary(g => g.Key, g => g.OrderBy(c => c.Ordinal).ToList());
        var pkByTable = primaryKeys.GroupBy(p => (p.Schema, p.Table)).ToDictionary(g => g.Key, g => g.OrderBy(p => p.KeyOrdinal).Select(p => p.Column).ToList());
        var fkByTable = foreignKeys.GroupBy(f => (f.Schema, f.Table)).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var t in objects.Where(o => o.TypeCode.Trim() == "U").OrderBy(o => o.Schema).ThenBy(o => o.Name))
        {
            var key = (t.Schema, t.Name);
            colsByTable.TryGetValue(key, out var cols);
            pkByTable.TryGetValue(key, out var pk);
            fkByTable.TryGetValue(key, out var fks);
            units.Add(($"live/{t.Schema}.{t.Name}", TableDdl(t.Schema, t.Name, cols ?? [], pk ?? [], fks ?? [])));
        }

        foreach (var m in modules.OrderBy(m => m.Schema).ThenBy(m => m.Name))
        {
            units.Add(($"live/{m.Schema}.{m.Name}", m.Definition));
        }

        return units;
    }

    private static string TableDdl(string schema, string name, List<ColumnRow> cols, List<string> pk, List<FkRow> fks)
    {
        var lines = new List<string>();
        foreach (var c in cols)
        {
            var type = SqlTypeText.Format(c.TypeName, c.MaxLength, c.Precision, c.Scale);
            var identity = c.IsIdentity ? " IDENTITY(1,1)" : "";
            var nullText = c.IsNullable ? "NULL" : "NOT NULL";
            lines.Add($"    [{c.Column}] {type}{identity} {nullText}");
        }
        if (pk.Count > 0)
        {
            lines.Add($"    CONSTRAINT [PK_{name}] PRIMARY KEY ({string.Join(", ", pk.Select(c => $"[{c}]"))})");
        }
        foreach (var fk in fks.GroupBy(f => f.FkName))
        {
            var ordered = fk.OrderBy(f => f.Ordinal).ToList();
            var first = ordered[0];
            var fromCols = string.Join(", ", ordered.Select(f => $"[{f.FromColumn}]"));
            var toCols = string.Join(", ", ordered.Select(f => $"[{f.ToColumn}]"));
            var onDelete = OnDeleteClause(first.OnDelete);
            lines.Add($"    CONSTRAINT [{fk.Key}] FOREIGN KEY ({fromCols}) REFERENCES [{first.RefSchema}].[{first.RefTable}] ({toCols}){onDelete}");
        }

        var sb = new StringBuilder();
        sb.Append($"CREATE TABLE [{schema}].[{name}] (\n");
        sb.Append(string.Join(",\n", lines));
        sb.Append("\n);\n");
        return sb.ToString();
    }

    private static string OnDeleteClause(string deleteActionDesc) => deleteActionDesc.ToUpperInvariant() switch
    {
        "CASCADE" => " ON DELETE CASCADE",
        "SET_NULL" => " ON DELETE SET NULL",
        "SET_DEFAULT" => " ON DELETE SET DEFAULT",
        _ => "", // NO_ACTION / unspecified: omit (parses as the default NoAction)
    };
}
