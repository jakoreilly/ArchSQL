using ArchSql.Model;

namespace ArchSql.Analysis;

/// <summary>Groups objects into likely bounded contexts by name prefix (the first underscore-
/// delimited token). Flat single-schema databases encode
/// their domains in naming; this makes that structure visible and measures cross-domain coupling.
/// Pure and deterministic.</summary>
public static class DomainGrouping
{
    public sealed record Domain(string Name, int ObjectCount, int Tables, int Programmable, int InternalEdges, int OutgoingEdges, int IncomingEdges);
    public sealed record CrossEdge(string From, string To, int Count);
    public sealed record Result(List<Domain> Domains, List<CrossEdge> CrossEdges);

    /// <summary>The domain bucket for an object name: the first non-empty underscore-delimited
    /// token, else "(ungrouped)".</summary>
    public static string DomainOf(string name)
    {
        foreach (var token in name.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = token.Trim();
            if (t.Length > 0) { return t; }
        }
        return "(ungrouped)";
    }

    public static Result Compute(SqlModel model)
    {
        var domainOf = model.Objects.ToDictionary(o => o.Id, o => DomainOf(o.Name), StringComparer.Ordinal);

        var counts = new Dictionary<string, (int Objs, int Tables, int Prog)>(StringComparer.Ordinal);
        foreach (var o in model.Objects)
        {
            var d = domainOf[o.Id];
            var c = counts.GetValueOrDefault(d);
            counts[d] = (c.Objs + 1,
                c.Tables + (o.Kind == "table" ? 1 : 0),
                c.Prog + (o.Kind is "procedure" or "function" or "trigger" ? 1 : 0));
        }

        var internalEdges = new Dictionary<string, int>(StringComparer.Ordinal);
        var outgoing = new Dictionary<string, int>(StringComparer.Ordinal);
        var incoming = new Dictionary<string, int>(StringComparer.Ordinal);
        var cross = new Dictionary<(string, string), int>();
        var seen = new HashSet<(string, string)>();
        foreach (var dep in model.Dependencies)
        {
            if (dep.ToObjectId.Length == 0 || !domainOf.TryGetValue(dep.FromObjectId, out var from) || !domainOf.TryGetValue(dep.ToObjectId, out var to)) { continue; }
            if (!seen.Add((dep.FromObjectId, dep.ToObjectId))) { continue; }
            if (from == to) { internalEdges[from] = internalEdges.GetValueOrDefault(from) + 1; continue; }
            cross[(from, to)] = cross.GetValueOrDefault((from, to)) + 1;
            outgoing[from] = outgoing.GetValueOrDefault(from) + 1;
            incoming[to] = incoming.GetValueOrDefault(to) + 1;
        }

        var domains = counts
            .Select(kv => new Domain(kv.Key, kv.Value.Objs, kv.Value.Tables, kv.Value.Prog,
                internalEdges.GetValueOrDefault(kv.Key), outgoing.GetValueOrDefault(kv.Key), incoming.GetValueOrDefault(kv.Key)))
            .OrderByDescending(d => d.ObjectCount).ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToList();

        var crossEdges = cross
            .Select(kv => new CrossEdge(kv.Key.Item1, kv.Key.Item2, kv.Value))
            .OrderByDescending(e => e.Count).ThenBy(e => e.From, StringComparer.Ordinal).ThenBy(e => e.To, StringComparer.Ordinal)
            .ToList();

        return new Result(domains, crossEdges);
    }
}
