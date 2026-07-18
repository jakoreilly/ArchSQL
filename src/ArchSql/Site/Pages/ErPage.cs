namespace ArchSql.Site.Pages;

public static class ErPage
{
    public static string Body(SiteContext ctx, int maxNodes)
    {
        var tablesWithFk = ctx.Model.Objects.Count(o => o.Kind == "table");
        if (ctx.Model.ForeignKeys.Count == 0 || tablesWithFk == 0)
        {
            return """
<h1>ER Diagram</h1>
<div class="panel empty-state"><div class="big">◇</div>
<p>No tables with foreign keys were found in this scan. Add schema files containing
CREATE TABLE … FOREIGN KEY statements, or check the Diagnostics on the Overview if parsing failed.</p>
</div>
""";
        }

        return $"""
<h1>ER Diagram</h1>
<p class="lede">Every table and its foreign-key relationships to other tables in this scan.</p>
{PageTemplate.DiagramBlock("er-diagram", MermaidRenderer.BuildEr(ctx.Model, maxNodes))}
{PageTemplate.Legend()}
""";
    }
}
