using ArchSql.Analysis;
using ArchSql.Model;
using ArchSql.Site;
using Xunit;

namespace ArchSql.Tests;

public class Phase29_RelationshipsPageTests
{
    private static DbObject Table(string name, params Column[] columns) => new()
    {
        Id = IdentifierRules.NormalizeId("dbo", name, "tsql"),
        Schema = "dbo", Name = name, Kind = "table", Dialect = "tsql",
        Columns = columns.ToList(),
    };

    [Fact]
    public void Body_ShowsEmptyStateWhenNothingInferred()
    {
        var model = new SqlModel { RootName = "x", SourcePath = "x", Objects = [Table("Solo", new Column { Name = "Value", DataType = "int" })] };
        var body = Site.Pages.RelationshipsPage.Body(SiteContext.Build(model), 60);
        Assert.Contains("No relationships could be inferred", body);
    }

    [Fact]
    public void Body_ListsInferredRelationshipWithConfidenceBadge()
    {
        var model = new SqlModel
        {
            RootName = "x", SourcePath = "x",
            Objects =
            [
                Table("Customer", new Column { Name = "Id", DataType = "int" }),
                Table("Order", new Column { Name = "CustomerId", DataType = "int" }),
            ],
        };
        var body = Site.Pages.RelationshipsPage.Body(SiteContext.Build(model), 60);
        Assert.Contains("erDiagram", body);
        Assert.Contains("badge ok\">high", body);
        Assert.Contains("CustomerId", body);
    }
}
