using ArchSql.Analysis;
using ArchSql.Rendering;

namespace ArchSql.Site.Pages;

/// <summary>Schema drift since a baseline scan, when one was supplied. Reuses the same schema-diff
/// report the standalone diff verb produces. Empty (with guidance) when no baseline was given.</summary>
public static class DriftPage
{
    public static string Body(List<SchemaChange>? changes)
    {
        if (changes is null)
        {
            return """
<h1>Schema Diff</h1>
<div class="panel empty-state"><div class="big">◇</div>
<p>No baseline was supplied for this run. Pass --baseline &lt;model.json&gt; (a model.json from an
earlier scan or connection) to see what changed since then.</p>
</div>
""";
        }
        return DiffReport.Body(changes, new HashSet<string>(StringComparer.Ordinal));
    }
}
