using System.Text;
using ArchSql.Analysis;

namespace ArchSql.Site.Pages;

public static class ScorecardPage
{
    public static string Body(SiteContext ctx)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Scorecard</h1>");
        sb.Append("""<p class="lede">A worst-wins health grade: the overall grade is the worst status among every metric below (a metric with no data doesn't worsen the grade).</p>""");

        sb.Append($"<h2>Overall: {Badge(ctx.Scorecard.Overall)}</h2>");
        sb.Append("""<table class="grid"><tr><th>Metric</th><th>Value</th><th>Status</th><th>Note</th><th>Action</th></tr>""");
        foreach (var row in ctx.Scorecard.Rows)
        {
            var link = row.Link.Length > 0 ? $"""<a href="{row.Link}">{Html.Encode(row.Metric)}</a>""" : Html.Encode(row.Metric);
            sb.Append($"""
<tr><td>{link}</td><td>{Html.Encode(row.Value)}</td><td>{Badge(row.Status)}</td><td>{Html.Encode(row.Note)}</td><td>{Html.Encode(row.Action)}</td></tr>
""");
        }
        sb.Append("</table>");
        return sb.ToString();
    }

    private static string Badge(SqlScorecard.Status status) => status switch
    {
        SqlScorecard.Status.Ok => """<span class="badge ok">Ok</span>""",
        SqlScorecard.Status.Watch => """<span class="badge warn">Watch</span>""",
        SqlScorecard.Status.Fail => """<span class="badge danger">Fail</span>""",
        _ => """<span class="badge">N/A</span>""",
    };
}
