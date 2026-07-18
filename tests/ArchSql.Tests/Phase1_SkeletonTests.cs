using ArchSql.Analysis;
using ArchSql.Rendering;
using Xunit;

namespace ArchSql.Tests;

public class Phase1_SkeletonTests
{
    [Fact]
    public void MakeSlug_DeDuplicatesIdenticalRelPaths()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var a = Pipeline.MakeSlug("schema/orders.sql", used);
        var b = Pipeline.MakeSlug("schema/orders.sql", used);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void NormalizeId_IgnoresBracketQuoteCaseDifferences()
    {
        var a = IdentifierRules.NormalizeId("[dbo]", "[Orders]", "tsql");
        var b = IdentifierRules.NormalizeId("dbo", "ORDERS", "tsql");
        Assert.Equal(a, b);
    }

    [Fact]
    public void NormalizeId_DefaultsSchemaPerDialect()
    {
        Assert.Equal("dbo.orders", IdentifierRules.NormalizeId("", "Orders", "tsql"));
        Assert.Equal("public.accounts", IdentifierRules.NormalizeId("", "Accounts", "postgres"));
    }

    [Theory]
    [InlineData("CREATE TABLE `t` (id INT) ENGINE=InnoDB AUTO_INCREMENT=1;", "mysql")]
    [InlineData("CREATE TABLE t (id SERIAL PRIMARY KEY); $$ body $$", "postgres")]
    [InlineData("CREATE TABLE [dbo].[T] (Id INT)\nGO\n", "tsql")]
    public void DialectDetector_ScoresMarkerTokens(string sql, string expected)
    {
        Assert.Equal(expected, DialectDetector.Detect(sql, "auto"));
    }

    [Fact]
    public void ModelJsonWriter_RoundTripsByteIdentically()
    {
        var model = new Model.SqlModel { RootName = "demo", SourcePath = "C:/demo" };
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
