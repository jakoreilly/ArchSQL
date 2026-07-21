using ArchSql.Analysis;
using ArchSql.Model;
using Xunit;

namespace ArchSql.Tests;

public class Phase28_InferredRelationshipsTests
{
    private static DbObject Table(string name, params Column[] columns) => new()
    {
        Id = IdentifierRules.NormalizeId("dbo", name, "tsql"),
        Schema = "dbo", Name = name, Kind = "table", Dialect = "tsql",
        Columns = columns.ToList(),
    };

    [Fact]
    public void Compute_MatchesColumnStemToTableNameExactly()
    {
        var model = new SqlModel
        {
            RootName = "x", SourcePath = "x",
            Objects =
            [
                Table("Customer", new Column { Name = "Id", DataType = "int" }),
                Table("Order", new Column { Name = "Id", DataType = "int" }, new Column { Name = "CustomerId", DataType = "int" }),
            ],
        };
        var rel = Assert.Single(InferredRelationships.Compute(model));
        Assert.Equal("dbo.order", rel.FromObjectId);
        Assert.Equal("CustomerId", rel.FromColumn);
        Assert.Equal("dbo.customer", rel.ToObjectId);
        Assert.Equal("high", rel.Confidence);
    }

    [Fact]
    public void Compute_MatchesPluralTableNameAtMediumConfidence()
    {
        var model = new SqlModel
        {
            RootName = "x", SourcePath = "x",
            Objects =
            [
                Table("Orders", new Column { Name = "Id", DataType = "int" }),
                Table("OrderLine", new Column { Name = "OrderId", DataType = "int" }),
            ],
        };
        var rel = Assert.Single(InferredRelationships.Compute(model));
        Assert.Equal("dbo.orders", rel.ToObjectId);
        Assert.Equal("medium", rel.Confidence);
    }

    [Fact]
    public void Compute_WorksWithoutAnySeparatorConvention()
    {
        // No underscore, no camelCase — must not assume a naming convention.
        var model = new SqlModel
        {
            RootName = "x", SourcePath = "x",
            Objects =
            [
                Table("customer", new Column { Name = "id", DataType = "int" }),
                Table("order", new Column { Name = "customerid", DataType = "int" }),
            ],
        };
        var rel = Assert.Single(InferredRelationships.Compute(model));
        Assert.Equal("dbo.customer", rel.ToObjectId);
    }

    [Fact]
    public void Compute_SkipsAmbiguousStemMatchingTwoTables()
    {
        var model = new SqlModel
        {
            RootName = "x", SourcePath = "x",
            Objects =
            [
                Table("Zone", new Column { Name = "Id", DataType = "int" }),
                Table("Zones", new Column { Name = "Id", DataType = "int" }), // both canonicalize to "zone"
                Table("Location", new Column { Name = "ZoneId", DataType = "int" }),
            ],
        };
        Assert.Empty(InferredRelationships.Compute(model));
    }

    [Fact]
    public void Compute_SkipsATablesOwnIdentityColumn()
    {
        var model = new SqlModel
        {
            RootName = "x", SourcePath = "x",
            Objects = [Table("Customer", new Column { Name = "CustomerId", DataType = "int" })],
        };
        Assert.Empty(InferredRelationships.Compute(model));
    }

    [Fact]
    public void Compute_SkipsColumnAlreadyCoveredByADeclaredForeignKey()
    {
        var model = new SqlModel
        {
            RootName = "x", SourcePath = "x",
            Objects =
            [
                Table("Customer", new Column { Name = "Id", DataType = "int" }),
                Table("Order", new Column { Name = "CustomerId", DataType = "int" }),
            ],
            ForeignKeys = [new ForeignKey { FromObjectId = "dbo.order", ToObjectId = "dbo.customer", FromColumns = ["CustomerId"] }],
        };
        Assert.Empty(InferredRelationships.Compute(model));
    }
}
