using ArchSql.Model;
using ArchSql.Site;
using Xunit;

namespace ArchSql.Tests;

/// <summary>Guards the scalability fixes: the search index is a shared asset (not inlined into
/// every page), and the impact page ships the edge graph rather than a per-object precomputed
/// blob, so output stays linear in the object count.</summary>
public class Phase13_ScaleTests
{
    private static SqlModel TwoObjectModel() => new()
    {
        RootName = "scale",
        SourcePath = "scale",
        Objects =
        [
            new DbObject { Id = "dbo.a", Schema = "dbo", Name = "A", Kind = "table", Dialect = "tsql", DefinedInSlug = "a" },
            new DbObject { Id = "dbo.b", Schema = "dbo", Name = "B", Kind = "procedure", Dialect = "tsql", DefinedInSlug = "b" },
        ],
        Files =
        [
            new SqlFile { RelPath = "a", Slug = "a", Dialect = "tsql" },
            new SqlFile { RelPath = "b", Slug = "b", Dialect = "tsql" },
        ],
        Dependencies = [new ObjectDep { FromObjectId = "dbo.b", ToObjectId = "dbo.a", Kind = "read" }],
    };

    [Fact]
    public void SearchIndex_IsWrittenOnceAsSharedAssetAndReferencedBySrc()
    {
        var dir = Path.Combine(Path.GetTempPath(), "archsql_scale_" + Guid.NewGuid().ToString("N"));
        try
        {
            SiteGenerator.Generate(TwoObjectModel(), dir, 60);
            Assert.True(File.Exists(Path.Combine(dir, "assets", "search-index.js")));

            var index = File.ReadAllText(Path.Combine(dir, "index.html"));
            Assert.Contains("assets/search-index.js", index);
            // The index array itself must NOT be inlined into the page.
            Assert.DoesNotContain("window.ARCH_SEARCH_INDEX=", index);

            var filePage = File.ReadAllText(Path.Combine(dir, "files", "a.html"));
            Assert.Contains("../assets/search-index.js", filePage);
        }
        finally { if (Directory.Exists(dir)) { Directory.Delete(dir, recursive: true); } }
    }

    [Fact]
    public void ImpactPage_ShipsEdgeGraphNotPerObjectPrecompute()
    {
        var body = Site.Pages.ImpactPage.Body(SiteContext.Build(TwoObjectModel()));
        // The reverse-edge map and metadata are emitted; the browser does the BFS.
        Assert.Contains("window.ARCH_REV=", body);
        Assert.Contains("window.ARCH_META=", body);
        // The old per-object precomputed structure must be gone.
        Assert.DoesNotContain("window.ARCH_IMPACT=", body);
    }
}
