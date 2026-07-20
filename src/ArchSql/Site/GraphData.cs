using System.Text.Json;
using ArchSql.Analysis;
using ArchSql.Model;

namespace ArchSql.Site;

/// <summary>Builds the object/dependency graph payload the client-side engine in site.js consumes
/// as window.ARCH_QUERY (the Explore console and the neighborhood diagrams). Field names match the
/// engine's expectations exactly: it maps query tokens "fanin"/"fanout" to node properties fanIn/
/// fanOut, and searches node.path as a case-insensitive substring. Written once as a shared asset
/// (like SearchIndex) so it works from file:// and stays linear in object count.</summary>
public static class GraphData
{
    public static void WriteAsset(SiteContext ctx, string outDir)
    {
        var model = ctx.Model;
        var execById = model.Runtime.Available
            ? model.Runtime.ObjectStats.GroupBy(s => s.ObjectId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().ExecutionCount, StringComparer.Ordinal)
            : null;

        var nodes = model.Objects
            .OrderBy(o => o.Id, StringComparer.Ordinal)
            .Select(o => BuildNode(o, ctx, execById))
            .ToList();

        var edges = model.Dependencies
            .Where(d => d.ToObjectId.Length > 0)
            .Select(d => new { source = d.FromObjectId, target = d.ToObjectId, kind = d.Kind })
            .Concat(model.ForeignKeys.Where(fk => fk.ToObjectId.Length > 0).Select(fk => new
            {
                source = fk.FromObjectId,
                target = fk.ToObjectId,
                kind = fk.OnDelete.Contains("Cascade", StringComparison.OrdinalIgnoreCase) ? "fk-cascade" : "fk",
            }))
            .DistinctBy(e => (e.source, e.target, e.kind))
            .OrderBy(e => e.source, StringComparer.Ordinal).ThenBy(e => e.target, StringComparer.Ordinal)
            .ToList();

        var payload = new { nodes, edges };
        var json = JsonSerializer.Serialize(payload);

        var assetsDir = Path.Combine(outDir, "assets");
        Directory.CreateDirectory(assetsDir);
        File.WriteAllText(Path.Combine(assetsDir, "graph-data.js"), $"window.ARCH_QUERY={json};");
        File.WriteAllText(Path.Combine(outDir, "graph.json"), json);
    }

    /// <summary>The &lt;script src&gt; tag pointing at the shared payload, relative to the page.
    /// Must load before assets/site.js so the query-engine/neighborhood IIFEs see the data.</summary>
    public static string ScriptSrc(string relRoot) => $"<script src=\"{relRoot}assets/graph-data.js\"></script>";

    private static object BuildNode(DbObject o, SiteContext ctx, Dictionary<string, long>? execById)
    {
        var execs = execById is not null && execById.TryGetValue(o.Id, out var e) ? e : -1L;
        return new
        {
            id = o.Id,
            label = o.Name,
            path = $"{o.Schema}.{o.Name}",
            folder = o.Schema,
            lang = o.Kind,
            loc = o.StatementCount > 0 ? o.StatementCount : CountLines(o.Body),
            cog = o.Cyclomatic,
            fanIn = ctx.FanIn.GetValueOrDefault(o.Id),
            fanOut = ctx.FanOut.GetValueOrDefault(o.Id),
            href = "object.html?id=" + Uri.EscapeDataString(o.Id),
            execs,
        };
    }

    private static int CountLines(string body) => string.IsNullOrEmpty(body) ? 0 : body.AsSpan().Count('\n') + 1;
}
