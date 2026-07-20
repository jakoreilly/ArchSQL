using System.Text;

namespace ArchSql.Site.Pages;

public static class MetricsPage
{
    public static string Body(SiteContext ctx)
    {
        var model = ctx.Model;
        var sb = new StringBuilder();
        sb.Append("<h1>Metrics</h1>");
        sb.Append("""<p class="lede">Coupling (fan-in/fan-out) and procedure complexity across the scanned schema.</p>""");

        sb.Append("<div class=\"two-col\">");
        RankTable(sb, "Most depended-on (fan-in)", "Objects many other objects reference. Changes here ripple widest.", ctx.FanIn, ctx);
        RankTable(sb, "Most dependencies (fan-out)", "Objects that reference the most other objects.", ctx.FanOut, ctx);
        sb.Append("</div>");

        var procs = model.Objects.Where(o => o.Kind is "procedure" or "function" or "trigger").OrderByDescending(o => o.Cyclomatic).Take(20).ToList();
        if (procs.Count > 0)
        {
            sb.Append("<h2>Most complex procedures / functions / triggers</h2>");
            sb.Append("""<table class="grid"><tr><th>Object</th><th>Cyclomatic</th></tr>""");
            foreach (var o in procs)
            {
                sb.Append($"""<tr><td><a href="object.html?id={Uri.EscapeDataString(o.Id)}">{Html.Encode(o.Schema)}.{Html.Encode(o.Name)}</a></td><td>{o.Cyclomatic}</td></tr>""");
            }
            sb.Append("</table>");
        }

        return sb.ToString();
    }

    private static void RankTable(StringBuilder sb, string title, string blurb, Dictionary<string, int> counts, SiteContext ctx)
    {
        sb.Append($"<div><h2>{Html.Encode(title)}</h2><p class=\"note\">{Html.Encode(blurb)}</p>");
        sb.Append("""<table class="grid"><tr><th>Object</th><th>Count</th></tr>""");
        foreach (var (id, count) in counts.OrderByDescending(kv => kv.Value).Take(15))
        {
            if (!ctx.ById.TryGetValue(id, out var o)) { continue; }
            sb.Append($"""<tr><td><a href="object.html?id={Uri.EscapeDataString(o.Id)}">{Html.Encode(o.Schema)}.{Html.Encode(o.Name)}</a></td><td>{count}</td></tr>""");
        }
        sb.Append("</table></div>");
    }
}
