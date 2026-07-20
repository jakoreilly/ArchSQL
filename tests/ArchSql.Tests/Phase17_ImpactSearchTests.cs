using ArchSql.Model;
using ArchSql.Site;
using Xunit;

namespace ArchSql.Tests;

public class Phase17_ImpactSearchTests
{
    private static SqlModel TwoObjectModel() => new()
    {
        RootName = "impact",
        SourcePath = "impact",
        Objects =
        [
            new DbObject { Id = "dbo.a", Schema = "dbo", Name = "A", Kind = "table", Dialect = "tsql", DefinedInSlug = "a" },
            new DbObject { Id = "dbo.b", Schema = "dbo", Name = "B", Kind = "procedure", Dialect = "tsql", DefinedInSlug = "b" },
        ],
        Dependencies = [new ObjectDep { FromObjectId = "dbo.b", ToObjectId = "dbo.a", Kind = "read" }],
    };

    [Fact]
    public void Body_HasSearchInputNotAFullSelectOfAllObjects()
    {
        var body = Site.Pages.ImpactPage.Body(SiteContext.Build(TwoObjectModel()));
        Assert.Contains("id=\"impact-search\"", body);
        Assert.Contains("id=\"impact-search-results\"", body);
        // The old full <select> of every object must be gone.
        Assert.DoesNotContain("<select id=\"impact-object\"", body);
    }

    [Fact]
    public void Body_ReadsIdQueryParamForPreselection()
    {
        var body = Site.Pages.ImpactPage.Body(SiteContext.Build(TwoObjectModel()));
        Assert.Contains("window.location.search", body);
        Assert.Contains("id=", body);
    }
}
