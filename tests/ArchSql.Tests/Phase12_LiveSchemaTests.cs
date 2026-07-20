using ArchSql.Analysis;
using ArchSql.Live;
using Xunit;

namespace ArchSql.Tests;

public class Phase12_LiveSchemaTests
{
    [Theory]
    [InlineData("int", 4, 0, 0, "int")]
    [InlineData("nvarchar", 200, 0, 0, "nvarchar(100)")]   // n-types: max_length is bytes -> /2
    [InlineData("nvarchar", -1, 0, 0, "nvarchar(max)")]
    [InlineData("varchar", 50, 0, 0, "varchar(50)")]
    [InlineData("decimal", 9, 18, 2, "decimal(18,2)")]
    [InlineData("datetime", 8, 0, 0, "datetime")]
    public void SqlTypeText_FormatsTypesLikeDdl(string baseType, int maxLen, byte prec, byte scale, string expected)
    {
        Assert.Equal(expected, SqlTypeText.Format(baseType, maxLen, prec, scale));
    }

    [Fact]
    public void LiveDdl_ReconstructedTableRoundTripsThroughTheAnalyzer()
    {
        // The reconstructed CREATE TABLE must parse back into the same object/columns/PK/FK the
        // file scanner would produce — proving the whole live-schema path without a database.
        var objects = new List<ObjectRow> { new("dbo", "Orders", "U"), new("dbo", "OrderLine", "U") };
        var columns = new List<ColumnRow>
        {
            new("dbo", "Orders", "OrderId", "int", 4, 0, 0, false, true, 1),
            new("dbo", "Orders", "Total", "decimal", 9, 18, 2, false, false, 2),
            new("dbo", "OrderLine", "OrderLineId", "int", 4, 0, 0, false, true, 1),
            new("dbo", "OrderLine", "OrderId", "int", 4, 0, 0, false, false, 2),
        };
        var pks = new List<PkRow> { new("dbo", "Orders", "OrderId", 1), new("dbo", "OrderLine", "OrderLineId", 1) };
        var fks = new List<FkRow>
        {
            new("dbo", "OrderLine", "dbo", "Orders", "FK_OrderLine_Orders", "CASCADE", "OrderId", "OrderId", 1),
        };

        var units = LiveDdl.BuildUnits(objects, columns, pks, fks, []);
        var analyzer = new TSqlScriptDomAnalyzer();

        var orderLineUnit = units.Single(u => u.RelPath.EndsWith("OrderLine"));
        var facts = analyzer.Analyze(orderLineUnit.RelPath, orderLineUnit.Content);

        var table = Assert.Single(facts.Objects);
        Assert.Equal("dbo.orderline", table.Id);
        Assert.Equal("table", table.Kind);
        Assert.Equal(["OrderLineId", "OrderId"], table.Columns.Select(c => c.Name));
        Assert.Equal(["OrderLineId"], table.PrimaryKey);
        var fk = Assert.Single(facts.ForeignKeys);
        Assert.Equal("dbo.orders", fk.ToObjectId);
        Assert.Equal("Cascade", fk.OnDelete);
    }

    [Fact]
    public void LiveDdl_ModuleDefinitionPassesThroughVerbatim()
    {
        var def = "CREATE VIEW [dbo].[vOrders] AS SELECT OrderId FROM [dbo].[Orders];";
        var units = LiveDdl.BuildUnits([], [], [], [], [new ModuleRow("dbo", "vOrders", def)]);
        var unit = Assert.Single(units);
        Assert.Equal(def, unit.Content);
    }

    [Fact]
    public void Analyzer_TempTableInProcedureBodyIsNotEmittedAsAnObject()
    {
        // Temp tables are session-local. Two procedures reusing the same temp-table name (very
        // common: #tmpdeleted, #tmp) must not each produce a dbo.#... object, or the resolver's
        // id-keyed dictionary collides — which is what a real database surfaced.
        const string proc = """
            CREATE PROCEDURE dbo.usp_a AS
            BEGIN
                CREATE TABLE #tmpdeleted (Id INT);
                INSERT INTO #tmpdeleted (Id) VALUES (1);
            END
            """;
        var facts = new TSqlScriptDomAnalyzer().Analyze("live/dbo.usp_a", proc);
        Assert.DoesNotContain(facts.Objects, o => o.Id.Contains("#tmpdeleted"));
        Assert.Contains(facts.Objects, o => o.Id == "dbo.usp_a");
    }

    [Fact]
    public void RuntimeAggregate_UnavailableProducesEmptyStatsWithReason()
    {
        var stats = RuntimeAggregate.Build([], [], [], available: false, "no permission");
        Assert.False(stats.Available);
        Assert.Equal("no permission", stats.Note);
        Assert.Empty(stats.ObjectStats);
    }

    [Fact]
    public void RuntimeAggregate_FlagsUnusedIndexAndSortsHotObjectsFirst()
    {
        var procs = new List<ProcStatRow>
        {
            new("dbo", "usp_cold", 1, 0, 0),
            new("dbo", "usp_hot", 500, 10, 20),
        };
        var indexes = new List<IndexUsageRow>
        {
            new("dbo", "Orders", "IX_used", 100, 0, 0, 5),
            new("dbo", "Orders", "IX_unused", 0, 0, 0, 42),
        };
        var missing = new List<MissingIndexRow>
        {
            new("dbo", "Orders", "[CustomerId]", "", "", 999.0),
        };

        var stats = RuntimeAggregate.Build(procs, indexes, missing, available: true);

        Assert.True(stats.Available);
        Assert.Equal("dbo.usp_hot", stats.ObjectStats[0].ObjectId); // hottest first
        Assert.True(stats.IndexStats.Single(i => i.IndexName == "IX_unused").IsUnused);
        Assert.False(stats.IndexStats.Single(i => i.IndexName == "IX_used").IsUnused);
        Assert.Equal("dbo.orders", stats.MissingIndexes[0].ObjectId);
    }
}
