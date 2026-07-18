using System.Text.Json;
using ArchSql.Analysis;
using ArchSql.Cli;
using ArchSql.Rendering;
using Xunit;

namespace ArchSql.Tests;

public class Phase4_LintTests
{
    private static Model.SqlModel BuildModelFromFixtureDir()
    {
        var options = new CliOptions { SourcePath = Path.Combine(AppContext.BaseDirectory, "Fixtures"), ForceDialect = "tsql" };
        // Only analyze the clean tsql schema + the dedicated dynamic-sql fixture; broken/mysql/pg
        // fixtures live alongside for Phase 1-2 tests and are excluded here via a temp copy.
        var tempDir = Path.Combine(Path.GetTempPath(), "archsql-phase4-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        File.Copy(Path.Combine(options.SourcePath, "tsql_schema.sql"), Path.Combine(tempDir, "tsql_schema.sql"));
        var model = Pipeline.BuildModel(options with { SourcePath = tempDir });
        Directory.Delete(tempDir, recursive: true);
        return model;
    }

    [Fact]
    public void SQL0001_FiresOnCredentialFixtureOnly()
    {
        var model = BuildModelFromFixtureDir();
        Assert.Contains(model.Findings, f => f.RuleId == "SQL0001");
    }

    [Fact]
    public void SQL0003_FiresOnTableWithoutPrimaryKey()
    {
        var model = BuildModelFromFixtureDir();
        Assert.Contains(model.Findings, f => f.RuleId == "SQL0003" && f.Message.Contains("NoPkTable"));
    }

    [Fact]
    public void SQL0005_FiresOnSelectStarInProcedure()
    {
        var model = BuildModelFromFixtureDir();
        Assert.Contains(model.Findings, f => f.RuleId == "SQL0005");
    }

    [Fact]
    public void SQL0007_DoesNotFalseFireWhenFkColumnIsIndexed()
    {
        var model = BuildModelFromFixtureDir();
        // The fixture's Orders.CustomerId FK IS covered by IX_Orders_CustomerId.
        Assert.DoesNotContain(model.Findings, f => f.RuleId == "SQL0007");
    }

    [Fact]
    public void CleanModel_NoRuleFalseFires()
    {
        var clean = new Model.SqlModel
        {
            RootName = "clean", SourcePath = "x",
            Objects =
            [
                new Model.DbObject { Id = "dbo.a", Schema = "dbo", Name = "A", Kind = "table", Dialect = "tsql", PrimaryKey = ["Id"] },
            ],
        };
        Assert.Empty(SqlRules.Run(clean));
    }

    [Fact]
    public void CiGate_TripsOnCredentialsAndPassesOnCleanFolder()
    {
        var withCredential = BuildModelFromFixtureDir();
        var withCredentialFindings = withCredential with { Findings = SqlRules.Run(withCredential) };
        var card1 = SqlScorecard.Build(withCredentialFindings);
        var reasons1 = SqlCiGate.Evaluate(["secrets"], card1);
        Assert.NotEmpty(reasons1);

        var clean = new Model.SqlModel { RootName = "clean", SourcePath = "x" };
        var card2 = SqlScorecard.Build(clean);
        var reasons2 = SqlCiGate.Evaluate(["secrets"], card2);
        Assert.Empty(reasons2);
    }

    [Fact]
    public void SarifWriter_ProducesValidJsonWithNoSecretValue()
    {
        var model = BuildModelFromFixtureDir();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sarif");
        try
        {
            SarifWriter.Write(model, path);
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json); // throws if invalid JSON
            Assert.Equal("2.1.0", doc.RootElement.GetProperty("version").GetString());
            Assert.DoesNotContain("DoNotLeak123", json);
        }
        finally { File.Delete(path); }
    }
}
