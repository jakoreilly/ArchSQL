using System.Text.Json;
using ArchSql.Model;

namespace ArchSql.Site;

/// <summary>Builds the [kind,name,detail,href] tuple array the Ctrl+K palette (site.js) reads from
/// window.ARCH_SEARCH_INDEX. Emitted once per page, inline, so it works on file:// (fetch of local
/// JSON is blocked in Chrome; inline script is not). Deterministic: objects then files, each
/// sorted.</summary>
public static class SearchIndex
{
    public static string ScriptTag(SqlModel model)
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
        return $"<script>window.ARCH_SEARCH_INDEX={json};</script>";
    }
}
