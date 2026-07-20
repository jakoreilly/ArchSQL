using ArchSql.Model;
using ArchSql.Site;
using Xunit;

namespace ArchSql.Tests;

public class Phase18_ActivityPolishTests
{
    private static SqlModel ModelWithRuntime()
    {
        var stats = Enumerable.Range(0, 25)
            .Select(i => new ObjectStat { ObjectId = $"dbo.p{i}", ExecutionCount = 100 - i, TotalWorkerTimeMs = i, TotalLogicalReads = i })
            .ToList();
        return new SqlModel
        {
            RootName = "activity",
            SourcePath = "activity",
            Objects = Enumerable.Range(0, 25).Select(i => new DbObject { Id = $"dbo.p{i}", Schema = "dbo", Name = $"p{i}", Kind = "procedure", Dialect = "tsql" }).ToList(),
            Runtime = new RuntimeStats { Source = "live-mssql", Available = true, Note = "test", ObjectStats = stats },
        };
    }

    [Fact]
    public void Body_HotspotsTableIsSortableAndPaginated()
    {
        var body = Site.Pages.ActivityPage.Body(SiteContext.Build(ModelWithRuntime()));
        Assert.Contains("class=\"grid sortable\"", body);
        Assert.Contains("data-page-size=\"20\"", body);
        Assert.Contains("<thead>", body);
    }

    [Fact]
    public void Body_HasSummaryStatTiles()
    {
        var body = Site.Pages.ActivityPage.Body(SiteContext.Build(ModelWithRuntime()));
        Assert.Contains("Hot objects", body);
        Assert.Contains("Missing indexes", body);
        Assert.Contains("Unused indexes", body);
    }

    [Fact]
    public void SiteJs_HasSortableTableEngine()
    {
        var js = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "ArchSql", "Site", "assets", "site.js"));
        Assert.Contains("table.sortable", js);
        Assert.Contains("table-more", js);
    }
}
