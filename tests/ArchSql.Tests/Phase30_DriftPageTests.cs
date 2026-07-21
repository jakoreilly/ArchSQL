using ArchSql.Analysis;
using ArchSql.Cli;
using ArchSql.Model;
using ArchSql.Site;
using Xunit;

namespace ArchSql.Tests;

public class Phase30_DriftPageTests
{
    [Fact]
    public void Body_NullChangesShowsGuidanceEmptyState()
    {
        var body = Site.Pages.DriftPage.Body(null);
        Assert.Contains("No baseline was supplied", body);
        Assert.Contains("--baseline", body);
    }

    [Fact]
    public void Body_EmptyChangesListShowsNoChangesState()
    {
        var body = Site.Pages.DriftPage.Body([]);
        Assert.Contains("No schema changes detected", body);
    }

    [Fact]
    public void Body_RendersChangesTable()
    {
        var changes = new List<SchemaChange> { new("Column dropped", "dbo.T.Col", ChangeRisk.Breaking, "column removed") };
        var body = Site.Pages.DriftPage.Body(changes);
        Assert.Contains("dbo.T.Col", body);
        Assert.Contains("Breaking", body);
    }

    [Fact]
    public void CliOptions_ParsesBaselineFlag()
    {
        var dir = Path.Combine(Path.GetTempPath(), "archsql_baseline_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var options = CliOptions.Parse([dir, "--baseline", "old.json"], out var exit);
            Assert.Equal(0, exit);
            Assert.Equal("old.json", options!.BaselineModelPath);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void SiteGenerator_WritesDriftPageWithoutBaseline()
    {
        var dir = Path.Combine(Path.GetTempPath(), "archsql_drift_" + Guid.NewGuid().ToString("N"));
        try
        {
            var model = new SqlModel { RootName = "x", SourcePath = "x" };
            SiteGenerator.Generate(model, dir, 60);
            var html = File.ReadAllText(Path.Combine(dir, "drift.html"));
            Assert.Contains("No baseline was supplied", html);
        }
        finally { if (Directory.Exists(dir)) { Directory.Delete(dir, recursive: true); } }
    }

    [Fact]
    public void SiteGenerator_WritesDriftPageWithBaselineChanges()
    {
        var dir = Path.Combine(Path.GetTempPath(), "archsql_drift2_" + Guid.NewGuid().ToString("N"));
        try
        {
            var model = new SqlModel { RootName = "x", SourcePath = "x" };
            var changes = new List<SchemaChange> { new("Table dropped", "dbo.Old", ChangeRisk.Breaking, "table removed") };
            SiteGenerator.Generate(model, dir, 60, changes);
            var html = File.ReadAllText(Path.Combine(dir, "drift.html"));
            Assert.Contains("dbo.Old", html);
        }
        finally { if (Directory.Exists(dir)) { Directory.Delete(dir, recursive: true); } }
    }
}
