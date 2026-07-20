using System.Text;

namespace ArchSql.Site.Pages;

/// <summary>Client-side query console over the object/dependency graph (window.ARCH_QUERY). The
/// engine already exists in site.js; this page supplies the markup, SQL-flavoured example queries,
/// and the query reference. Runs entirely in the browser — no network, works from file://.</summary>
public static class ExplorePage
{
    private static readonly (string Query, string Meaning)[] QueryReference =
    [
        ("referencedby: <name>", "objects that read or write <name> (procedures, views, triggers)"),
        ("reads: <name>", "objects <name> reads or writes (its outgoing references)"),
        ("affects: <name>", "everything transitively downstream of <name> — the full blast radius"),
        ("affectedby: <name>", "everything transitively upstream of <name>"),
        ("orphans", "objects nothing references — dead code candidates"),
        ("orphans in <schema>", "orphans restricted to one schema"),
        ("schema: <name>", "objects in exactly that schema"),
        ("kind: <table|view|procedure|function|trigger>", "objects of that kind (substring match)"),
        ("path: <a> <b>", "shortest dependency path from object a to object b"),
        ("loc/cog/fanin/fanout > N", "numeric filter — statement count, cyclomatic complexity, fan-in, fan-out"),
    ];

    public static string Body()
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Explore</h1>");
        sb.Append("""
<p class="lede">Ask the dependency graph questions — everything runs in your browser against the
model embedded in this page, no network. Try <code>referencedby: &lt;table&gt;</code> to see what
reads/writes it, <code>affects: &lt;object&gt;</code> for the full downstream blast radius, or
<code>orphans</code> for objects nothing references. Click any result to open it.</p>
""");

        sb.Append("""<div id="query-console">""");
        sb.Append("""<div class="select-row"><input class="filter-input" id="query-input" type="search" autocomplete="off" spellcheck="false" placeholder="e.g.  referencedby: Orders   ·   affects: Customer   ·   orphans   ·   cog > 20"><span class="filter-count" id="query-count"></span></div>""");

        sb.Append("""<div class="lang-legend" id="query-examples" style="gap:.4rem;margin:.2rem 0 .6rem">""");
        foreach (var ex in new[] { "referencedby: Orders", "reads: dbo.", "affects: Customer", "affectedby: Orders", "orphans", "kind: procedure", "schema: dbo", "cog > 20", "fanin > 5" })
        {
            sb.Append($"""<button type="button" class="btn query-example" style="padding:.15rem .5rem;font-size:.75rem">{Html.Encode(ex)}</button>""");
        }
        sb.Append("</div>");

        sb.Append("""<details class="legend"><summary>Query reference</summary><div class="legend-grid" style="flex-direction:column;gap:.3rem">""");
        foreach (var (q, meaning) in QueryReference)
        {
            sb.Append($"""<span class="legend-item"><code>{Html.Encode(q)}</code> — {Html.Encode(meaning)}</span>""");
        }
        sb.Append("""</div><p class="note" style="margin:.5rem 0 0">Name matches are case-insensitive substrings of "schema.name". Numeric fields: loc, cog, fanin, fanout — use &gt;, &gt;=, &lt;, &lt;=, or =.</p></details>""");

        sb.Append("""<ul class="member-list" id="query-results"></ul>""");
        sb.Append("</div>");
        return sb.ToString();
    }
}
