using ArchSql.Model;

namespace ArchSql.Analysis;

/// <summary>"What breaks if I change X?" — transitive dependents (reverse edges) plus FK-cascade and
/// exec/write chains. BFS per query with a depth cap and cycle detection (broken/aspirational DDL in
/// script folders can contain cascade cycles SQL Server would reject at CREATE time). Pure and
/// deterministic.</summary>
public static class ImpactGraph
{
    public const int MaxDepth = 32;

    public sealed record Edge(string From, string To, string Kind);
    public sealed record Hit(string ObjectId, int Depth, string ViaKind);

    /// <summary>Reverse adjacency: dependents-of. Includes CRUD writes/reads, proc/view refs, exec,
    /// and FK edges (a change to the parent impacts the child table). Sorted lists for deterministic
    /// traversal order.</summary>
    public static Dictionary<string, List<Edge>> BuildReverse(SqlModel model)
    {
        var edges = new List<Edge>();
        foreach (var d in model.Dependencies)
        {
            if (d.ToObjectId.Length == 0) { continue; }
            edges.Add(new Edge(d.FromObjectId, d.ToObjectId, d.Kind));
        }
        foreach (var fk in model.ForeignKeys)
        {
            if (fk.ToObjectId.Length == 0) { continue; }
            var kind = fk.OnDelete.Contains("Cascade", StringComparison.OrdinalIgnoreCase) ? "fk-cascade" : "fk";
            edges.Add(new Edge(fk.FromObjectId, fk.ToObjectId, kind));
        }
        // Distinct by (From, To, Kind): one object legitimately produces the same dependency many
        // times (e.g. a proc reads a table in several statements), but a repeated reverse edge just
        // inflates the blast-radius list and duplicates rows in the impact table.
        return edges
            .DistinctBy(e => (e.From, e.To, e.Kind))
            .GroupBy(e => e.To, StringComparer.Ordinal)
            .ToDictionary(g => g.Key,
                g => g.OrderBy(e => e.From, StringComparer.Ordinal).ThenBy(e => e.Kind, StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);
    }

    /// <summary>Everything transitively affected by a change to <paramref name="rootId"/>. Returns
    /// hits sorted by (Depth, ObjectId). DepthCapped is true if MaxDepth was hit before the
    /// traversal exhausted (i.e. a long chain or a cycle).</summary>
    public static (List<Hit> Hits, bool DepthCapped) Dependents(Dictionary<string, List<Edge>> reverse, string rootId)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { rootId };
        var hits = new List<Hit>();
        var queue = new Queue<(string Id, int Depth, string Via)>();
        queue.Enqueue((rootId, 0, ""));
        var capped = false;
        while (queue.Count > 0)
        {
            var (id, depth, via) = queue.Dequeue();
            if (depth > 0) { hits.Add(new Hit(id, depth, via)); }
            if (depth >= MaxDepth) { capped = true; continue; }
            foreach (var e in reverse.GetValueOrDefault(id) ?? [])
            {
                if (visited.Add(e.From)) { queue.Enqueue((e.From, depth + 1, e.Kind)); }
            }
        }
        hits.Sort((a, b) => a.Depth != b.Depth ? a.Depth - b.Depth : string.CompareOrdinal(a.ObjectId, b.ObjectId));
        return (hits, capped);
    }
}
