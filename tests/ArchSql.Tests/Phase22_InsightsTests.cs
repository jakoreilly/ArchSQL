using ArchSql.Analysis;
using ArchSql.Model;
using Xunit;

namespace ArchSql.Tests;

public class Phase22_InsightsTests
{
    private static DbObject Obj(string id, string kind = "procedure") =>
        new() { Id = id, Schema = "dbo", Name = id.Replace("dbo.", ""), Kind = kind, Dialect = "tsql" };

    [Fact]
    public void GraphInsights_DetectsA2NodeCycle()
    {
        var model = new SqlModel
        {
            RootName = "x", SourcePath = "x",
            Objects = [Obj("dbo.a"), Obj("dbo.b"), Obj("dbo.c")],
            Dependencies =
            [
                new ObjectDep { FromObjectId = "dbo.a", ToObjectId = "dbo.b", Kind = "exec" },
                new ObjectDep { FromObjectId = "dbo.b", ToObjectId = "dbo.a", Kind = "exec" },
                new ObjectDep { FromObjectId = "dbo.c", ToObjectId = "dbo.a", Kind = "exec" },
            ],
        };
        var insight = GraphInsights.Compute(model);
        var cycle = Assert.Single(insight.Cycles);
        Assert.Equal(2, cycle.Count);
        Assert.Contains("dbo.a", cycle);
        Assert.Contains("dbo.b", cycle);
    }

    [Fact]
    public void GraphInsights_AcyclicGraphHasNoCycles()
    {
        var model = new SqlModel
        {
            RootName = "x", SourcePath = "x",
            Objects = [Obj("dbo.a"), Obj("dbo.b")],
            Dependencies = [new ObjectDep { FromObjectId = "dbo.a", ToObjectId = "dbo.b", Kind = "exec" }],
        };
        Assert.Empty(GraphInsights.Compute(model).Cycles);
    }

    [Fact]
    public void GraphInsights_InstabilityIsOutOverInPlusOut()
    {
        var model = new SqlModel
        {
            RootName = "x", SourcePath = "x",
            Objects = [Obj("dbo.a"), Obj("dbo.b"), Obj("dbo.c")],
            Dependencies =
            [
                new ObjectDep { FromObjectId = "dbo.a", ToObjectId = "dbo.b", Kind = "exec" },
                new ObjectDep { FromObjectId = "dbo.a", ToObjectId = "dbo.c", Kind = "exec" },
            ],
        };
        var a = GraphInsights.Compute(model).GodObjects.Single(g => g.ObjectId == "dbo.a");
        Assert.Equal(0, a.FanIn);
        Assert.Equal(2, a.FanOut);
        Assert.Equal(1.0, a.Instability, 3);
    }

    [Theory]
    [InlineData("Shop_CartItem_X", "Shop")] 
    [InlineData("Int_Process_Y", "Int")]
    [InlineData("NoUnderscore", "NoUnderscore")]
    public void DomainGrouping_DomainOfUsesFirstToken(string name, string expected)
    {
        Assert.Equal(expected, DomainGrouping.DomainOf(name));
    }

    [Fact]
    public void DomainGrouping_CountsCrossDomainEdges()
    {
        var model = new SqlModel
        {
            RootName = "x", SourcePath = "x",
            Objects = [Obj("dbo.Shop_A"), Obj("dbo.Int_B", "table")],
            Dependencies = [new ObjectDep { FromObjectId = "dbo.Shop_A", ToObjectId = "dbo.Int_B", Kind = "read" }],
        };
        var result = DomainGrouping.Compute(model);
        var edge = Assert.Single(result.CrossEdges);
        Assert.Equal("Shop", edge.From);
        Assert.Equal("Int", edge.To);
    }
}
