using ArchSql.Model;
using ArchSql.Site;
using Xunit;

namespace ArchSql.Tests;

public class Phase6_SiteTests
{
    [Fact]
    public void Generate_CopiesAssetsIntoOutputDirectory()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "archsql-site-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);

        try
        {
            var model = new SqlModel
            {
                RootName = "TestSQL",
                SourcePath = Path.GetTempPath(),
                Files = [],
                Objects = [],
                ForeignKeys = [],
                Dependencies = [],
                Findings = [],
                Diagnostics = [],
                DialectLoc = [],
                Dialect = "unknown"
            };

            SiteGenerator.Generate(model, outDir, 60);

            Assert.True(File.Exists(Path.Combine(outDir, "assets", "site.css")));
            Assert.True(File.Exists(Path.Combine(outDir, "assets", "site.js")));
            Assert.True(File.Exists(Path.Combine(outDir, "assets", "lib", "mermaid.min.js")));
        }
        finally
        {
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, recursive: true);
            }
        }
    }
}
