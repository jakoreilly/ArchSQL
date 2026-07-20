using ArchSql.Model;
using ArchSql.Site;
using Xunit;

namespace ArchSql.Tests;

public class Phase15_GraphAndObjectPageTests
{
    private static SqlModel TwoObjectModel() => new()
    {
        RootName = "graph",
        SourcePath = "graph",
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
    public void GraphData_WritesAssetWithExpectedFieldNames()
    {
        var dir = Path.Combine(Path.GetTempPath(), "archsql_graph_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var model = TwoObjectModel();
            var ctx = SiteContext.Build(model);
            GraphData.WriteAsset(ctx, dir);

            var js = File.ReadAllText(Path.Combine(dir, "assets", "graph-data.js"));
            Assert.Contains("window.ARCH_QUERY=", js);
            // Field names the vendored engine reads verbatim (fanIn/fanOut camelCase, path, href).
            Assert.Contains("\"fanIn\"", js);
            Assert.Contains("\"fanOut\"", js);
            Assert.Contains("\"path\":\"dbo.A\"", js);
            Assert.Contains("\"href\":\"object.html?id=dbo.a\"", js);
            Assert.Contains("\"source\":\"dbo.b\"", js);
            Assert.Contains("\"target\":\"dbo.a\"", js);

            Assert.True(File.Exists(Path.Combine(dir, "graph.json")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ObjectDetailData_WritesColumnsAndPurposePerObject()
    {
        var dir = Path.Combine(Path.GetTempPath(), "archsql_objdetail_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var model = TwoObjectModel() with
            {
                Objects =
                [
                    new DbObject { Id = "dbo.a", Schema = "dbo", Name = "A", Kind = "table", Dialect = "tsql", DefinedInSlug = "a",
                        Columns = [new Column { Name = "Id", DataType = "int", Nullable = false }], PrimaryKey = ["Id"] },
                ],
            };
            var ctx = SiteContext.Build(model);
            ObjectDetailData.WriteAsset(ctx, dir);

            var js = File.ReadAllText(Path.Combine(dir, "assets", "object-detail.js"));
            Assert.Contains("window.ARCH_OBJDETAIL=", js);
            Assert.Contains("\"name\":\"Id\"", js);
            Assert.Contains("\"pk\":true", js);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void SiteGenerator_WritesOneObjectHtmlNotPerObjectFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "archsql_objpage_" + Guid.NewGuid().ToString("N"));
        try
        {
            SiteGenerator.Generate(TwoObjectModel(), dir, 60);
            Assert.True(File.Exists(Path.Combine(dir, "object.html")));
            var html = File.ReadAllText(Path.Combine(dir, "object.html"));
            Assert.Contains("id=\"object-page\"", html);
            Assert.Contains("graph-data.js", html);
            Assert.Contains("object-detail.js", html);
            // graph-data.js must appear before site.js in the markup so the IIFEs see the payload.
            Assert.True(html.IndexOf("graph-data.js", StringComparison.Ordinal) < html.IndexOf("assets/site.js", StringComparison.Ordinal));
        }
        finally { if (Directory.Exists(dir)) { Directory.Delete(dir, recursive: true); } }
    }

    [Fact]
    public void ObjectsPage_LinksToObjectHtmlNotPerObjectFile()
    {
        var body = Site.Pages.ObjectsPage.Body(SiteContext.Build(TwoObjectModel()));
        // The object's own name links to the client detail page; the separate "File" column may
        // still point at the raw source file — that link is intentionally unchanged.
        Assert.Contains("""<a href="object.html?id=dbo.a">A</a>""", body);
    }
}
