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
        // Write the search index and graph/object-detail payloads once as shared assets; every
        // page references them by src (O(pages) bytes instead of inlining into each page).
        SearchIndex.WriteAsset(model, outDir);
        GraphData.WriteAsset(ctx, outDir);
        ObjectDetailData.WriteAsset(ctx, outDir);
        var searchIndexHtml = SearchIndex.ScriptSrc("");
        // graph-data.js/object-detail.js must load before assets/site.js (whose IIFEs read the
        // payload synchronously at parse time), so they are appended to the same pre-site.js slot.
        var graphPayloadScripts = searchIndexHtml + GraphData.ScriptSrc("") + ObjectDetailData.ScriptSrc("");

        WritePage(outDir, "index.html", "Overview", model, "index.html", "", PageTemplate.Crumbs((null, "Overview")), IndexPage.Body(ctx), searchIndexHtml);
        WritePage(outDir, "guide.html", "Guide", model, "guide.html", "", PageTemplate.Crumbs((null, "Guide")), GuidePage.Body(ctx), searchIndexHtml);
        WritePage(outDir, "explore.html", "Explore", model, "explore.html", "", PageTemplate.Crumbs((null, "Explore")), ExplorePage.Body(), graphPayloadScripts);
        WritePage(outDir, "objects.html", "Objects", model, "objects.html", "", PageTemplate.Crumbs((null, "Objects")), ObjectsPage.Body(ctx), searchIndexHtml);
        WritePage(outDir, "er.html", "ER Diagram", model, "er.html", "", PageTemplate.Crumbs((null, "ER Diagram")), ErPage.Body(ctx, maxNodes), searchIndexHtml);
        WritePage(outDir, "dependencies.html", "Dependencies", model, "dependencies.html", "", PageTemplate.Crumbs((null, "Dependencies")), DependenciesPage.Body(ctx, maxNodes), searchIndexHtml);
        WritePage(outDir, "crud.html", "CRUD Matrix", model, "crud.html", "", PageTemplate.Crumbs((null, "CRUD Matrix")), Pages.CrudPage.Body(ctx), searchIndexHtml);
        WritePage(outDir, "impact.html", "Impact", model, "impact.html", "", PageTemplate.Crumbs((null, "Impact")), Pages.ImpactPage.Body(ctx), searchIndexHtml);
        WritePage(outDir, "lint.html", "Lint", model, "lint.html", "", PageTemplate.Crumbs((null, "Lint")), LintPage.Body(ctx), searchIndexHtml);
        WritePage(outDir, "scorecard.html", "Scorecard", model, "scorecard.html", "", PageTemplate.Crumbs((null, "Scorecard")), ScorecardPage.Body(ctx), searchIndexHtml);
        WritePage(outDir, "metrics.html", "Metrics", model, "metrics.html", "", PageTemplate.Crumbs((null, "Metrics")), MetricsPage.Body(ctx), searchIndexHtml);
        WritePage(outDir, "activity.html", "Activity", model, "activity.html", "", PageTemplate.Crumbs((null, "Activity")), ActivityPage.Body(ctx), searchIndexHtml);
        WritePage(outDir, "config.html", "Config & Secrets", model, "config.html", "", PageTemplate.Crumbs((null, "Config & Secrets")), ConfigPage.Body(ctx), searchIndexHtml);
        WritePage(outDir, "object.html", "Object", model, "", "", PageTemplate.Crumbs(("objects.html", "Objects"), (null, "Object")), Pages.ObjectPage.Body(), graphPayloadScripts);

        Directory.CreateDirectory(Path.Combine(outDir, "files"));
        var fileSearchIndexHtml = SearchIndex.ScriptSrc("../");
        foreach (var file in model.Files)
        {
            var crumbs = PageTemplate.Crumbs(("../objects.html", "Objects"), (null, file.RelPath));
            var html = PageTemplate.Render(file.RelPath, model.RootName, "", "../", crumbs, ObjectFilePage.Body(ctx, file), fileSearchIndexHtml);
            File.WriteAllText(Path.Combine(outDir, "files", file.Slug + ".html"), html, Utf8NoBom);
        }

        ModelJsonWriter.Write(model, Path.Combine(outDir, "model.json"));

        return Path.Combine(outDir, "index.html");
    }

    private static void WritePage(string outDir, string fileName, string title, SqlModel model, string activeHref, string relRoot, string crumbs, string body, string searchIndexHtml)
    {
        var html = PageTemplate.Render(title, model.RootName, activeHref, relRoot, crumbs, body, searchIndexHtml);
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
