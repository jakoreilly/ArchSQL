using System.Text;
using ArchSql.Analysis;

namespace ArchSql.Site.Pages;

/// <summary>Likely table relationships inferred from column-naming patterns, for databases where
/// referential integrity is not declared as foreign keys — a complement to the FK-based ER diagram,
/// not a replacement. Every relationship here is a guess, clearly labelled as such.</summary>
public static class RelationshipsPage
{
    public static string Body(SiteContext ctx, int maxNodes)
    {
        var model = ctx.Model;
        var sb = new StringBuilder();
        sb.Append("<h1>Relationships</h1>");
        sb.Append("""
<p class="lede">Likely relationships between tables, inferred from column-naming patterns rather than
declared foreign keys. Useful when referential integrity lives in application code instead of the
schema. Every row here is a guess — an inferred relationship is only shown when its column stem
matches exactly one table, so it should be read as a lead to confirm, not a fact.</p>
""");

        var relationships = InferredRelationships.Compute(model);
        if (relationships.Count == 0)
        {
            sb.Append("""
<div class="panel empty-state"><div class="big">◇</div>
<p>No relationships could be inferred from column naming. This is expected when referential
integrity is already declared as foreign keys, or when column names don't follow an identifiable
"&lt;entity&gt;Id"-style pattern.</p>
</div>
""");
            return sb.ToString();
        }

        sb.Append(PageTemplate.DiagramBlock("inferred-er", MermaidRenderer.BuildInferredEr(model, relationships, maxNodes)));
        sb.Append("""<p class="note">Dashed lines are inferred, not declared — see the ER Diagram page for real foreign keys.</p>""");

        sb.Append("""<input class="filter-input" type="search" data-filter-target="#rel-rows" placeholder="Filter by table or column…" autocomplete="off" spellcheck="false"> <span class="filter-count"></span>""");
        sb.Append("""<table class="grid sortable" data-page-size="30"><thead><tr><th>From table</th><th>Column</th><th>Likely references</th><th>Confidence</th></tr></thead><tbody id="rel-rows">""");
        foreach (var r in relationships)
        {
            var from = ctx.ById.GetValueOrDefault(r.FromObjectId);
            var to = ctx.ById.GetValueOrDefault(r.ToObjectId);
            var fromLabel = from is null ? r.FromObjectId : $"{from.Schema}.{from.Name}";
            var toLabel = to is null ? r.ToObjectId : $"{to.Schema}.{to.Name}";
            var badge = r.Confidence == "high" ? """<span class="badge ok">high</span>""" : """<span class="badge">medium</span>""";
            var search = $"{fromLabel} {r.FromColumn} {toLabel}".ToLowerInvariant();
            sb.Append($"""
<tr class="filterable" data-search="{Html.Encode(search)}">
<td><a href="object.html?id={Uri.EscapeDataString(r.FromObjectId)}">{Html.Encode(fromLabel)}</a></td>
<td>{Html.Encode(r.FromColumn)}</td>
<td><a href="object.html?id={Uri.EscapeDataString(r.ToObjectId)}">{Html.Encode(toLabel)}</a></td>
<td>{badge}</td>
</tr>
""");
        }
        sb.Append("</tbody></table>");
        return sb.ToString();
    }
}
