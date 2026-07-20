using System.Text;

namespace ArchSql.Site.Pages;

public static class IndexPage
{
    public static string Body(SiteContext ctx)
    {
        var model = ctx.Model;
        var sb = new StringBuilder();
        sb.Append("<h1>Overview</h1>");
        var sourceSentence = model.Runtime.Source == "live-mssql"
            ? "It was built from a read-only connection to a live SQL Server (schema from catalog views; runtime figures on the <a href=\"activity.html\">Activity</a> page from DMVs)."
            : "It was built by scanning the .sql files — no database was connected.";
        sb.Append($"""
<p class="lede">This site is a fully-offline architecture map of the SQL in {Html.Encode(model.RootName)}.
{sourceSentence} Tables, views, procedures and their foreign-key and dependency links are below,
alongside a lint report and a health scorecard. Everything works from file:// with no network.</p>
""");

        sb.Append("""
<div class="panel">
<p><strong>New to this database?</strong> Start with <a href="explore.html">Explore</a> to search
objects and ask the graph questions ("what does this table affect?"), or open any object to see its
<strong>neighborhood</strong> — what it touches and what touches it. <a href="crud.html">CRUD</a>
shows what writes each table; <a href="impact.html">Impact</a> shows what breaks if you change one.</p>
</div>
""");

        var tables = model.Objects.Count(o => o.Kind == "table");
        var views = model.Objects.Count(o => o.Kind == "view");
        var procs = model.Objects.Count(o => o.Kind is "procedure" or "function" or "trigger");
        var fks = model.ForeignKeys.Count;

        sb.Append("<div class=\"tiles\">");
        Tile(sb, tables.ToString("N0"), "Tables");
        Tile(sb, views.ToString("N0"), "Views");
        Tile(sb, procs.ToString("N0"), "Procedures / functions / triggers");
        Tile(sb, fks.ToString("N0"), "Foreign keys");
        Tile(sb, model.Findings.Count.ToString("N0"), "Lint findings");
        sb.Append("</div>");

        if (model.DialectLoc.Count > 0)
        {
            sb.Append("<h2>Dialect mix</h2><div class=\"lang-bar\">");
            var total = model.DialectLoc.Values.Sum();
            foreach (var (dialect, loc) in model.DialectLoc.OrderByDescending(kv => kv.Value))
            {
                var pct = total == 0 ? 0 : 100.0 * loc / total;
                sb.Append($"<span class=\"lang-dot\" style=\"width:{pct:F1}%\" title=\"{Html.Encode(dialect)}: {loc:N0} LOC\"></span>");
            }
            sb.Append("</div><div class=\"lang-legend\">");
            foreach (var (dialect, loc) in model.DialectLoc.OrderByDescending(kv => kv.Value))
            {
                sb.Append($"<span>{Html.Encode(dialect)} ({loc:N0} LOC)</span> ");
            }
            sb.Append("</div>");
        }

        var overallBadge = ctx.Scorecard.Overall switch
        {
            Analysis.SqlScorecard.Status.Ok => "badge ok",
            Analysis.SqlScorecard.Status.Watch => "badge warn",
            _ => "badge danger",
        };
        sb.Append($"<h2>Overall grade <span class=\"{overallBadge}\">{ctx.Scorecard.Overall}</span></h2>");
        sb.Append("<p class=\"note\"><a href=\"scorecard.html\">See the full scorecard</a> for every metric.</p>");

        if (model.Objects.Count(o => o.Kind == "table") == 0)
        {
            sb.Append("""<div class="panel empty-state"><div class="big">✓</div><p>No tables were found in this scan. Point ArchSql at a folder containing CREATE TABLE statements to see the architecture map.</p></div>""");
        }
        else
        {
            sb.Append("<h2>Foreign-key ER diagram</h2>");
            sb.Append(PageTemplate.DiagramBlock("er-preview", MermaidRenderer.BuildEr(model, 60)));
            sb.Append(PageTemplate.Legend());
            sb.Append("<p class=\"note\">See the <a href=\"er.html\">full ER Diagram</a> page for the complete view.</p>");
        }

        var degraded = model.Files.Count(f => !f.ParsedCleanly);
        if (degraded > 0)
        {
            sb.Append($"""<p class="note diag-list">{degraded} file(s) could not be fully parsed and were analyzed partially; see Diagnostics below. The rest of this site reflects the files that parsed.</p>""");
        }
        if (model.Diagnostics.Count > 0)
        {
            sb.Append("<h2>Diagnostics</h2><ul class=\"diag-list\">");
            foreach (var d in model.Diagnostics.Take(50)) { sb.Append($"<li>{Html.Encode(d)}</li>"); }
            sb.Append("</ul>");
        }

        return sb.ToString();
    }

    private static void Tile(StringBuilder sb, string num, string label) =>
        sb.Append($"""<div class="tile{(num == "0" ? " tile-zero" : "")}"><div class="num">{num}</div><div class="lbl">{Html.Encode(label)}</div></div>""");
}
