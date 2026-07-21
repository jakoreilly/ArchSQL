using ArchSql.Analysis;
using ArchSql.Live;
using ArchSql.Model;
using Xunit;

namespace ArchSql.Tests;

public class Phase25_CatalogDetailTests
{
    private static DbObject Table(string schema, string name, params Column[] columns) => new()
    {
        Id = IdentifierRules.NormalizeId(schema, name, "tsql"),
        Schema = schema, Name = name, Kind = "table", Dialect = "tsql",
        Columns = columns.ToList(),
    };

    [Fact]
    public void IndexInventory_GroupsKeyAndIncludedColumnsInOrder()
    {
        var rows = new List<IndexColumnRow>
        {
            new("dbo", "Orders", "IX_Orders", false, false, "NONCLUSTERED", false, "CustomerId", 1, false),
            new("dbo", "Orders", "IX_Orders", false, false, "NONCLUSTERED", false, "OrderDate", 2, false),
            new("dbo", "Orders", "IX_Orders", false, false, "NONCLUSTERED", false, "Total", 0, true),
            new("dbo", "Orders", "PK_Orders", true, true, "CLUSTERED", false, "Id", 1, false),
        };
        var defs = IndexInventory.Build(rows);
        Assert.Equal(2, defs.Count);
        var ix = defs.Single(d => d.Name == "IX_Orders");
        Assert.Equal(["CustomerId", "OrderDate"], ix.KeyColumns);
        Assert.Equal(["Total"], ix.IncludedColumns);
        Assert.False(ix.IsClustered);
        var pk = defs.Single(d => d.Name == "PK_Orders");
        Assert.True(pk.IsClustered);
        Assert.True(pk.IsPrimaryKey);
    }

    [Fact]
    public void IndexAnalysis_Heaps_FlagsTableWithNoClusteredIndex()
    {
        var table = Table("dbo", "T", new Column { Name = "Id", DataType = "int" }) with
        {
            IndexDetails = [new IndexDef { Name = "IX_T", ObjectId = "dbo.t", KeyColumns = ["Id"], IsClustered = false }],
        };
        var model = new SqlModel { RootName = "x", SourcePath = "x", Objects = [table] };
        var heaps = IndexAnalysis.Heaps(model);
        Assert.Single(heaps);
    }

    [Fact]
    public void IndexAnalysis_Heaps_DoesNotFlagTableWithClusteredIndex()
    {
        var table = Table("dbo", "T", new Column { Name = "Id", DataType = "int" }) with
        {
            IndexDetails = [new IndexDef { Name = "PK_T", ObjectId = "dbo.t", KeyColumns = ["Id"], IsClustered = true }],
        };
        var model = new SqlModel { RootName = "x", SourcePath = "x", Objects = [table] };
        Assert.Empty(IndexAnalysis.Heaps(model));
    }

    [Fact]
    public void IndexAnalysis_DuplicateIndexes_DetectsIdenticalAndPrefixKeys()
    {
        var table = Table("dbo", "T", new Column { Name = "A", DataType = "int" }) with
        {
            IndexDetails =
            [
                new IndexDef { Name = "IX_A", ObjectId = "dbo.t", KeyColumns = ["A", "B"] },
                new IndexDef { Name = "IX_B", ObjectId = "dbo.t", KeyColumns = ["A", "B"] },
                new IndexDef { Name = "IX_C", ObjectId = "dbo.t", KeyColumns = ["A"] },
            ],
        };
        var model = new SqlModel { RootName = "x", SourcePath = "x", Objects = [table] };
        var pairs = IndexAnalysis.DuplicateIndexes(model);
        Assert.Contains(pairs, p => p.IndexA == "IX_A" && p.IndexB == "IX_B" && p.Relationship.Contains("identical"));
        Assert.Contains(pairs, p => (p.IndexA == "IX_A" || p.IndexB == "IX_A") && (p.IndexA == "IX_C" || p.IndexB == "IX_C"));
    }

    [Fact]
    public void IndexAnalysis_UnusedIndexes_ProducesDropStatementFromRuntimeUsage()
    {
        var table = Table("dbo", "T", new Column { Name = "A", DataType = "int" }) with
        {
            IndexDetails = [new IndexDef { Name = "IX_Dead", ObjectId = "dbo.t", KeyColumns = ["A"] }],
        };
        var model = new SqlModel
        {
            RootName = "x", SourcePath = "x", Objects = [table],
            Runtime = new RuntimeStats
            {
                Source = "live-mssql", Available = true,
                IndexStats = [new IndexStat { ObjectId = "dbo.t", IndexName = "IX_Dead", UserUpdates = 12, IsUnused = true }],
            },
        };
        var unused = Assert.Single(IndexAnalysis.UnusedIndexes(model));
        Assert.Equal("DROP INDEX [IX_Dead] ON [dbo].[T];", unused.DropStatement);
    }

    [Fact]
    public void CatalogDetailMerge_PopulatesColumnWidthAndIndexAndRowCount()
    {
        var table = Table("dbo", "T", new Column { Name = "Notes", DataType = "nvarchar" });
        var model = new SqlModel { RootName = "x", SourcePath = "x", Objects = [table] };
        var detail = new CatalogDetail(
            Columns: [new ColumnRow("dbo", "T", "Notes", "nvarchar", 200, 0, 0, true, false, 1, "SQL_Latin1_General_CP1_CI_AS")],
            Indexes: [new IndexColumnRow("dbo", "T", "PK_T", true, true, "CLUSTERED", false, "Notes", 1, false)],
            TableStats: [new TableStatsRow("dbo", "T", 4200, 1024)]);

        var merged = CatalogDetailMerge.Merge(model, detail);
        var obj = merged.Objects.Single();
        Assert.Equal(200, obj.Columns[0].MaxLength);
        Assert.Equal("SQL_Latin1_General_CP1_CI_AS", obj.Columns[0].Collation);
        Assert.Single(obj.IndexDetails);
        Assert.Equal(4200, obj.RowCount);
        Assert.Equal(1024, obj.ReservedKb);
    }
}
