using ArchSql.Analysis;
using ArchSql.Model;

namespace ArchSql.Live;

/// <summary>Merges catalog detail (column width/precision/scale/collation, static index
/// definitions, row counts and storage size) onto an already-built model's objects. Runs after
/// analysis, since it enriches objects the analyzer already produced from reconstructed DDL rather
/// than replacing them. Pure and DB-free — the merge keys are (schema, table[, column]), matched
/// case-insensitively against the catalog's own casing.</summary>
public static class CatalogDetailMerge
{
    public static SqlModel Merge(SqlModel model, CatalogDetail detail)
    {
        var columnsByTable = detail.Columns
            .GroupBy(c => IdentifierRules.NormalizeId(c.Schema, c.Table, "tsql"))
            .ToDictionary(g => g.Key, g => g.ToDictionary(c => c.Column, StringComparer.OrdinalIgnoreCase), StringComparer.Ordinal);

        var indexDefs = IndexInventory.Build(detail.Indexes).ToLookup(i => i.ObjectId, StringComparer.Ordinal);
        var statsByTable = detail.TableStats
            .ToDictionary(t => IdentifierRules.NormalizeId(t.Schema, t.Table, "tsql"), t => t, StringComparer.Ordinal);

        var objects = model.Objects.Select(o =>
        {
            var updated = o;
            if (columnsByTable.TryGetValue(o.Id, out var cols) && o.Columns.Count > 0)
            {
                updated = updated with
                {
                    Columns = updated.Columns.Select(c => cols.TryGetValue(c.Name, out var detailRow)
                        ? c with { MaxLength = detailRow.MaxLength, Precision = detailRow.Precision, Scale = detailRow.Scale, Collation = detailRow.Collation }
                        : c).ToList(),
                };
            }
            if (indexDefs[o.Id].Any()) { updated = updated with { IndexDetails = indexDefs[o.Id].ToList() }; }
            if (statsByTable.TryGetValue(o.Id, out var stats)) { updated = updated with { RowCount = stats.RowCount, ReservedKb = stats.ReservedKb }; }
            return updated;
        }).ToList();

        return model with { Objects = objects };
    }
}
