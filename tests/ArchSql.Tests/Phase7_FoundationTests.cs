using ArchSql.Model;
using ArchSql.Rendering;
using Xunit;

namespace ArchSql.Tests;

public class Phase7_FoundationTests
{
    [Fact]
    public void ModelUpgrader_UpgradesV0ToCurrent()
    {
        var model = new SqlModel { RootName = "x", SourcePath = "x", SchemaVersion = 0 };
        var upgraded = ModelUpgrader.Upgrade(model);
        Assert.Equal(SchemaVersions.Current, upgraded.SchemaVersion);
    }

    [Fact]
    public void ModelUpgrader_V2ModelGetsEmptyRuntimeAndCurrentVersion()
    {
        // A v2 model predates the Runtime field; on load it must upgrade to the current version
        // with an empty, unavailable RuntimeStats rather than null.
        var model = new SqlModel { RootName = "x", SourcePath = "x", SchemaVersion = 2 };
        var upgraded = ModelUpgrader.Upgrade(model);
        Assert.Equal(SchemaVersions.Current, upgraded.SchemaVersion);
        Assert.NotNull(upgraded.Runtime);
        Assert.False(upgraded.Runtime.Available);
        Assert.Empty(upgraded.Runtime.ObjectStats);
    }

    [Fact]
    public void ModelUpgrader_ThrowsOnFutureSchemaVersion()
    {
        var model = new SqlModel { RootName = "x", SourcePath = "x", SchemaVersion = 99 };
        Assert.Throws<InvalidDataException>(() => ModelUpgrader.Upgrade(model));
    }

    [Fact]
    public void ModelJsonReader_UpgradesOnLoad()
    {
        var model = new SqlModel { RootName = "x", SourcePath = "x", SchemaVersion = 0 };
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            ModelJsonWriter.Write(model, path);
            var reloaded = ModelJsonReader.Read(path);
            Assert.Equal(SchemaVersions.Current, reloaded.SchemaVersion);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ModelJsonWriter_RoundTripsByteIdenticallyWithPopulatedModel()
    {
        var model = new SqlModel
        {
            RootName = "demo",
            SourcePath = "C:/demo",
            SchemaVersion = SchemaVersions.Current,
            Objects = [new DbObject { Id = "dbo.orders", Schema = "dbo", Name = "Orders", Kind = "table", Dialect = "tsql" }],
            Crud = [new CrudEntry { Actor = "dbo.usp_x", Target = "dbo.orders", Ops = "R" }],
            Runtime = new RuntimeStats
            {
                Source = "live-mssql",
                Available = true,
                Note = "captured live",
                ObjectStats = [new ObjectStat { ObjectId = "dbo.usp_x", ExecutionCount = 10, TotalWorkerTimeMs = 5, TotalLogicalReads = 99 }],
                IndexStats = [new IndexStat { ObjectId = "dbo.orders", IndexName = "IX_x", UserUpdates = 3, IsUnused = true }],
                MissingIndexes = [new MissingIndex { ObjectId = "dbo.orders", EqualityColumns = "[a]", ImpactScore = 1234.5 }],
            },
        };
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            ModelJsonWriter.Write(model, path);
            var reloaded = ModelJsonReader.Read(path);
            var path2 = path + ".2";
            ModelJsonWriter.Write(reloaded, path2);
            Assert.Equal(File.ReadAllText(path), File.ReadAllText(path2));
            File.Delete(path2);
        }
        finally { File.Delete(path); }
    }
}
