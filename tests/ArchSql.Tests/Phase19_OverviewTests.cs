using ArchSql.Model;
using ArchSql.Site;
using Xunit;

namespace ArchSql.Tests;

public class Phase19_OverviewTests
{
    [Fact]
    public void Body_HasNewcomerStartHerePanel()
    {
        var model = new SqlModel { RootName = "x", SourcePath = "x" };
        var body = Site.Pages.IndexPage.Body(SiteContext.Build(model));
        Assert.Contains("New to this database?", body);
        Assert.Contains("href=\"explore.html\"", body);
        Assert.Contains("href=\"crud.html\"", body);
        Assert.Contains("href=\"impact.html\"", body);
    }
}
