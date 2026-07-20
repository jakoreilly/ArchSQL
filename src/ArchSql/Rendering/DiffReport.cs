using System.Text;
using ArchSql.Analysis;
using ArchSql.Site;

namespace ArchSql.Rendering;

/// <summary>Renders a SchemaDiff result as a Markdown report (for PR comments) and a themed HTML
/// page (reusing PageTemplate/site.css so it's legible in both themes without new CSS).</summary>
public static class DiffReport
{
    public static string Markdown(IReadOnlyList<SchemaChange> changes, IReadOnlySet<string> suppressed)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Schema diff");
        sb.AppendLine();
        if (changes.Count == 0) { sb.AppendLine("No schema changes detected."); return sb.ToString(); }

        foreach (var risk in new[] { ChangeRisk.Breaking, ChangeRisk.Degrading, ChangeRisk.Safe })
        {
            var group = changes.Where(c => c.Risk == risk).ToList();
            if (group.Count == 0) { continue; }
            sb.AppendLine($"## {risk}");
            sb.AppendLine();
            foreach (var c in group)
            {
                var isSuppressed = suppressed.Contains(DiffBaseline.Key(c));
                var line = $"- `{c.Kind}` **{c.Target}** — {c.Detail}";
                sb.AppendLine(isSuppressed ? $"- ~~`{c.Kind}` **{c.Target}** — {c.Detail}~~ (baselined)" : line);
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string RenderHtml(IReadOnlyList<SchemaChange> changes, IReadOnlySet<string> suppressed)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Schema Diff</h1>");
        sb.Append("""
<p class="lede">What changed between two ArchSql scans, classified by risk. Breaking = drops,
narrowing type changes, NULL to NOT NULL, or new NOT NULL columns. Degrading = dropped indexes/FKs.
Safe = additive. Baselined changes are shown struck-through and don't fail the gate.</p>
""");
        if (changes.Count == 0)
        {
            sb.Append("""<div class="panel empty-state"><div class="big">✓</div><p>No schema changes detected between these two scans.</p></div>""");
        }
        else
        {
            sb.Append("""<table class="grid"><tr><th>Risk</th><th>Kind</th><th>Target</th><th>Detail</th></tr>""");
            foreach (var c in changes)
            {
                var isSuppressed = suppressed.Contains(DiffBaseline.Key(c));
                var badge = c.Risk switch
                {
                    ChangeRisk.Breaking => "badge warn",
                    ChangeRisk.Degrading => "badge",
                    _ => "badge ok",
                };
                var rowStyle = isSuppressed ? "text-decoration:line-through;color:var(--text-soft)" : "";
                sb.Append($"""
<tr style="{rowStyle}">
<td><span class="{badge}">{Html.Encode(c.Risk.ToString())}</span></td>
<td>{Html.Encode(c.Kind)}</td>
<td>{Html.Encode(c.Target)}</td>
<td>{Html.Encode(c.Detail)}{(isSuppressed ? " (baselined)" : "")}</td>
</tr>
""");
            }
            sb.Append("</table>");
        }
        return PageTemplate.Render("Schema Diff", "ArchSql", "", "", PageTemplate.Crumbs((null, "Schema Diff")), sb.ToString());
    }
}
