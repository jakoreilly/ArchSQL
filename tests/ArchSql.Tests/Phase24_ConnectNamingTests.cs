using ArchSql.Cli;
using Xunit;

namespace ArchSql.Tests;

public class Phase24_ConnectNamingTests
{
    private static CliOptions ParseConnFile(string connectionString)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        File.WriteAllText(path, connectionString);
        try
        {
            var options = ConnectOptions.Parse(["connect", "--conn-file", path], out var exit);
            Assert.Equal(0, exit);
            Assert.NotNull(options);
            return options!;
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OutputDirAndRootDeriveFromDatabaseName()
    {
        var options = ParseConnFile("Server=any;Database=SalesWarehouse;User ID=u;Password=p");
        Assert.EndsWith("site-db-saleswarehouse", options.OutDir.Replace('\\', '/'));
        Assert.Equal("SalesWarehouse", options.SourcePath);
    }

    [Fact]
    public void DifferentDatabasesGetDifferentOutputDirs()
    {
        var a = ParseConnFile("Server=any;Database=DbOne;User ID=u;Password=p");
        var b = ParseConnFile("Server=any;Database=DbTwo;User ID=u;Password=p");
        Assert.NotEqual(a.OutDir, b.OutDir);
    }

    [Fact]
    public void MissingDatabaseFallsBackToNeutralName()
    {
        var options = ParseConnFile("Server=any;User ID=u;Password=p");
        Assert.EndsWith("site-db-database", options.OutDir.Replace('\\', '/'));
    }

    [Fact]
    public void ExplicitOutDirOverridesDerivedName()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        File.WriteAllText(path, "Server=any;Database=Whatever;User ID=u;Password=p");
        try
        {
            var options = ConnectOptions.Parse(["connect", "--conn-file", path, "--out", "custom-site"], out _);
            Assert.EndsWith("custom-site", options!.OutDir.Replace('\\', '/'));
        }
        finally { File.Delete(path); }
    }
}
