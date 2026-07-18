namespace ArchSql.Site.Pages;

public static class DependenciesPage
{
    public static string Body(SiteContext ctx, int maxNodes)
    {
        var hasDeps = ctx.Model.Dependencies.Any(d => d.ToObjectId.Length > 0);
        if (!hasDeps)
        {
            return """
<h1>Dependencies</h1>
<div class="panel empty-state"><div class="big">◇</div>
<p>No resolved object-to-object dependencies were found. Views, procedures and triggers that
reference tables or other objects will show up here once the scan can resolve those references.</p>
</div>
""";
        }

        return $"""
<h1>Dependencies</h1>
<p class="lede">Which objects reference which — procedures calling other procedures, views
selecting from tables, foreign keys between tables. High fan-in objects are risky to change;
high fan-out objects know too much.</p>
{PageTemplate.DiagramBlock("deps-diagram", MermaidRenderer.BuildDependencies(ctx.Model, maxNodes))}
{PageTemplate.Legend()}
""";
    }
}
