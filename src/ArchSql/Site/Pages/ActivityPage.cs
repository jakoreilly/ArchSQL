using System.Text;
using ArchSql.Model;

namespace ArchSql.Site.Pages;

/// <summary>Runtime activity and issue concentration from a live connection: where execution
/// actually concentrates, which indexes are missing or unused, and where problems cluster by
/// schema. Empty for file scans and for logins without DMV permission.</summary>
public static class ActivityPage
{
    public static string Body(SiteContext ctx)
    {
        var model = ctx.Model;
        var rt = model.Runtime;
        var sb = new StringBuilder();
        sb.Append("<h1>Activity</h1>");
        sb.Append("""
<p class="lede"><strong>Activity &amp; hotspots.</strong> These figures come from the live server's
runtime counters (DMVs). They are cumulative since the server (or its statistics) last reset — not a
fixed time window — and reset when SQL Server restarts. Use them to see <em>where work actually
concentrates</em>, not for precise billing.</p>
""");

        if (!rt.Available)
        {
            sb.Append($"""
<div class="panel empty-state"><div class="big">🔥</div>
<p><strong>No runtime data.</strong> This site was built from a file scan, or the connected login
lacks <code>VIEW DATABASE STATE</code> permission, so execution and index statistics could not be
read. The schema, dependency, CRUD and impact views still reflect the structure. To populate this
page, reconnect with a login that has <code>VIEW DATABASE STATE</code>.</p>
<p class="note">{Html.Encode(rt.Note.Length == 0 ? "Built from a file scan." : rt.Note)}</p>
</div>
""");
            return sb.ToString();
        }

        sb.Append($"""<p class="note">{Html.Encode(rt.Note)}</p>""");
        var hotCount = rt.ObjectStats.Count > 0 ? rt.ObjectStats.Count(s => s.ExecutionCount >= HotThreshold(rt.ObjectStats)) : 0;
        var missingCount = rt.MissingIndexes.Count;
        var unusedCount = rt.IndexStats.Count(i => i.IsUnused);
        sb.Append("<div class=\"tiles\">");
        sb.Append($"""<div class="tile{(hotCount == 0 ? " tile-zero" : "")}"><div class="num">{hotCount:N0}</div><div class="lbl">Hot objects</div></div>""");
        sb.Append($"""<div class="tile{(missingCount == 0 ? " tile-zero" : "")}"><div class="num">{missingCount:N0}</div><div class="lbl">Missing indexes</div></div>""");
        sb.Append($"""<div class="tile{(unusedCount == 0 ? " tile-zero" : "")}"><div class="num">{unusedCount:N0}</div><div class="lbl">Unused indexes</div></div>""");
        sb.Append("</div>");
        AppendHotspots(sb, ctx, rt);
        AppendHeatMap(sb, ctx, rt);
        AppendIndexIssues(sb, ctx, rt);
        AppendIssueConcentration(sb, model, rt);
        return sb.ToString();
    }

    private static void AppendHotspots(StringBuilder sb, SiteContext ctx, RuntimeStats rt)
    {
        sb.Append("<h2>Hotspots</h2>");
        if (rt.ObjectStats.Count == 0)
        {
            sb.Append("""<div class="panel empty-state"><p>No procedure/function execution stats were returned.</p></div>""");
            return;
        }
        var hotThreshold = HotThreshold(rt.ObjectStats);
        sb.Append("""<input class="filter-input" type="search" data-filter-target="#activity-rows" placeholder="Filter by object…" autocomplete="off" spellcheck="false"> <span class="filter-count"></span>""");
        sb.Append("""<table class="grid sortable" data-page-size="20"><thead><tr><th>Object</th><th>Executions</th><th>Logical reads</th><th>CPU (ms)</th><th></th></tr></thead><tbody id="activity-rows">""");
        foreach (var s in rt.ObjectStats)
        {
            var obj = ctx.ById.GetValueOrDefault(s.ObjectId);
            var label = obj is null ? s.ObjectId : $"{obj.Schema}.{obj.Name}";
            var cell = obj is null ? Html.Encode(label) : $"""<a href="object.html?id={Uri.EscapeDataString(obj.Id)}">{Html.Encode(label)}</a>""";
            var hot = s.ExecutionCount >= hotThreshold ? """<span class="badge accent">hot</span>""" : "";
            sb.Append($"""
<tr class="filterable" data-search="{Html.Encode(label.ToLowerInvariant())}">
<td>{cell}</td><td>{s.ExecutionCount:N0}</td><td>{s.TotalLogicalReads:N0}</td><td>{s.TotalWorkerTimeMs:N0}</td><td>{hot}</td>
</tr>
""");
        }
        sb.Append("</tbody></table>");
    }

    private static void AppendHeatMap(StringBuilder sb, SiteContext ctx, RuntimeStats rt)
    {
        if (rt.ObjectStats.Count == 0) { return; }
        sb.Append("<h2>Heat-map</h2>");
        sb.Append("""<p class="note">Tile shade tracks execution volume (log-scaled). Darker = hotter.</p>""");
        var max = rt.ObjectStats.Max(s => s.ExecutionCount);
        sb.Append("""<div class="tiles">""");
        foreach (var s in rt.ObjectStats)
        {
            var obj = ctx.ById.GetValueOrDefault(s.ObjectId);
            var label = obj is null ? s.ObjectId : $"{obj.Schema}.{obj.Name}";
            var bucket = Bucket(s.ExecutionCount, max);
            // Bucket 0..4 -> alpha 0.15..0.85 over a fixed hue that reads on both themes.
            var alpha = (0.15 + bucket * 0.175).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            var style = $"background:rgba(220,80,40,{alpha});border-color:rgba(220,80,40,0.9)";
            var href = obj is null ? "#" : $"object.html?id={Uri.EscapeDataString(obj.Id)}";
            sb.Append($"""<a class="tile" href="{href}" style="{style}" title="{Html.Encode(label)}: {s.ExecutionCount:N0} executions">{Html.Encode(label)}<br><small>{s.ExecutionCount:N0}</small></a>""");
        }
        sb.Append("</div>");
    }

    private static void AppendIndexIssues(StringBuilder sb, SiteContext ctx, RuntimeStats rt)
    {
        sb.Append("<h2>Index issues</h2>");
        var unused = rt.IndexStats.Where(i => i.IsUnused).ToList();

        if (rt.MissingIndexes.Count > 0)
        {
            sb.Append("<h3>Missing indexes</h3>");
            sb.Append("""<table class="grid sortable" data-page-size="20"><thead><tr><th>Table</th><th>Equality cols</th><th>Inequality cols</th><th>Included</th><th>Impact</th><th></th></tr></thead><tbody>""");
            foreach (var m in rt.MissingIndexes)
            {
                var label = Label(ctx, m.ObjectId);
                var badge = m.ImpactScore >= 10000 ? """<span class="badge warn">high impact</span>""" : "";
                sb.Append($"""
<tr><td>{Html.Encode(label)}</td><td>{Html.Encode(m.EqualityColumns)}</td><td>{Html.Encode(m.InequalityColumns)}</td>
<td>{Html.Encode(m.IncludedColumns)}</td><td>{m.ImpactScore:N0}</td><td>{badge}</td></tr>
""");
            }
            sb.Append("</tbody></table>");
        }

        if (unused.Count > 0)
        {
            sb.Append("<h3>Unused indexes</h3>");
            sb.Append("""<table class="grid sortable" data-page-size="20"><thead><tr><th>Table</th><th>Index</th><th>Updates (writes)</th><th></th></tr></thead><tbody>""");
            foreach (var i in unused)
            {
                sb.Append($"""<tr><td>{Html.Encode(Label(ctx, i.ObjectId))}</td><td>{Html.Encode(i.IndexName)}</td><td>{i.UserUpdates:N0}</td><td><span class="badge warn">unused</span></td></tr>""");
            }
            sb.Append("</tbody></table>");
        }

        if (rt.MissingIndexes.Count == 0 && unused.Count == 0)
        {
            sb.Append("""<div class="panel empty-state"><p>No missing or unused indexes were reported.</p></div>""");
        }
        sb.Append("""<p class="note">Impact is the server's own benefit estimate; unused-index drops should be confirmed across a full workload cycle (e.g. month-end jobs) before acting.</p>""");
    }

    private static void AppendIssueConcentration(StringBuilder sb, SqlModel model, RuntimeStats rt)
    {
        sb.Append("<h2>Issue concentration</h2>");
        sb.Append("""<p class="note">Lint findings and index issues rolled up by schema, so trouble spots surface next to hot spots.</p>""");
        var perSchema = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        void Add(string objectId)
        {
            var schema = SchemaOf(objectId);
            perSchema[schema] = perSchema.GetValueOrDefault(schema) + 1;
        }
        foreach (var f in model.Findings) { if (f.ObjectId.Length > 0) { Add(f.ObjectId); } }
        foreach (var m in rt.MissingIndexes) { Add(m.ObjectId); }
        foreach (var i in rt.IndexStats.Where(x => x.IsUnused)) { Add(i.ObjectId); }

        if (perSchema.Count == 0)
        {
            sb.Append("""<div class="panel empty-state"><p>No lint findings or index issues to group.</p></div>""");
            return;
        }
        sb.Append("""<table class="grid"><tr><th>Schema</th><th>Issue count</th></tr>""");
        foreach (var kv in perSchema.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.Append($"""<tr><td>{Html.Encode(kv.Key)}</td><td>{kv.Value}</td></tr>""");
        }
        sb.Append("</table>");
    }

    private static string Label(SiteContext ctx, string objectId)
    {
        var obj = ctx.ById.GetValueOrDefault(objectId);
        return obj is null ? objectId : $"{obj.Schema}.{obj.Name}";
    }

    private static string SchemaOf(string objectId)
    {
        var dot = objectId.IndexOf('.');
        return dot > 0 ? objectId[..dot] : objectId;
    }

    /// <summary>Top-decile execution count; objects at or above it are flagged "hot".</summary>
    private static long HotThreshold(List<ObjectStat> stats)
    {
        var sorted = stats.Select(s => s.ExecutionCount).OrderByDescending(x => x).ToList();
        var idx = Math.Max(0, sorted.Count / 10 - 1);
        return sorted[idx];
    }

    /// <summary>Log-scaled bucket 0..4 of an execution count relative to the max.</summary>
    private static int Bucket(long value, long max)
    {
        if (max <= 0 || value <= 0) { return 0; }
        var ratio = Math.Log(value + 1) / Math.Log(max + 1);
        return Math.Clamp((int)(ratio * 5), 0, 4);
    }
}
