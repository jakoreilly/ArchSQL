using ArchSql.Analysis;
using ArchSql.Model;
using Xunit;

namespace ArchSql.Tests;

public class Phase8_CrudTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    // --- Pure reducer tests: hand-built ObjectDep lists, no ScriptDom involved ---

    [Fact]
    public void CrudMatrix_UpdateAndReadOnSameTargetFoldsToRU()
    {
        var model = new SqlModel
        {
            RootName = "x",
            SourcePath = "x",
            Dependencies =
            [
                new ObjectDep { FromObjectId = "dbo.usp_a", ToObjectId = "dbo.orders", Kind = "update" },
                new ObjectDep { FromObjectId = "dbo.usp_a", ToObjectId = "dbo.orders", Kind = "read" },
            ],
        };
        var entries = CrudMatrix.Build(model);
        var entry = Assert.Single(entries);
        Assert.Equal("RU", entry.Ops);
        Assert.False(entry.IsBlindSpot);
    }

    [Fact]
    public void CrudMatrix_InsertUpdateDeleteFoldsToCDUSorted()
    {
        var model = new SqlModel
        {
            RootName = "x",
            SourcePath = "x",
            Dependencies =
            [
                new ObjectDep { FromObjectId = "dbo.usp_a", ToObjectId = "dbo.orders", Kind = "insert" },
                new ObjectDep { FromObjectId = "dbo.usp_a", ToObjectId = "dbo.orders", Kind = "update" },
                new ObjectDep { FromObjectId = "dbo.usp_a", ToObjectId = "dbo.orders", Kind = "delete" },
            ],
        };
        var entries = CrudMatrix.Build(model);
        var entry = Assert.Single(entries);
        Assert.Equal("CDU", entry.Ops); // sorted by char value: C < D < U
    }

    [Fact]
    public void CrudMatrix_ExecDynamicProducesBlindSpotEntry()
    {
        var model = new SqlModel
        {
            RootName = "x",
            SourcePath = "x",
            Dependencies = [new ObjectDep { FromObjectId = "dbo.usp_a", Kind = "exec-dynamic" }],
        };
        var entries = CrudMatrix.Build(model);
        var entry = Assert.Single(entries);
        Assert.True(entry.IsBlindSpot);
        Assert.Equal("?", entry.Ops);
        Assert.Equal("dbo.usp_a", entry.Actor);
    }

    [Fact]
    public void CrudMatrix_FkAndExecKindsAreIgnored()
    {
        var model = new SqlModel
        {
            RootName = "x",
            SourcePath = "x",
            Dependencies =
            [
                new ObjectDep { FromObjectId = "dbo.orders", ToObjectId = "dbo.customers", Kind = "fk" },
                new ObjectDep { FromObjectId = "dbo.usp_a", ToObjectId = "dbo.usp_b", Kind = "exec" },
            ],
        };
        Assert.Empty(CrudMatrix.Build(model));
    }

    // --- Analyzer extraction tests: real ScriptDom parse against the crud_demo fixture ---

    [Fact]
    public void Analyzer_UpdateFromAliasResolvesToRealTableNotTheAlias()
    {
        var analyzer = new TSqlScriptDomAnalyzer();
        var facts = analyzer.Analyze("crud_demo.sql", Fixture("crud_demo.sql"));
        Assert.Contains(facts.Dependencies, d => d.Kind == "update" && d.ToObjectId == "dbo.orders");
        Assert.DoesNotContain(facts.Dependencies, d => d.ToObjectId == "dbo.o");
    }

    [Fact]
    public void Analyzer_MergeProducesInsertAndUpdateOnTarget()
    {
        var analyzer = new TSqlScriptDomAnalyzer();
        var facts = analyzer.Analyze("crud_demo.sql", Fixture("crud_demo.sql"));
        Assert.Contains(facts.Dependencies, d => d.Kind == "insert" && d.ToObjectId == "dbo.orderline");
        Assert.Contains(facts.Dependencies, d => d.Kind == "update" && d.ToObjectId == "dbo.orderline");
    }

    [Fact]
    public void Analyzer_DynamicExecProducesExecDynamicBlindSpot()
    {
        var analyzer = new TSqlScriptDomAnalyzer();
        var facts = analyzer.Analyze("crud_demo.sql", Fixture("crud_demo.sql"));
        Assert.Contains(facts.Dependencies, d => d.Kind == "exec-dynamic");
    }

    [Fact]
    public void Analyzer_TempTableWriteIsExcludedFromDependencies()
    {
        var analyzer = new TSqlScriptDomAnalyzer();
        var facts = analyzer.Analyze("crud_demo.sql", Fixture("crud_demo.sql"));
        Assert.DoesNotContain(facts.Dependencies, d => d.ToObjectId.Contains('#'));
    }

    [Fact]
    public void CrudMatrix_EndToEndOverCrudDemoFixtureHasNoTempEntries()
    {
        var analyzer = new TSqlScriptDomAnalyzer();
        var facts = analyzer.Analyze("crud_demo.sql", Fixture("crud_demo.sql"));
        var model = new SqlModel { RootName = "x", SourcePath = "x", Dependencies = facts.Dependencies };
        var entries = CrudMatrix.Build(model);
        Assert.DoesNotContain(entries, e => e.Target.Contains('#'));
    }
}
