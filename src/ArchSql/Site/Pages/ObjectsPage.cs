using System.Text;
using ArchSql.Analysis;

namespace ArchSql.Site.Pages;

public static class ObjectsPage
{
    public static string Body(SiteContext ctx)
    {
        var model = ctx.Model;
        var sb = new StringBuilder();
        sb.Append("<h1>Objects</h1>");
        sb.Append("""<p class="lede">Every table, view, procedure, function and trigger found in this scan.</p>""");

        if (model.Objects.Count == 0)
        {
            sb.Append("""<div class="panel empty-state"><div class="big">◇</div><p>No schema objects were found. Point ArchSql at a folder containing CREATE TABLE/VIEW/PROCEDURE statements.</p></div>""");
            return sb.ToString();
        }

        sb.Append("""<input class="filter-input" type="search" data-filter-target="#objects-tbody" placeholder="Filter by schema, name or kind…" autocomplete="off" spellcheck="false"> <span class="filter-count"></span>""");
        sb.Append("""<table class="grid sortable" id="objects-table"><thead><tr><th>Schema</th><th>Name</th><th>Kind</th><th>PK?</th><th>Columns</th><th>Fan-in</th><th>Fan-out</th><th>Purpose</th><th>File</th></tr></thead><tbody id="objects-tbody">""");
        foreach (var o in model.Objects.OrderBy(o => o.Schema, StringComparer.OrdinalIgnoreCase).ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase))
        {
            var pkBadge = o.Kind != "table" ? "" : o.PrimaryKey.Count > 0 ? """<span class="badge ok">Yes</span>""" : """<span class="badge warn">No</span>""";
            var file = ctx.BySlug.GetValueOrDefault(o.DefinedInSlug);
            var shallow = file is { ParsedCleanly: false } ? """ <span class="badge" title="Shallow parse">shallow</span>""" : "";
            var search = $"{o.Schema}.{o.Name} {o.Kind}".ToLowerInvariant();
            sb.Append($"""
<tr class="filterable" data-search="{Html.Encode(search)}">
<td>{Html.Encode(o.Schema)}</td>
<td><a href="object.html?id={Uri.EscapeDataString(o.Id)}">{Html.Encode(o.Name)}</a>{shallow}</td>
<td>{Html.Encode(o.Kind)}</td>
<td>{pkBadge}</td>
<td>{o.Columns.Count}</td>
<td>{ctx.FanIn.GetValueOrDefault(o.Id)}</td>
<td>{ctx.FanOut.GetValueOrDefault(o.Id)}</td>
<td>{Html.Encode(SqlPurpose.ForObject(o))}</td>
<td><a href="files/{o.DefinedInSlug}.html">{Html.Encode(file?.RelPath ?? "")}</a></td>
</tr>
""");
        }
        sb.Append("</tbody></table>");
        return sb.ToString();
    }
}
