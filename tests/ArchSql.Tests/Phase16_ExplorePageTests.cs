using Xunit;

namespace ArchSql.Tests;

public class Phase16_ExplorePageTests
{
    [Fact]
    public void Body_ContainsQueryConsoleAndExampleChips()
    {
        var body = Site.Pages.ExplorePage.Body();
        Assert.Contains("id=\"query-console\"", body);
        Assert.Contains("id=\"query-input\"", body);
        Assert.Contains("id=\"query-results\"", body);
        Assert.Contains("id=\"query-count\"", body);
        Assert.Contains("referencedby: Orders", body);
        Assert.Contains("query-example", body);
    }

    [Fact]
    public void SiteJs_DefinesSqlFlavouredVerbAliases()
    {
        var js = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "ArchSql", "Site", "assets", "site.js"));
        Assert.Contains("VERB_ALIASES", js);
        Assert.Contains("referencedby:", js);
        Assert.Contains("affects:", js);
    }
}
