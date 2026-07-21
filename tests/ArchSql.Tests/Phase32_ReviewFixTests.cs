using ArchSql.Analysis;
using ArchSql.Model;
using ArchSql.Site;
using Xunit;

namespace ArchSql.Tests;

public class Phase32_ReviewFixTests
{
    private static DbObject Table(string schema, string name, params Column[] columns) => new()
    {
        Id = IdentifierRules.NormalizeId(schema, name, "tsql"),
        Schema = schema, Name = name, Kind = "table", Dialect = "tsql",
        Columns = columns.ToList(),
    };

    // D1 — Maintenance renders whenever its own data is available, independent of DMV runtime.
    [Fact]
    public void ActivityPage_RendersMaintenanceWhenRuntimeUnavailableButMaintenanceIsPresent()
    {
        var model = new SqlModel
        {
            RootName = "x", SourcePath = "x",
            Runtime = new RuntimeStats
            {
                Available = false,
                Maintenance = new MaintenanceInfo { Available = true, DaysSinceLastBackup = 3 },
            },
        };
        var body = Site.Pages.ActivityPage.Body(SiteContext.Build(model));
        Assert.Contains("<h2>Maintenance</h2>", body);
        Assert.Contains("No runtime data.", body);
    }

    [Fact]
    public void ActivityPage_OmitsMaintenanceHeadingWhenNeitherRuntimeNorMaintenanceAvailable()
    {
        var model = new SqlModel
        {
            RootName = "x", SourcePath = "x",
            Runtime = new RuntimeStats { Available = false, Maintenance = new MaintenanceInfo { Available = false } },
        };
        var body = Site.Pages.ActivityPage.Body(SiteContext.Build(model));
        Assert.DoesNotContain("<h2>Maintenance</h2>", body);
    }

    // D2 — true zero-index heaps are detected when index inventory was captured for the run,
    // but no table is flagged when the run captured no index inventory at all (file scan).
    [Fact]
    public void IndexAnalysis_Heaps_FlagsZeroIndexTableWhenInventoryWasCaptured()
    {
        var zeroIndexTable = Table("dbo", "NoIndexAtAll", new Column { Name = "Id", DataType = "int" });
        var otherTable = Table("dbo", "Indexed", new Column { Name = "Id", DataType = "int" }) with
        {
            IndexDetails = [new IndexDef { Name = "PK_Indexed", ObjectId = "dbo.indexed", KeyColumns = ["Id"], IsClustered = true }],
        };
        var model = new SqlModel { RootName = "x", SourcePath = "x", Objects = [zeroIndexTable, otherTable] };
        var heaps = IndexAnalysis.Heaps(model);
        Assert.Contains(heaps, o => o.Id == zeroIndexTable.Id);
        Assert.DoesNotContain(heaps, o => o.Id == otherTable.Id);
    }

    [Fact]
    public void IndexAnalysis_Heaps_ReturnsNoneWhenNoIndexInventoryWasCapturedAtAll()
    {
        var model = new SqlModel
        {
            RootName = "x", SourcePath = "x",
            Objects = [Table("dbo", "A", new Column { Name = "Id", DataType = "int" }), Table("dbo", "B", new Column { Name = "Id", DataType = "int" })],
        };
        Assert.Empty(IndexAnalysis.Heaps(model));
    }

    // D3 — SQL0020 only fires on procedures that were actually body-scanned.
    [Fact]
    public void SQL0020_DoesNotFireWhenProcedureWasNotScanned()
    {
        var proc = new DbObject
        {
            Id = "dbo.usp_unscanned", Schema = "dbo", Name = "usp_unscanned", Kind = "procedure", Dialect = "tsql",
            CodeFlags = new CodeFlags { Scanned = false, HasSetNoCount = false },
        };
        var model = new SqlModel { RootName = "x", SourcePath = "x", Objects = [proc] };
        Assert.DoesNotContain(SqlRules.Run(model), f => f.RuleId == "SQL0020");
    }

    [Fact]
    public void SQL0020_FiresWhenProcedureWasScannedAndMissingSetNoCount()
    {
        var proc = new DbObject
        {
            Id = "dbo.usp_scanned", Schema = "dbo", Name = "usp_scanned", Kind = "procedure", Dialect = "tsql",
            CodeFlags = new CodeFlags { Scanned = true, HasSetNoCount = false },
        };
        var model = new SqlModel { RootName = "x", SourcePath = "x", Objects = [proc] };
        Assert.Contains(SqlRules.Run(model), f => f.RuleId == "SQL0020");
    }

    // D4 — EXECUTE AS is ignored inside comments and string literals, but detected in real DDL.
    [Fact]
    public void CodeFlagsScanner_DoesNotFlagExecuteAsInsideLineComment()
    {
        Assert.False(CodeFlagsScanner.Scan("-- EXECUTE AS OWNER\nSELECT 1;").UsesExecuteAs);
    }

    [Fact]
    public void CodeFlagsScanner_DoesNotFlagExecuteAsInsideBlockComment()
    {
        Assert.False(CodeFlagsScanner.Scan("/* EXECUTE AS OWNER */ SELECT 1;").UsesExecuteAs);
    }

    [Fact]
    public void CodeFlagsScanner_DoesNotFlagExecuteAsInsideStringLiteral()
    {
        Assert.False(CodeFlagsScanner.Scan("SELECT 'EXECUTE AS OWNER';").UsesExecuteAs);
    }

    [Fact]
    public void CodeFlagsScanner_StillFlagsRealExecuteAsClause()
    {
        Assert.True(CodeFlagsScanner.Scan("CREATE PROC dbo.P WITH EXECUTE AS OWNER AS SELECT 1").UsesExecuteAs);
    }

    [Fact]
    public void CodeFlagsScanner_SetsScannedTrueOnNonEmptySource()
    {
        Assert.True(CodeFlagsScanner.Scan("SELECT 1;").Scanned);
    }

    [Fact]
    public void CodeFlagsScanner_SetsScannedFalseOnEmptySource()
    {
        Assert.False(CodeFlagsScanner.Scan("").Scanned);
    }

    // D5 — UnusedIndexes uses a keyed lookup but produces identical results to the prior linear scan.
    [Fact]
    public void IndexAnalysis_UnusedIndexes_FindsExactlyTheUnusedIndexAcrossMultipleObjects()
    {
        var tableA = Table("dbo", "A", new Column { Name = "Id", DataType = "int" }) with
        {
            IndexDetails =
            [
                new IndexDef { Name = "IX_A_Live", ObjectId = "dbo.a", KeyColumns = ["Id"] },
                new IndexDef { Name = "IX_A_Dead", ObjectId = "dbo.a", KeyColumns = ["Id"] },
            ],
        };
        var tableB = Table("dbo", "B", new Column { Name = "Id", DataType = "int" }) with
        {
            IndexDetails = [new IndexDef { Name = "IX_B_Live", ObjectId = "dbo.b", KeyColumns = ["Id"] }],
        };
        var model = new SqlModel
        {
            RootName = "x", SourcePath = "x", Objects = [tableA, tableB],
            Runtime = new RuntimeStats
            {
                Available = true,
                IndexStats =
                [
                    new IndexStat { ObjectId = "dbo.a", IndexName = "IX_A_Live", UserUpdates = 1, IsUnused = false },
                    new IndexStat { ObjectId = "dbo.a", IndexName = "IX_A_Dead", UserUpdates = 7, IsUnused = true },
                    new IndexStat { ObjectId = "dbo.b", IndexName = "IX_B_Live", UserUpdates = 2, IsUnused = false },
                ],
            },
        };
        var result = IndexAnalysis.UnusedIndexes(model);
        var unused = Assert.Single(result);
        Assert.Equal("dbo.a", unused.ObjectId);
        Assert.Equal("IX_A_Dead", unused.IndexName);
        Assert.Equal(7, unused.UserUpdates);
        Assert.Equal("DROP INDEX [IX_A_Dead] ON [dbo].[A];", unused.DropStatement);
    }

    // Model round-trip: the new CodeFlags.Scanned field survives a write/read cycle.
    [Fact]
    public void ModelJsonWriter_RoundTripsCodeFlagsScanned()
    {
        var proc = new DbObject
        {
            Id = "dbo.usp_p", Schema = "dbo", Name = "usp_p", Kind = "procedure", Dialect = "tsql",
            CodeFlags = new CodeFlags { Scanned = true, HasSetNoCount = true },
        };
        var model = new SqlModel { RootName = "demo", SourcePath = "C:/demo", SchemaVersion = SchemaVersions.Current, Objects = [proc] };
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            Rendering.ModelJsonWriter.Write(model, path);
            var reloaded = Rendering.ModelJsonReader.Read(path);
            Assert.True(reloaded.Objects.Single(o => o.Id == "dbo.usp_p").CodeFlags.Scanned);
        }
        finally { File.Delete(path); }
    }
}
