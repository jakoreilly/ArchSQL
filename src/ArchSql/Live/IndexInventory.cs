using ArchSql.Analysis;
using ArchSql.Model;

namespace ArchSql.Live;

/// <summary>Builds IndexDef records from flat catalog rows: groups (schema, table, index) into one
/// definition with ordered key columns and included columns. Pure and DB-free.</summary>
public static class IndexInventory
{
    public static List<IndexDef> Build(IReadOnlyList<IndexColumnRow> rows)
    {
        var result = new List<IndexDef>();
        foreach (var group in rows.GroupBy(r => (r.Schema, r.Table, r.IndexName)))
        {
            var first = group.First();
            var objectId = IdentifierRules.NormalizeId(group.Key.Schema, group.Key.Table, "tsql");
            var keyCols = group.Where(r => !r.IsIncluded).OrderBy(r => r.KeyOrdinal).Select(r => r.ColumnName).ToList();
            var includedCols = group.Where(r => r.IsIncluded).Select(r => r.ColumnName).ToList();
            result.Add(new IndexDef
            {
                Name = first.IndexName,
                ObjectId = objectId,
                KeyColumns = keyCols,
                IncludedColumns = includedCols,
                IsUnique = first.IsUnique,
                IsPrimaryKey = first.IsPrimaryKey,
                IsClustered = first.TypeDesc.Equals("CLUSTERED", StringComparison.OrdinalIgnoreCase),
                IsDisabled = first.IsDisabled,
            });
        }
        return result
            .OrderBy(i => i.ObjectId, StringComparer.Ordinal).ThenBy(i => i.Name, StringComparer.Ordinal)
            .ToList();
    }
}
