using ArchSql.Analysis;
using ArchSql.Model;
using Xunit;

namespace ArchSql.Tests;

public class Phase9_ImpactTests
{
    [Fact]
    public void Dependents_TransitiveChainReturnsSortedByDepth()
    {
        // A depends on B, B depends on C. Querying impact of C should surface B (depth 1) then A (depth 2).
        var model = new SqlModel
        {
            RootName = "x",
            SourcePath = "x",
            Dependencies =
            [
                new ObjectDep { FromObjectId = "a", ToObjectId = "b", Kind = "read" },
                new ObjectDep { FromObjectId = "b", ToObjectId = "c", Kind = "read" },
            ],
        };
        var reverse = ImpactGraph.BuildReverse(model);
        var (hits, capped) = ImpactGraph.Dependents(reverse, "c");

        Assert.False(capped);
        Assert.Equal(2, hits.Count);
        Assert.Equal("b", hits[0].ObjectId);
        Assert.Equal(1, hits[0].Depth);
        Assert.Equal("a", hits[1].ObjectId);
        Assert.Equal(2, hits[1].Depth);
    }

    [Fact]
    public void Dependents_QueryingTheRootOfTheChainReturnsEmpty()
    {
        var model = new SqlModel
        {
            RootName = "x",
            SourcePath = "x",
            Dependencies = [new ObjectDep { FromObjectId = "a", ToObjectId = "b", Kind = "read" }],
        };
        var reverse = ImpactGraph.BuildReverse(model);
        var (hits, _) = ImpactGraph.Dependents(reverse, "a");
        Assert.Empty(hits);
    }

    [Fact]
    public void Dependents_TwoNodeCycleTerminatesViaVisitedSetNotTheDepthCap()
    {
        // A 2-node cycle (a<->b) is exhausted by the visited set after one hop each way; it must
        // NOT need the depth cap to terminate (that would mean the visited-set dedup is broken).
        var model = new SqlModel
        {
            RootName = "x",
            SourcePath = "x",
            Dependencies =
            [
                new ObjectDep { FromObjectId = "a", ToObjectId = "b", Kind = "read" },
                new ObjectDep { FromObjectId = "b", ToObjectId = "a", Kind = "read" },
            ],
        };
        var reverse = ImpactGraph.BuildReverse(model);
        var (hits, capped) = ImpactGraph.Dependents(reverse, "a");
        Assert.False(capped);
        Assert.Equal(["b"], hits.Select(h => h.ObjectId));
    }

    [Fact]
    public void Dependents_LongChainCapsAtMaxDepth()
    {
        var deps = new List<ObjectDep>();
        for (var i = 0; i < 40; i++) { deps.Add(new ObjectDep { FromObjectId = $"n{i}", ToObjectId = $"n{i + 1}", Kind = "read" }); }
        var model = new SqlModel { RootName = "x", SourcePath = "x", Dependencies = deps };

        var reverse = ImpactGraph.BuildReverse(model);
        var (hits, capped) = ImpactGraph.Dependents(reverse, "n40");

        Assert.True(capped);
        Assert.All(hits, h => Assert.True(h.Depth <= ImpactGraph.MaxDepth));
        Assert.Equal(ImpactGraph.MaxDepth, hits.Count);
    }

    [Fact]
    public void BuildReverse_CascadeForeignKeyProducesFkCascadeViaKind()
    {
        var model = new SqlModel
        {
            RootName = "x",
            SourcePath = "x",
            ForeignKeys = [new ForeignKey { FromObjectId = "dbo.orderline", ToObjectId = "dbo.orders", OnDelete = "Cascade" }],
        };
        var reverse = ImpactGraph.BuildReverse(model);
        var (hits, _) = ImpactGraph.Dependents(reverse, "dbo.orders");
        var hit = Assert.Single(hits);
        Assert.Equal("dbo.orderline", hit.ObjectId);
        Assert.Equal("fk-cascade", hit.ViaKind);
    }

    [Fact]
    public void BuildReverse_NonCascadeForeignKeyProducesPlainFkViaKind()
    {
        var model = new SqlModel
        {
            RootName = "x",
            SourcePath = "x",
            ForeignKeys = [new ForeignKey { FromObjectId = "dbo.orderline", ToObjectId = "dbo.orders", OnDelete = "NoAction" }],
        };
        var reverse = ImpactGraph.BuildReverse(model);
        var (hits, _) = ImpactGraph.Dependents(reverse, "dbo.orders");
        var hit = Assert.Single(hits);
        Assert.Equal("fk", hit.ViaKind);
    }
}
