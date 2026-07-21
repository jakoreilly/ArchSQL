using ArchSql.Analysis;
using ArchSql.Model;
using ArchSql.Site;
using Xunit;

namespace ArchSql.Tests;

public class Phase26_IndexRulesAndPageTests
{
    private static DbObject Table(string schema, string name, params Column[] columns) => new()
    {
        Id = IdentifierRules.NormalizeId(schema, name, "tsql"),
        Schema = schema, Name = name, Kind = "table", Dialect = "tsql",
        Columns = columns.ToList(),
    };

    [Fact]
    public void SQL0013_FlagsHeapTable()
    {
        var table = Table("dbo", "T", new Column { Name = "Id", DataType = "int" }) with
        {
            IndexDetails = [new IndexDef { Name = "IX", ObjectId = "dbo.t", KeyColumns = ["Id"], IsClustered = false }],
        };
        var model = new SqlModel { RootName = "x", SourcePath = "x", Objects = [table] };
        Assert.Contains(SqlRules.Run(model), f => f.RuleId == "SQL0013");
    }

    [Fact]
    public void SQL0014_FlagsDuplicateIndexes()
    {
        var table = Table("dbo", "T", new Column { Name = "A", DataType = "int" }) with
        {
            IndexDetails =
            [
                new IndexDef { Name = "IX_A", ObjectId = "dbo.t", KeyColumns = ["A"] },
                new IndexDef { Name = "IX_B", ObjectId = "dbo.t", KeyColumns = ["A"] },
            ],
        };
        var model = new SqlModel { RootName = "x", SourcePath = "x", Objects = [table] };
        Assert.Contains(SqlRules.Run(model), f => f.RuleId == "SQL0014");
    }

    [Fact]
    public void SQL0015_FlagsUnboundedMaxColumn()
    {
        var table = Table("dbo", "T", new Column { Name = "Notes", DataType = "nvarchar" }) with { };
        table = table with { Columns = [table.Columns[0] with { MaxLength = -1 }] };
        var model = new SqlModel { RootName = "x", SourcePath = "x", Objects = [table] };
        Assert.Contains(SqlRules.Run(model), f => f.RuleId == "SQL0015");
    }

    [Fact]
    public void SQL0015_DoesNotFlagOrdinaryWidthColumn()
    {
        var table = Table("dbo", "T", new Column { Name = "Name", DataType = "nvarchar", MaxLength = 100 });
        var model = new SqlModel { RootName = "x", SourcePath = "x", Objects = [table] };
        Assert.DoesNotContain(SqlRules.Run(model), f => f.RuleId == "SQL0015");
    }

    [Fact]
    public void SQL0016_FlagsUnusedIndexOnlyWithRuntimeData()
    {
        var table = Table("dbo", "T", new Column { Name = "A", DataType = "int" }) with
        {
            IndexDetails = [new IndexDef { Name = "IX_Dead", ObjectId = "dbo.t", KeyColumns = ["A"] }],
        };
        var model = new SqlModel
        {
            RootName = "x", SourcePath = "x", Objects = [table],
            Runtime = new RuntimeStats { Source = "live-mssql", Available = true, IndexStats = [new IndexStat { ObjectId = "dbo.t", IndexName = "IX_Dead", UserUpdates = 3, IsUnused = true }] },
        };
        Assert.Contains(SqlRules.Run(model), f => f.RuleId == "SQL0016");
    }

    [Fact]
    public void IndexesPage_ShowsEmptyStateWithoutIndexDetail()
    {
        var model = new SqlModel { RootName = "x", SourcePath = "x", Objects = [Table("dbo", "T", new Column { Name = "Id", DataType = "int" })] };
        var body = Site.Pages.IndexesPage.Body(SiteContext.Build(model));
        Assert.Contains("No index catalog detail is available", body);
    }

    [Fact]
    public void IndexesPage_ListsHeapsAndLargestTablesWhenDataPresent()
    {
        var table = Table("dbo", "T", new Column { Name = "Id", DataType = "int" }) with
        {
            IndexDetails = [new IndexDef { Name = "IX", ObjectId = "dbo.t", KeyColumns = ["Id"], IsClustered = false }],
            RowCount = 5000,
        };
        var model = new SqlModel { RootName = "x", SourcePath = "x", Objects = [table] };
        var body = Site.Pages.IndexesPage.Body(SiteContext.Build(model));
        Assert.Contains("Heap tables", body);
        Assert.Contains("5,000", body);
    }
}
