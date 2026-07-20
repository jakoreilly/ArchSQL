using System.Text;

namespace ArchSql.Site;

public static class Html
{
    public static string Encode(string s) => System.Net.WebUtility.HtmlEncode(s);
}

/// <summary>Shared page shell: sidebar navigation, breadcrumbs, theme toggle, local asset
/// references only (works from file:// with no network). Copies ArchDiagram's PageTemplate
/// verbatim — same theme localStorage keys, same DOM ids, so the vendored site.js/site.css work
/// unmodified.</summary>
public static class PageTemplate
{
    public static readonly (string Section, (string Href, string Title, string Icon)[] Items)[] NavSections =
    [
        ("Start", [("index.html", "Overview", "◈"), ("guide.html", "Guide", "❓")]),
        ("Schema", [("objects.html", "Objects", "❖"), ("er.html", "ER Diagram", "⬡"), ("dependencies.html", "Dependencies", "⇄"), ("crud.html", "CRUD Matrix", "▦")]),
        ("Health", [("lint.html", "Lint", "◉"), ("scorecard.html", "Scorecard", "✔"), ("metrics.html", "Metrics", "📐"), ("impact.html", "Impact", "☢"), ("activity.html", "Activity", "🔥")]),
        ("Reference", [("config.html", "Config & Secrets", "🔑")]),
    ];

    /// <param name="relRoot">"" for root pages, "../" for pages under files/.</param>
    /// <param name="searchIndexHtml">The Ctrl+K palette's window.ARCH_SEARCH_INDEX script tag
    /// (SearchIndex.ScriptTag); "" disables search on this page (kept optional so every existing
    /// caller keeps compiling).</param>
    public static string Render(string title, string siteName, string activeHref, string relRoot, string breadcrumbsHtml, string bodyHtml, string searchIndexHtml = "")
    {
        var nav = new StringBuilder();
        foreach (var (section, items) in NavSections)
        {
            nav.Append($"<div class=\"nav-section\">{Html.Encode(section)}</div>\n");
            foreach (var (href, navTitle, icon) in items)
            {
                var active = href == activeHref ? " class=\"active\"" : "";
                nav.Append($"<a href=\"{relRoot}{href}\"{active}><span class=\"nav-icon\">{icon}</span>{Html.Encode(navTitle)}</a>\n");
            }
        }

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{{Html.Encode(title)}} — {{Html.Encode(siteName)}}</title>
<link rel="stylesheet" href="{{relRoot}}assets/site.css">
<script>
(function () {
  var t = null;
  try { t = localStorage.getItem("archdiagram-theme"); } catch (e) { }
  if (!t) { t = window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light"; }
  document.documentElement.setAttribute("data-theme", t);
})();
</script>
</head>
<body>
<div class="layout">
  <button class="nav-toggle" id="nav-toggle" type="button" aria-label="Open menu" aria-expanded="false">☰</button>
  <div class="nav-overlay" id="nav-overlay" hidden></div>
  <aside class="sidebar" id="sidebar">
    <div class="brand"><span class="brand-mark">◆</span><div><div class="brand-name">ArchSql</div><div class="brand-sub">{{Html.Encode(siteName)}}</div></div></div>
    <button class="btn search-open" id="search-open" type="button" title="Search objects (Ctrl+K)">🔍 Search <kbd>Ctrl K</kbd></button>
    <nav>
{{nav}}    </nav>
    <div class="sidebar-foot">
      <button class="btn theme-toggle" id="theme-toggle" type="button" title="Switch between light and dark theme">◐ Theme</button>
    </div>
  </aside>
  <main class="content">
    <div class="breadcrumbs">{{breadcrumbsHtml}}</div>
{{bodyHtml}}
  </main>
</div>
<div class="hover-tip" id="hover-tip" hidden></div>
<div class="explain-pop" id="explain-pop" hidden role="dialog" aria-label="Explanation"></div>
<script type="application/json" id="arch-glossary">{{Glossary.Json()}}</script>
<div class="palette-overlay" id="palette" hidden data-rel-root="{{relRoot}}">
  <div class="palette">
    <input type="text" id="palette-input" placeholder="Search objects…" autocomplete="off" spellcheck="false">
    <ul class="palette-results" id="palette-results"></ul>
    <div class="palette-foot">↑↓ navigate · Enter open · Esc close</div>
  </div>
</div>
{{searchIndexHtml}}
<script src="{{relRoot}}assets/lib/mermaid.min.js"></script>
<script src="{{relRoot}}assets/site.js"></script>
</body>
</html>
""";
    }

    /// <summary>One interactive diagram card: toolbar (zoom/reset/PNG), pan/zoom stage, the
    /// mermaid source. Copies ArchDiagram's DiagramBlock (adjacency/tooltips omitted — v1 has
    /// no 3D graph or hover-trace data to feed them).</summary>
    public static string DiagramBlock(string id, string mermaidSource)
    {
        return $"""
<div class="diagram-card" id="{Html.Encode(id)}" data-png-name="{Html.Encode(id)}">
  <div class="toolbar">
    <button class="btn" data-act="zoom-in" type="button" title="Zoom in">+</button>
    <button class="btn" data-act="zoom-out" type="button" title="Zoom out">&minus;</button>
    <button class="btn" data-act="zoom-reset" type="button" title="Reset view">Reset</button>
    <button class="btn" data-act="fit" type="button" title="Fit diagram to the visible area">Fit</button>
    <button class="btn btn-primary" data-act="png" type="button" title="Download this diagram as a PNG image">⬇ PNG</button>
    <button class="btn" data-act="svg" type="button" title="Download this diagram as a scalable SVG">⬇ SVG</button>
    <button class="btn" data-act="copy" type="button" title="Copy the Mermaid source of this diagram to the clipboard">Copy Mermaid</button>
    <span class="tb-hint">Scroll to zoom · drag to pan · click a node to open it</span>
  </div>
  <div class="stage"><pre class="mermaid-src" hidden>{Html.Encode(mermaidSource)}</pre><div class="render-target"></div></div>
</div>
""";
    }

    public static string Legend() => """
<details class="legend"><summary>What the shapes and colours mean</summary>
<div class="legend-grid">
  <span class="legend-item"><span class="legend-swatch" style="background:#dcecf9;border-color:#2f6fab"></span>Table</span>
  <span class="legend-item"><span class="legend-swatch round" style="background:#fdf1dc;border-color:#b7791f"></span>View</span>
  <span class="legend-item"><span class="legend-swatch hex" style="background:#f0f0f0;border-color:#8a8a8a"></span>Procedure / function</span>
  <span class="legend-item"><span class="legend-line"></span>Foreign key / reference</span>
  <span class="legend-item"><span class="legend-line dashed"></span>Unresolved / external reference</span>
</div>
</details>
""";

    public static string Crumbs(params (string? Href, string Text)[] parts)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0) { sb.Append(" <span class=\"crumb-sep\">/</span> "); }
            var (href, text) = parts[i];
            sb.Append(href is null
                ? $"<span class=\"crumb-here\">{Html.Encode(text)}</span>"
                : $"<a href=\"{href}\">{Html.Encode(text)}</a>");
        }
        return sb.ToString();
    }
}
