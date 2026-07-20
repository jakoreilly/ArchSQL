using ArchSql.Model;
using ArchSql.Site;
using Xunit;

namespace ArchSql.Tests;

public class Phase20_UiFixTests
{
    private static SqlModel Model() => new()
    {
        RootName = "ui",
        SourcePath = "ui",
        Objects = [new DbObject { Id = "dbo.a", Schema = "dbo", Name = "A", Kind = "table", Dialect = "tsql", DefinedInSlug = "a" }],
        Files = [new SqlFile { RelPath = "a", Slug = "a", Dialect = "tsql" }],
    };

    [Fact]
    public void ObjectsPage_SearchIsWiredToGenericFilterEngine()
    {
        var body = Site.Pages.ObjectsPage.Body(SiteContext.Build(Model()));
        // The generic filter engine in site.js requires BOTH a data-filter-target input and
        // .filterable rows carrying data-search — the previous markup had neither, so search was dead.
        Assert.Contains("data-filter-target=\"#objects-tbody\"", body);
        Assert.Contains("id=\"objects-tbody\"", body);
        Assert.Contains("class=\"filterable\"", body);
        Assert.Contains("data-search=\"dbo.a table\"", body);
    }

    [Fact]
    public void ActivityHeatMap_UsesDedicatedWrappingTileClass()
    {
        var model = Model() with
        {
            Runtime = new RuntimeStats
            {
                Source = "live-mssql",
                Available = true,
                Note = "n",
                ObjectStats = [new ObjectStat { ObjectId = "dbo.a", ExecutionCount = 10 }],
            },
        };
        var body = Site.Pages.ActivityPage.Body(SiteContext.Build(model));
        Assert.Contains("class=\"heat-grid\"", body);
        Assert.Contains("class=\"heat-tile\"", body);
        Assert.Contains("heat-name", body);
    }

    [Fact]
    public void SiteCss_DefinesHeatTileWrappingRules()
    {
        var css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "ArchSql", "Site", "assets", "site.css"));
        Assert.Contains(".heat-tile", css);
        Assert.Contains("overflow-wrap: anywhere", css);
    }

    [Fact]
    public void SiteJs_NeighborhoodEmitsAdjacencyForHoverHighlight()
    {
        var js = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "ArchSql", "Site", "assets", "site.js"));
        Assert.Contains("script.adjacency", js);
        Assert.Contains("adjEl.textContent = JSON.stringify(adjacency)", js);
    }
}
