using ArchSql;
using ArchSql.Analysis;
using ArchSql.Cli;
using Xunit;

namespace ArchSql.Tests;

public class Phase14_ExclusionTests
{
    [Theory]
    [InlineData("usp_Foo_bak", "*_bak", true)]
    [InlineData("usp_Foo_BAK", "*_bak", true)] // case-insensitive
    [InlineData("usp_Foo", "*_bak", false)]
    [InlineData("tmp_Widgets", "tmp_*", true)]
    [InlineData("Widgets", "tmp_*", false)]
    [InlineData("abc", "a?c", true)]
    [InlineData("abbc", "a?c", false)]
    [InlineData("anything", "*", true)]
    [InlineData("", "*", true)]
    public void Glob_MatchesWildcardsCaseInsensitively(string text, string pattern, bool expected)
    {
        Assert.Equal(expected, Glob.IsMatch(text, pattern));
    }

    [Fact]
    public void ConfigLoader_MissingFileYieldsEmptyConfigNotError()
    {
        var config = ConfigLoader.Load(null, Path.GetTempPath(), out var error);
        Assert.Null(error);
        Assert.Empty(config.ExcludePatterns);
    }

    [Fact]
    public void ConfigLoader_ReadsExcludePatternsFromExplicitPath()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, """{"excludePatterns": ["*_bak", "tmp_*"]}""");
            var config = ConfigLoader.Load(path, Path.GetTempPath(), out var error);
            Assert.Null(error);
            Assert.Equal(["*_bak", "tmp_*"], config.ExcludePatterns);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ConfigLoader_MalformedJsonReturnsError()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, "{ not json");
            ConfigLoader.Load(path, Path.GetTempPath(), out var error);
            Assert.NotNull(error);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Pipeline_ExcludePatternDropsBackupCopyAndItsFalseDuplicateWarning()
    {
        var dir = Path.Combine(Path.GetTempPath(), "archsql_excl_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.sql"), "CREATE TABLE [dbo].[Widget] (Id INT);");
            // A stale backup whose authored definition still names the ORIGINAL object — this is
            // the real-world shape that produced a false "duplicate object id" diagnostic.
            File.WriteAllText(Path.Combine(dir, "b.sql"), "CREATE PROCEDURE [dbo].[Widget_bak] AS SELECT 1;");

            var options = new CliOptions { SourcePath = dir, ExcludePatterns = ["*_bak"] };
            var model = Pipeline.BuildModel(options);

            Assert.Single(model.Objects, o => o.Id == "dbo.widget");
            Assert.DoesNotContain(model.Objects, o => o.Id.Contains("bak"));
            Assert.DoesNotContain(model.Diagnostics, d => d.Contains("Duplicate object id"));
            Assert.Contains(model.Diagnostics, d => d.Contains("Excluded 1 object(s)"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
