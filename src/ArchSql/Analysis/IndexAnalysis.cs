using ArchSql.Model;

namespace ArchSql.Analysis;

/// <summary>Index-health checks over the static index inventory (IndexDef) and, where available,
/// runtime index usage (IndexStat). Pure and deterministic; every check degrades to an empty list
/// when IndexDetails/Runtime data is absent (file scan, or a login without the needed permission).</summary>
public static class IndexAnalysis
{
    public sealed record DuplicatePair(string ObjectId, string IndexA, string IndexB, string Relationship);
    public sealed record UnusedIndex(string ObjectId, string IndexName, long UserUpdates, string DropStatement);

    /// <summary>Tables with at least one column but no clustered index recorded — a heap.</summary>
    public static List<DbObject> Heaps(SqlModel model) =>
        model.Objects
            .Where(o => o.Kind == "table" && o.Columns.Count > 0 && o.IndexDetails.Count > 0
                && !o.IndexDetails.Any(i => i.IsClustered))
            .ToList();

    /// <summary>Pairs of indexes on the same table whose key-column lists are equal, or where one is
    /// an ordered prefix of the other — duplicate or redundant coverage.</summary>
    public static List<DuplicatePair> DuplicateIndexes(SqlModel model)
    {
        var pairs = new List<DuplicatePair>();
        foreach (var o in model.Objects.Where(o => o.IndexDetails.Count > 1))
        {
            var indexes = o.IndexDetails.OrderBy(i => i.Name, StringComparer.Ordinal).ToList();
            for (var i = 0; i < indexes.Count; i++)
            {
                for (var j = i + 1; j < indexes.Count; j++)
                {
                    var relationship = Compare(indexes[i].KeyColumns, indexes[j].KeyColumns);
                    if (relationship is not null) { pairs.Add(new DuplicatePair(o.Id, indexes[i].Name, indexes[j].Name, relationship)); }
                }
            }
        }
        return pairs;
    }

    private static string? Compare(List<string> a, List<string> b)
    {
        if (a.Count == 0 || b.Count == 0) { return null; }
        if (a.SequenceEqual(b, StringComparer.OrdinalIgnoreCase)) { return "identical key columns"; }
        var (shorter, longer) = a.Count <= b.Count ? (a, b) : (b, a);
        if (shorter.Count > 0 && longer.Take(shorter.Count).SequenceEqual(shorter, StringComparer.OrdinalIgnoreCase))
        {
            return "one index's key is a prefix of the other's";
        }
        return null;
    }

    /// <summary>Static indexes joined to runtime usage where seeks+scans+lookups is zero and writes
    /// are non-zero — a genuine drop candidate, with the exact DROP statement for the operator to
    /// review.</summary>
    public static List<UnusedIndex> UnusedIndexes(SqlModel model)
    {
        if (!model.Runtime.Available) { return []; }
        var byId = model.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        var result = new List<UnusedIndex>();
        foreach (var o in model.Objects)
        {
            foreach (var idx in o.IndexDetails)
            {
                if (idx.IsPrimaryKey || idx.IsDisabled) { continue; }
                if (!model.Runtime.IndexStats.Any(s => s.ObjectId == o.Id && s.IndexName == idx.Name && s.IsUnused)) { continue; }
                var usage = model.Runtime.IndexStats.First(s => s.ObjectId == o.Id && s.IndexName == idx.Name);
                var drop = $"DROP INDEX [{idx.Name}] ON [{byId[o.Id].Schema}].[{byId[o.Id].Name}];";
                result.Add(new UnusedIndex(o.Id, idx.Name, usage.UserUpdates, drop));
            }
        }
        return result.OrderByDescending(u => u.UserUpdates).ThenBy(u => u.ObjectId, StringComparer.Ordinal).ToList();
    }
}
