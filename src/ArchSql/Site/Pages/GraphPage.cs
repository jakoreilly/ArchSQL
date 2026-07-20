namespace ArchSql.Site.Pages;

/// <summary>Interactive 3D dependency graph: the whole object/dependency model rendered with the
/// vendored ForceGraph3D WebGL bundle, driven client-side by graph3d.js against the shared
/// window.ARCH_QUERY payload. Controls (colour, spread, hops, search, isolate, hide-orphans) and
/// the focus side-panel are wired in graph3d.js; this page supplies the shell. The lib + controller
/// scripts are appended by SiteGenerator after the payload so they load in order.</summary>
public static class GraphPage
{
    public static string Body(SiteContext ctx)
    {
        if (ctx.Model.Objects.Count == 0)
        {
            return """
<h1>3D Graph</h1>
<div class="panel empty-state"><div class="big">🕸</div><p>No schema objects were found, so there is
nothing to plot. See <a href="objects.html">Objects</a>.</p></div>
""";
        }

        return """
<h1>3D Graph</h1>
<p class="lede">The whole schema as an interactive 3D force graph. Drag to orbit, scroll to zoom,
click a node to focus its neighbourhood and fly to it (Esc or background click clears). Node size is
fan-in + fan-out; colour is coupling by default. Search or press a node to open it, trace its impact,
or hop to a neighbour. Everything runs in your browser — nothing is fetched.</p>

<div class="select-row" id="graph3d-controls">
  <label class="lf-select">Colour
    <select id="g3d-color">
      <option value="coupling">Coupling (heat)</option>
      <option value="schema">Schema</option>
      <option value="kind">Kind</option>
    </select>
  </label>
  <label class="lf-range">Focus hops <input type="range" id="g3d-hops" min="1" max="4" value="1"> <span id="g3d-hops-val">1</span></label>
  <label class="lf-range">Spread <input type="range" id="g3d-spread" min="1" max="12" value="5"> <span id="g3d-spread-val">5</span></label>
  <label class="lf-check"><input type="checkbox" id="g3d-hide-orphans"> Hide unconnected</label>
  <label class="lf-check"><input type="checkbox" id="g3d-isolate"> Isolate focus</label>
  <input class="lf-search filter-input" id="g3d-search" type="search" list="g3d-search-list" placeholder="Search object… (Enter)" autocomplete="off">
  <datalist id="g3d-search-list"></datalist>
  <button class="btn" id="g3d-reset" type="button">Reset view</button>
  <span class="filter-count" id="g3d-count"></span>
</div>

<div id="graph3d-root" class="panel" style="padding:0;position:relative">
  <div id="graph3d-canvas" class="graph3d-canvas"></div>
  <aside id="graph3d-panel" class="graph3d-panel" hidden></aside>
</div>
<p class="note">Colour legend — Coupling: blue (low) → red (high fan-in+out). Kind: table blue, view
amber, procedure purple, function green, trigger red. Edges: writes amber, cascade FKs red, other
references blue. Large graphs settle over a few seconds; use "Hide unconnected" to cut noise.</p>
""";
    }
}
