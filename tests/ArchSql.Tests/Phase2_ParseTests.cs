using System.Text.Json;
using ArchSql.Analysis;
using Xunit;

namespace ArchSql.Tests;

public class Phase2_ParseTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void TSql_ParsesObjectsColumnsAndForeignKeys()
    {
        var analyzer = new TSqlScriptDomAnalyzer();
        var facts = analyzer.Analyze("tsql_schema.sql", Fixture("tsql_schema.sql"));

        Assert.True(facts.ParsedCleanly);
        Assert.Contains(facts.Objects, o => o.Kind == "table" && o.Name == "Customers");
        Assert.Contains(facts.Objects, o => o.Kind == "table" && o.Name == "Orders");
        Assert.Contains(facts.Objects, o => o.Kind == "view" && o.Name == "vw_OrderSummary");
        Assert.Contains(facts.Objects, o => o.Kind == "procedure" && o.Name == "usp_GetOrdersForCustomer");

        var fk = Assert.Single(facts.ForeignKeys);
        Assert.Equal("dbo.orders", fk.FromObjectId);
        Assert.Equal("dbo.customers", fk.ToObjectId);
    }

    [Fact]
    public void TSql_ProcedureBodyProducesSelectDependency()
    {
        var analyzer = new TSqlScriptDomAnalyzer();
        var facts = analyzer.Analyze("tsql_schema.sql", Fixture("tsql_schema.sql"));
        Assert.Contains(facts.Dependencies, d => d.Kind == "select" && d.ToObjectId == "dbo.orders");
    }

    [Fact]
    public void TSql_TableWithoutPrimaryKeyHasEmptyPk()
    {
        var analyzer = new TSqlScriptDomAnalyzer();
        var facts = analyzer.Analyze("tsql_schema.sql", Fixture("tsql_schema.sql"));
        var noPk = facts.Objects.Single(o => o.Name == "NoPkTable");
        Assert.Empty(noPk.PrimaryKey);
    }

    [Fact]
    public void TSql_CreateLoginSetsCredentialFlagButNeverStoresTheSecret()
    {
        var analyzer = new TSqlScriptDomAnalyzer();
        var content = Fixture("tsql_schema.sql");
        var facts = analyzer.Analyze("tsql_schema.sql", content);

        Assert.True(facts.HasCredential);

        var json = JsonSerializer.Serialize(facts);
        Assert.DoesNotContain("DoNotLeak123", json);
    }

    [Fact]
    public void TSql_BrokenFileRecordsDiagnosticAndReturnsPartialObjects()
    {
        var analyzer = new TSqlScriptDomAnalyzer();
        var facts = analyzer.Analyze("tsql_broken.sql", Fixture("tsql_broken.sql"));

        Assert.False(facts.ParsedCleanly);
        Assert.NotEmpty(facts.Diagnostics);
    }
}
