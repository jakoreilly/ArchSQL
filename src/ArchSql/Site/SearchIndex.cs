using System.Text.Json;
using ArchSql.Model;

namespace ArchSql.Site;

/// <summary>Builds the [kind,name,detail,href] tuple array the Ctrl+K palette (site.js) reads from
/// window.ARCH_SEARCH_INDEX. Written ONCE to assets/search-index.js and referenced by every page
/// via a &lt;script src&gt; tag — a local script src works under file:// (only fetch of local JSON
/// is blocked), so inlining the whole index into every page (O(pages x objects) bytes, which does
/// not scale to enterprise schemas) is unnecessary. Deterministic: objects then files, each sorted.</summary>
public static class SearchIndex
{
    /// <summary>Writes assets/search-index.js. Call once per site, after the asset tree is copied.</summary>
    public static void WriteAsset(SqlModel model, string outDir)
    {
        var rows = new List<string[]>();
        foreach (var o in model.Objects.OrderBy(o => o.Id, StringComparer.Ordinal))
        {
            rows.Add(["object", $"{o.Schema}.{o.Name}", o.Kind, $"files/{o.DefinedInSlug}.html"]);
        }
        foreach (var f in model.Files.OrderBy(f => f.Slug, StringComparer.Ordinal))
        {
            rows.Add(["file", f.RelPath, "file", $"files/{f.Slug}.html"]);
        }
        var json = JsonSerializer.Serialize(rows);
        var assetsDir = Path.Combine(outDir, "assets");
        Directory.CreateDirectory(assetsDir);
        File.WriteAllText(Path.Combine(assetsDir, "search-index.js"), $"window.ARCH_SEARCH_INDEX={json};");
    }

    /// <summary>The &lt;script src&gt; tag pointing at the shared index, relative to the page.</summary>
    public static string ScriptSrc(string relRoot) => $"<script src=\"{relRoot}assets/search-index.js\"></script>";
}
