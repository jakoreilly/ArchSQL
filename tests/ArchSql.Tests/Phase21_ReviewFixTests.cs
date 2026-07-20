using ArchSql;
using ArchSql.Analysis;
using ArchSql.Cli;
using ArchSql.Model;
using ArchSql.Site;
using Xunit;

namespace ArchSql.Tests;

public class Phase21_ReviewFixTests
{
    private static SqlModel Analyze(string sql)
    {
        var dir = Path.Combine(Path.GetTempPath(), "archsql_rev_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.sql"), sql);
            return Pipeline.BuildModel(new CliOptions { SourcePath = dir });
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void UnqualifiedObject_GetsDefaultDboSchemaForDisplay()
    {
        // A procedure created without a schema prefix must display as "dbo.Name", not ".Name".
        var model = Analyze("CREATE PROCEDURE usp_NoSchema AS BEGIN SELECT 1; END");
        var proc = Assert.Single(model.Objects, o => o.Kind == "procedure");
        Assert.Equal("dbo", proc.Schema);
        Assert.Equal("dbo.usp_noschema", proc.Id);
    }

    [Fact]
    public void ProcedureComplexity_IsComputedFromBodyNotAlwaysOne()
    {
        var model = Analyze("""
            CREATE PROCEDURE dbo.usp_Branchy @x INT AS
            BEGIN
                IF @x > 0 SET @x = 1;
                WHILE @x < 10 SET @x = @x + 1;
                IF @x = 5 SET @x = 6;
            END
            """);
        var proc = Assert.Single(model.Objects, o => o.Kind == "procedure");
        Assert.True(proc.Cyclomatic >= 4, $"expected complexity >= 4 (1 + 2 IF + 1 WHILE), got {proc.Cyclomatic}");
    }

    [Fact]
    public void ImpactReverseGraph_HasNoDuplicateEdges()
    {
        // A proc that reads the same table in two statements must not produce two identical
        // reverse edges (which would double-count the blast radius).
        var model = new SqlModel
        {
            RootName = "x",
            SourcePath = "x",
            Dependencies =
            [
                new ObjectDep { FromObjectId = "dbo.p", ToObjectId = "dbo.t", Kind = "read" },
                new ObjectDep { FromObjectId = "dbo.p", ToObjectId = "dbo.t", Kind = "read" },
            ],
        };
        var reverse = ImpactGraph.BuildReverse(model);
        Assert.Single(reverse["dbo.t"]);
    }

    [Fact]
    public void Scorecard_DeadObjectRow_LinksToAnExistingPage()
    {
        var model = Analyze("CREATE PROCEDURE dbo.usp_Dead AS BEGIN SELECT 1; END");
        var card = SqlScorecard.Build(model);
        var dead = card.Rows.FirstOrDefault(r => r.Metric.Contains("Dead", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(dead);
        // The old link target hotspots.html was never generated.
        Assert.NotEqual("hotspots.html", dead!.Link);
    }

    [Fact]
    public void GraphPage_And3dAssetsWired()
    {
        var dir = Path.Combine(Path.GetTempPath(), "archsql_g3d_" + Guid.NewGuid().ToString("N"));
        try
        {
            var model = Analyze("CREATE TABLE dbo.T (Id INT NOT NULL, CONSTRAINT PK_T PRIMARY KEY (Id));");
            SiteGenerator.Generate(model, dir, 60);
            var graph = File.ReadAllText(Path.Combine(dir, "graph.html"));
            Assert.Contains("id=\"graph3d-canvas\"", graph);
            Assert.Contains("assets/lib/3d-force-graph.min.js", graph);
            Assert.Contains("assets/graph3d.js", graph);
            Assert.True(File.Exists(Path.Combine(dir, "assets", "graph3d.js")));
            Assert.True(File.Exists(Path.Combine(dir, "assets", "lib", "3d-force-graph.min.js")));
        }
        finally { if (Directory.Exists(dir)) { Directory.Delete(dir, recursive: true); } }
    }
}
