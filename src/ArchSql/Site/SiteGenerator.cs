using System.Text;
using ArchSql.Model;
using ArchSql.Rendering;
using ArchSql.Site.Pages;

using System.Reflection;

namespace ArchSql.Site;

/// <summary>Orchestrates static-site output: one WritePage call per page, copies ArchDiagram's
/// SiteGenerator idiom exactly (UTF-8 no BOM, PageTemplate.Render, relRoot convention).</summary>
public static class SiteGenerator
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string? Generate(SqlModel model, string outDir, int maxNodes)
    {
        Directory.CreateDirectory(outDir);
        CopyAssetTree(outDir);
        var ctx = SiteContext.Build(model);

        WritePage(outDir, "index.html", "Overview", model, "index.html", "", PageTemplate.Crumbs((null, "Overview")), IndexPage.Body(ctx));
        WritePage(outDir, "guide.html", "Guide", model, "guide.html", "", PageTemplate.Crumbs((null, "Guide")), GuidePage.Body(ctx));
        WritePage(outDir, "objects.html", "Objects", model, "objects.html", "", PageTemplate.Crumbs((null, "Objects")), ObjectsPage.Body(ctx));
        WritePage(outDir, "er.html", "ER Diagram", model, "er.html", "", PageTemplate.Crumbs((null, "ER Diagram")), ErPage.Body(ctx, maxNodes));
        WritePage(outDir, "dependencies.html", "Dependencies", model, "dependencies.html", "", PageTemplate.Crumbs((null, "Dependencies")), DependenciesPage.Body(ctx, maxNodes));
        WritePage(outDir, "lint.html", "Lint", model, "lint.html", "", PageTemplate.Crumbs((null, "Lint")), LintPage.Body(ctx));
        WritePage(outDir, "scorecard.html", "Scorecard", model, "scorecard.html", "", PageTemplate.Crumbs((null, "Scorecard")), ScorecardPage.Body(ctx));
        WritePage(outDir, "metrics.html", "Metrics", model, "metrics.html", "", PageTemplate.Crumbs((null, "Metrics")), MetricsPage.Body(ctx));
        WritePage(outDir, "config.html", "Config & Secrets", model, "config.html", "", PageTemplate.Crumbs((null, "Config & Secrets")), ConfigPage.Body(ctx));

        Directory.CreateDirectory(Path.Combine(outDir, "files"));
        foreach (var file in model.Files)
        {
            var crumbs = PageTemplate.Crumbs(("../objects.html", "Objects"), (null, file.RelPath));
            var html = PageTemplate.Render(file.RelPath, model.RootName, "", "../", crumbs, ObjectFilePage.Body(ctx, file));
            File.WriteAllText(Path.Combine(outDir, "files", file.Slug + ".html"), html, Utf8NoBom);
        }

        ModelJsonWriter.Write(model, Path.Combine(outDir, "model.json"));

        return Path.Combine(outDir, "index.html");
    }

    private static void WritePage(string outDir, string fileName, string title, SqlModel model, string activeHref, string relRoot, string crumbs, string body)
    {
        var html = PageTemplate.Render(title, model.RootName, activeHref, relRoot, crumbs, body);
        File.WriteAllText(Path.Combine(outDir, fileName), html, Utf8NoBom);
    }

    private static void CopyAssetTree(string outDir)
    {
        var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (baseDir is null) { return; }

        var sourceAssetsDir = Path.Combine(baseDir, "assets");
        if (!Directory.Exists(sourceAssetsDir)) { return; }

        var targetAssetsDir = Path.Combine(outDir, "assets");
        Directory.CreateDirectory(targetAssetsDir);

        foreach (var file in Directory.GetFiles(sourceAssetsDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceAssetsDir, file);
            var target = Path.Combine(targetAssetsDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
