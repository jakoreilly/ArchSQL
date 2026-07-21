using System.Text;
using ArchSql.Analysis;

namespace ArchSql.Site.Pages;

/// <summary>Static index-health report: heaps, duplicate/overlapping indexes, unused indexes (with
/// runtime evidence and a ready-to-review drop statement), and the largest tables by row count.
/// Empty unless the model carries index/catalog detail — a file scan or a permission-limited login
/// leaves the relevant sections showing their empty state.</summary>
public static class IndexesPage
{
    public static string Body(SiteContext ctx)
    {
        var model = ctx.Model;
        var sb = new StringBuilder();
        sb.Append("<h1>Indexes</h1>");
        sb.Append("""
<p class="lede">Index health from the connected server's catalog: tables with no clustered index
(heaps), indexes whose key columns duplicate or overlap another index on the same table, and indexes
that have never been read according to the server's runtime counters.</p>
""");

        var hasIndexDetail = model.Objects.Any(o => o.IndexDetails.Count > 0);
        if (!hasIndexDetail)
        {
            sb.Append("""
<div class="panel empty-state"><div class="big">🗄</div>
<p>No index catalog detail is available. This appears on a live connection with catalog read access;
a file scan or a login without the needed permission leaves this page empty.</p>
</div>
""");
            return sb.ToString();
        }

        AppendHeaps(sb, ctx);
        AppendDuplicates(sb, ctx);
        AppendUnused(sb, ctx);
        AppendLargestTables(sb, ctx);
        return sb.ToString();
    }

    private static void AppendHeaps(StringBuilder sb, SiteContext ctx)
    {
        sb.Append("<h2>Heap tables (no clustered index)</h2>");
        var heaps = IndexAnalysis.Heaps(ctx.Model);
        if (heaps.Count == 0)
        {
            sb.Append("""<div class="panel empty-state"><p>No heap tables found — every table with a recorded index has a clustered one.</p></div>""");
            return;
        }
        sb.Append("""<table class="grid sortable" data-page-size="20"><thead><tr><th>Table</th><th>Columns</th><th>Row count</th></tr></thead><tbody>""");
        foreach (var o in heaps.OrderByDescending(o => o.RowCount))
        {
            sb.Append($"""<tr><td><a href="object.html?id={Uri.EscapeDataString(o.Id)}">{Html.Encode(o.Schema)}.{Html.Encode(o.Name)}</a></td><td>{o.Columns.Count}</td><td>{o.RowCount:N0}</td></tr>""");
        }
        sb.Append("</tbody></table>");
    }

    private static void AppendDuplicates(StringBuilder sb, SiteContext ctx)
    {
        sb.Append("<h2>Duplicate / overlapping indexes</h2>");
        var pairs = IndexAnalysis.DuplicateIndexes(ctx.Model);
        if (pairs.Count == 0)
        {
            sb.Append("""<div class="panel empty-state"><p>No duplicate or overlapping indexes found.</p></div>""");
            return;
        }
        sb.Append("""<table class="grid sortable" data-page-size="20"><thead><tr><th>Table</th><th>Index A</th><th>Index B</th><th>Relationship</th></tr></thead><tbody>""");
        foreach (var p in pairs)
        {
            var obj = ctx.ById.GetValueOrDefault(p.ObjectId);
            var label = obj is null ? p.ObjectId : $"{obj.Schema}.{obj.Name}";
            sb.Append($"""<tr><td><a href="object.html?id={Uri.EscapeDataString(p.ObjectId)}">{Html.Encode(label)}</a></td><td>{Html.Encode(p.IndexA)}</td><td>{Html.Encode(p.IndexB)}</td><td>{Html.Encode(p.Relationship)}</td></tr>""");
        }
        sb.Append("</tbody></table>");
    }

    private static void AppendUnused(StringBuilder sb, SiteContext ctx)
    {
        sb.Append("<h2>Unused indexes</h2>");
        if (!ctx.Model.Runtime.Available)
        {
            sb.Append("""<div class="panel empty-state"><p>Unused-index detection needs runtime data from a live connection with the required permission.</p></div>""");
            return;
        }
        var unused = IndexAnalysis.UnusedIndexes(ctx.Model);
        if (unused.Count == 0)
        {
            sb.Append("""<div class="panel empty-state"><p>No unused indexes found.</p></div>""");
            return;
        }
        sb.Append("""<p class="note">Confirm across a full workload cycle before dropping — an index unread since the last statistics reset may still serve periodic or seasonal queries.</p>""");
        sb.Append("""<table class="grid sortable" data-page-size="20"><thead><tr><th>Table</th><th>Index</th><th>Writes recorded</th><th>Drop statement</th></tr></thead><tbody>""");
        foreach (var u in unused)
        {
            var obj = ctx.ById.GetValueOrDefault(u.ObjectId);
            var label = obj is null ? u.ObjectId : $"{obj.Schema}.{obj.Name}";
            sb.Append($"""<tr><td><a href="object.html?id={Uri.EscapeDataString(u.ObjectId)}">{Html.Encode(label)}</a></td><td>{Html.Encode(u.IndexName)}</td><td>{u.UserUpdates:N0}</td><td><code>{Html.Encode(u.DropStatement)}</code></td></tr>""");
        }
        sb.Append("</tbody></table>");
    }

    private static void AppendLargestTables(StringBuilder sb, SiteContext ctx)
    {
        sb.Append("<h2>Largest tables</h2>");
        var withStats = ctx.Model.Objects.Where(o => o.Kind == "table" && o.RowCount > 0).OrderByDescending(o => o.RowCount).Take(25).ToList();
        if (withStats.Count == 0)
        {
            sb.Append("""<div class="panel empty-state"><p>No row-count data is available — this needs a live connection with permission to read partition statistics.</p></div>""");
            return;
        }
        sb.Append("""<table class="grid sortable" data-page-size="25"><thead><tr><th>Table</th><th>Row count</th><th>Reserved (KB)</th></tr></thead><tbody>""");
        foreach (var o in withStats)
        {
            sb.Append($"""<tr><td><a href="object.html?id={Uri.EscapeDataString(o.Id)}">{Html.Encode(o.Schema)}.{Html.Encode(o.Name)}</a></td><td>{o.RowCount:N0}</td><td>{o.ReservedKb:N0}</td></tr>""");
        }
        sb.Append("</tbody></table>");
    }
}
