using ArchSql.Model;

namespace ArchSql.Analysis;

/// <summary>Guarantees a model's identity invariants before any consumer keys objects or runtime
/// facts by id: object ids are unique, and runtime stat lists hold no duplicate keys. A brownfield
/// database can legitimately yield collisions (two definitions whose authored text normalizes to
/// the same schema.object; two DMV rows normalizing to the same id), so every entry point — the
/// live/scan pipeline and the --from-model loader — routes through here to keep the many downstream
/// ToDictionary-by-id calls safe.</summary>
public static class ModelNormalizer
{
    /// <summary>Normalizes a fully-loaded model (used by the --from-model path).</summary>
    public static SqlModel Normalize(SqlModel model)
    {
        var diagnostics = new List<string>(model.Diagnostics);
        var objects = DedupeObjects(model.Objects, diagnostics);
        var runtime = DedupeRuntime(model.Runtime);
        return model with { Objects = objects, Runtime = runtime, Diagnostics = diagnostics };
    }

    /// <summary>Keeps the first object per id, recording each dropped duplicate as a diagnostic.
    /// Order-preserving so the result stays deterministic.</summary>
    public static List<DbObject> DedupeObjects(List<DbObject> objects, List<string> diagnostics)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<DbObject>(objects.Count);
        foreach (var o in objects)
        {
            if (seen.Add(o.Id)) { result.Add(o); }
            else { diagnostics.Add($"Duplicate object id '{o.Id}' (defined in {o.DefinedInSlug}) was skipped; the first definition is kept."); }
        }
        return result;
    }

    /// <summary>Collapses duplicate keys in the runtime stat lists: object stats are summed per id
    /// (two DMV rows can normalize to one id), index and missing-index rows keep their first
    /// occurrence. Preserves the existing sort order.</summary>
    public static RuntimeStats DedupeRuntime(RuntimeStats rt)
    {
        if (!rt.Available) { return rt; }

        var objectStats = rt.ObjectStats
            .GroupBy(s => s.ObjectId, StringComparer.Ordinal)
            .Select(g => new ObjectStat
            {
                ObjectId = g.Key,
                ExecutionCount = g.Sum(s => s.ExecutionCount),
                TotalWorkerTimeMs = g.Sum(s => s.TotalWorkerTimeMs),
                TotalLogicalReads = g.Sum(s => s.TotalLogicalReads),
            })
            .OrderByDescending(s => s.ExecutionCount).ThenBy(s => s.ObjectId, StringComparer.Ordinal)
            .ToList();

        var indexStats = DistinctBy(rt.IndexStats, i => (i.ObjectId, i.IndexName));
        var missing = DistinctBy(rt.MissingIndexes, m => (m.ObjectId, m.EqualityColumns, m.InequalityColumns, m.IncludedColumns));

        return rt with { ObjectStats = objectStats, IndexStats = indexStats, MissingIndexes = missing };
    }

    private static List<T> DistinctBy<T, TKey>(List<T> items, Func<T, TKey> key)
    {
        var seen = new HashSet<TKey>();
        var result = new List<T>(items.Count);
        foreach (var item in items) { if (seen.Add(key(item))) { result.Add(item); } }
        return result;
    }
}
