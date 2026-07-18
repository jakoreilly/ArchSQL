using System.Text;
using ArchSql.Model;

namespace ArchSql.Site.Pages;

/// <summary>One page per scanned .sql file: its objects, columns, findings, and Purpose text.</summary>
public static class ObjectFilePage
{
    public static string Body(SiteContext ctx, SqlFile file)
    {
        var sb = new StringBuilder();
        sb.Append($"<h1>{Html.Encode(file.RelPath)}</h1>");
        sb.Append($"""<p class="lede">{Html.Encode(file.Purpose)}</p>""");

        if (!file.ParsedCleanly)
        {
            sb.Append("""<p class="note diag-list">This file was analyzed with the lightweight fallback (or hit parse errors). Object and dependency detection here is best-effort.</p>""");
        }

        var objects = file.ObjectIds.Select(id => ctx.ById.GetValueOrDefault(id)).Where(o => o is not null).Select(o => o!).ToList();
        if (objects.Count == 0)
        {
            sb.Append("""<div class="panel empty-state"><div class="big">◇</div><p>No schema objects were parsed from this file.</p></div>""");
            return sb.ToString();
        }

        foreach (var o in objects)
        {
            sb.Append($"""<div class="panel type-card"><div class="type-head"><span class="type-name">{Html.Encode(o.Schema)}.{Html.Encode(o.Name)}</span> <span class="badge">{Html.Encode(o.Kind)}</span></div>""");
            if (o.Columns.Count > 0)
            {
                sb.Append("""<table class="grid"><tr><th>Column</th><th>Type</th><th>Nullable</th><th>PK</th></tr>""");
                foreach (var c in o.Columns)
                {
                    var pk = o.PrimaryKey.Contains(c.Name, StringComparer.OrdinalIgnoreCase) ? "✓" : "";
                    sb.Append($"<tr><td>{Html.Encode(c.Name)}</td><td>{Html.Encode(c.DataType)}</td><td>{(c.Nullable ? "Yes" : "No")}</td><td>{pk}</td></tr>");
                }
                sb.Append("</table>");
            }
            var findings = ctx.Model.Findings.Where(f => f.ObjectId == o.Id).ToList();
            if (findings.Count > 0)
            {
                sb.Append("<p><strong>Lint findings:</strong></p><ul>");
                foreach (var f in findings) { sb.Append($"<li><span class=\"badge warn\">{Html.Encode(f.RuleId)}</span> {Html.Encode(f.Message)}</li>"); }
                sb.Append("</ul>");
            }
            sb.Append("</div>");
        }

        return sb.ToString();
    }
}
