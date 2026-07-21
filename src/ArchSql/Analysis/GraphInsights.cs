using ArchSql.Model;

namespace ArchSql.Analysis;

/// <summary>Architecture-level graph analysis over the object dependency graph: strongly-connected
/// components (dependency cycles), per-object instability, and "god" objects (extreme fan-in/out).
/// Pure and deterministic. Cycles use an iterative Tarjan so a 2,000-node graph can't overflow the
/// stack.</summary>
public static class GraphInsights
{
    public sealed record Insight(
        List<List<string>> Cycles,
        List<GodObject> GodObjects);

    public sealed record GodObject(string ObjectId, int FanIn, int FanOut, double Instability);

    public static Insight Compute(SqlModel model)
    {
        var adj = BuildAdjacency(model, out var fanIn, out var fanOut);
        var cycles = StronglyConnected(adj)
            .Where(c => c.Count > 1)
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c[0], StringComparer.Ordinal)
            .ToList();

        var gods = model.Objects
            .Select(o =>
            {
                var ce = fanOut.GetValueOrDefault(o.Id);
                var ca = fanIn.GetValueOrDefault(o.Id);
                var instability = ca + ce == 0 ? 0.0 : (double)ce / (ca + ce);
                return new GodObject(o.Id, ca, ce, instability);
            })
            .Where(g => g.FanIn + g.FanOut > 0)
            .OrderByDescending(g => g.FanIn + g.FanOut)
            .ThenBy(g => g.ObjectId, StringComparer.Ordinal)
            .ToList();

        return new Insight(cycles, gods);
    }

    private static Dictionary<string, List<string>> BuildAdjacency(SqlModel model, out Dictionary<string, int> fanIn, out Dictionary<string, int> fanOut)
    {
        var adj = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var o in model.Objects) { adj[o.Id] = []; }
        fanIn = model.Objects.ToDictionary(o => o.Id, _ => 0, StringComparer.Ordinal);
        fanOut = model.Objects.ToDictionary(o => o.Id, _ => 0, StringComparer.Ordinal);

        var seen = new HashSet<(string, string)>();
        foreach (var d in model.Dependencies)
        {
            if (d.ToObjectId.Length == 0 || !adj.ContainsKey(d.FromObjectId) || !adj.ContainsKey(d.ToObjectId)) { continue; }
            if (d.FromObjectId == d.ToObjectId) { continue; }
            if (!seen.Add((d.FromObjectId, d.ToObjectId))) { continue; }
            adj[d.FromObjectId].Add(d.ToObjectId);
            fanOut[d.FromObjectId]++;
            fanIn[d.ToObjectId]++;
        }
        return adj;
    }

    /// <summary>Iterative Tarjan strongly-connected components. Returns every SCC (including
    /// singletons); callers filter for size &gt; 1 to get cycles.</summary>
    private static List<List<string>> StronglyConnected(Dictionary<string, List<string>> adj)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var low = new Dictionary<string, int>(StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var result = new List<List<string>>();
        var next = 0;

        foreach (var start in adj.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (index.ContainsKey(start)) { continue; }
            // Explicit DFS stack of (node, childIndex).
            var work = new Stack<(string Node, int Child)>();
            work.Push((start, 0));
            while (work.Count > 0)
            {
                var (v, ci) = work.Pop();
                if (ci == 0)
                {
                    index[v] = low[v] = next++;
                    stack.Push(v); onStack.Add(v);
                }
                var children = adj[v];
                var advanced = false;
                for (var i = ci; i < children.Count; i++)
                {
                    var w = children[i];
                    if (!index.ContainsKey(w))
                    {
                        work.Push((v, i + 1));
                        work.Push((w, 0));
                        advanced = true;
                        break;
                    }
                    if (onStack.Contains(w)) { low[v] = Math.Min(low[v], index[w]); }
                }
                if (advanced) { continue; }
                // All children processed: on return, fold child low-links, then close an SCC root.
                foreach (var w in children) { if (onStack.Contains(w)) { low[v] = Math.Min(low[v], low[w]); } }
                if (low[v] == index[v])
                {
                    var comp = new List<string>();
                    string popped;
                    do { popped = stack.Pop(); onStack.Remove(popped); comp.Add(popped); } while (popped != v);
                    result.Add(comp);
                }
            }
        }
        return result;
    }
}
