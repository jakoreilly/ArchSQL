namespace ArchSql.Site.Pages;

/// <summary>Single client-rendered page for every object's detail view (object.html?id=...).
/// Renders one static shell; site.js fills it from the shared graph-data.js/object-detail.js
/// payloads based on the id in the query string. Avoids emitting one file per object — the shell
/// is written once regardless of how many objects the database has.</summary>
public static class ObjectPage
{
    public static string Body() => """
<div id="object-page">
<div class="panel empty-state" id="obj-notfound"><div class="big">◇</div>
<p><strong>Object not found.</strong> No object with that id is in this model. Use the search
(Ctrl K) or the <a href="objects.html">Objects</a> list to find one.</p>
</div>
<div id="obj-content" hidden>
  <h1 id="obj-title"></h1>
  <p class="lede" id="obj-purpose"></p>
  <p class="note"><a id="obj-source-link" href="#" hidden>View full source →</a>
  &nbsp;·&nbsp; <a id="obj-impact-link" href="#">Trace full impact →</a></p>

  <div class="tiles" id="obj-tiles"></div>

  <h2>Neighborhood</h2>
  <p class="lede">What connects to this object — one hop by default. Click a node to recenter on
  it; widen the hop count to expand the blast radius. Cascade foreign keys are drawn in red.</p>
  <div class="select-row">
    <label>Hops <input type="range" id="obj-hops" min="1" max="3" value="1"> <span id="obj-hops-val">1</span></label>
    <label>Direction
      <select id="obj-direction">
        <option value="both">Both</option>
        <option value="in">Incoming only</option>
        <option value="out">Outgoing only</option>
      </select>
    </label>
  </div>
  <div class="diagram-card" id="neighborhood-card" data-deferred data-png-name="neighborhood">
    <div class="toolbar">
      <button class="btn" data-act="zoom-in" type="button" title="Zoom in">+</button>
      <button class="btn" data-act="zoom-out" type="button" title="Zoom out">&minus;</button>
      <button class="btn" data-act="zoom-reset" type="button" title="Reset view">Reset</button>
      <button class="btn" data-act="fit" type="button" title="Fit diagram to the visible area">Fit</button>
      <button class="btn btn-primary" data-act="png" type="button" title="Download this diagram as a PNG image">⬇ PNG</button>
      <button class="btn" data-act="svg" type="button" title="Download this diagram as a scalable SVG">⬇ SVG</button>
      <span class="tb-hint">Scroll to zoom · drag to pan · click a node to recenter</span>
    </div>
    <div class="stage"><pre class="mermaid-src" hidden></pre><div class="render-target"></div></div>
  </div>

  <div class="two-col">
    <div>
      <h2>Reads/writes this (in)</h2>
      <ul class="member-list" id="obj-deps-in"></ul>
    </div>
    <div>
      <h2>This reads/writes (out)</h2>
      <ul class="member-list" id="obj-deps-out"></ul>
    </div>
  </div>

  <h2>Columns</h2>
  <div id="obj-columns-wrap"></div>

  <div id="obj-findings-wrap"></div>
</div>
</div>
""";
}
