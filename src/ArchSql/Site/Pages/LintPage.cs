using System.Text;

namespace ArchSql.Site.Pages;

public static class LintPage
{
    private static readonly (string Label, string Cls)[] SeverityBand =
    [
        ("Critical", "badge warn"),
        ("High", "badge warn"),
        ("Medium", "badge"),
        ("Low", "badge ok"),
    ];

    public static string Body(SiteContext ctx)
    {
        var model = ctx.Model;
        var sb = new StringBuilder();
        sb.Append("<h1>Lint</h1>");
        sb.Append("""<p class="lede">SonarQube-style findings across security, correctness, performance and maintainability rules.</p>""");

        if (model.Findings.Count == 0)
        {
            sb.Append("""<div class="panel empty-state"><div class="big">✓</div><p>No issues found. Every rule passed on the objects ArchSql could parse — scroll to the Scorecard for the overall grade.</p></div>""");
            return sb.ToString();
        }

        sb.Append("<div class=\"tiles\">");
        for (var sev = 0; sev < 4; sev++)
        {
            var count = model.Findings.Count(f => f.Severity == sev);
            var (label, _) = SeverityBand[sev];
            sb.Append($"""<div class="tile{(count == 0 ? " tile-zero" : "")}"><div class="num">{count}</div><div class="lbl">{label}</div></div>""");
        }
        sb.Append("</div>");

        sb.Append("""<table class="grid"><tr><th>Severity</th><th>Rule</th><th>Object</th><th>Message</th></tr>""");
        foreach (var f in model.Findings.OrderBy(f => f.Severity).ThenBy(f => f.RuleId, StringComparer.Ordinal))
        {
            var (label, cls) = SeverityBand[f.Severity];
            var obj = ctx.ById.GetValueOrDefault(f.ObjectId);
            var link = obj is not null ? $"""<a href="object.html?id={Uri.EscapeDataString(obj.Id)}">{Html.Encode(obj.Schema)}.{Html.Encode(obj.Name)}</a>"""
                : ctx.BySlug.TryGetValue(f.Slug, out var file) ? $"""<a href="files/{f.Slug}.html">{Html.Encode(file.RelPath)}</a>""" : "";
            sb.Append($"""
<tr><td><span class="{cls}">{label}</span></td><td>{Html.Encode(f.RuleId)} — {Html.Encode(f.Title)}</td><td>{link}</td><td>{Html.Encode(f.Message)}</td></tr>
""");
        }
        sb.Append("</table>");
        return sb.ToString();
    }
}
