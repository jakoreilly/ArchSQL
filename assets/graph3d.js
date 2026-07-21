/* ArchSql 3D dependency graph controller.
   Renders the object/dependency graph (window.ARCH_QUERY — the same payload the Explore console and
   neighborhood diagrams use) with the vendored ForceGraph3D WebGL bundle. Colour by coupling /
   schema / kind; size by fan-in+fan-out; click a node to focus its ego set and fly the camera;
   search, hide-orphans, isolate, reset. Fully offline; degrades to an empty-state when WebGL or the
   bundle is unavailable. */
(function () {
  "use strict";
  var root = document.getElementById("graph3d-root");
  var canvas = document.getElementById("graph3d-canvas");
  if (!root || !canvas) { return; }

  function fail(msg) {
    root.innerHTML = '<div class="panel empty-state"><div class="big">🕸</div><p>' + msg +
      ' Use <a href="explore.html">Explore</a> or an object\'s <a href="objects.html">neighborhood</a> for the 2D views.</p></div>';
  }
  if (typeof ForceGraph3D !== "function") { fail("The 3D graph engine could not be loaded."); return; }
  try {
    var probe = document.createElement("canvas");
    if (!(probe.getContext("webgl") || probe.getContext("experimental-webgl"))) {
      fail("The 3D graph needs WebGL, which this browser/session doesn't provide."); return;
    }
  } catch (e) { fail("The 3D graph needs WebGL, which this browser/session doesn't provide."); return; }

  function cssVar(name) { return getComputedStyle(document.documentElement).getPropertyValue(name).trim(); }
  function isDark() { return document.documentElement.getAttribute("data-theme") === "dark"; }
  function esc(s) {
    return String(s == null ? "" : s).replace(/[&<>"']/g, function (c) {
      return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
    });
  }

  // Deterministic schema -> colour, and a fixed kind -> colour map.
  var palette = ["#2f6fab", "#b7791f", "#2e7d32", "#6b46c1", "#c0392b", "#1f8a8a", "#e8b73a", "#7a5195", "#ef5675", "#3572A5"];
  var schemaColor = {};
  function colorForSchema(f) {
    f = f || "";
    if (!(f in schemaColor)) {
      var h = 0; for (var i = 0; i < f.length; i++) { h = (h * 31 + f.charCodeAt(i)) & 0x7fffffff; }
      schemaColor[f] = palette[h % palette.length];
    }
    return schemaColor[f];
  }
  var KIND_COLORS = { table: "#2f6fab", view: "#b7791f", procedure: "#6b46c1", function: "#2e7d32", trigger: "#c0392b" };
  function colorForKind(k) { return KIND_COLORS[k] || "#8a8a8a"; }

  var hopsEl = document.getElementById("g3d-hops");
  var hopsValEl = document.getElementById("g3d-hops-val");
  var spreadEl = document.getElementById("g3d-spread");
  var spreadValEl = document.getElementById("g3d-spread-val");
  var colorEl = document.getElementById("g3d-color");
  var searchEl = document.getElementById("g3d-search");
  var searchListEl = document.getElementById("g3d-search-list");
  var isolateEl = document.getElementById("g3d-isolate");
  var hideOrphansEl = document.getElementById("g3d-hide-orphans");
  var resetEl = document.getElementById("g3d-reset");
  var countEl = document.getElementById("g3d-count");
  var panel = document.getElementById("graph3d-panel");

  var colorMode = "coupling";       // "coupling" | "schema" | "kind"
  var didInitialFit = false;
  var maxDegree = 1;
  function degreeOf(n) { return (n.fanIn || 0) + (n.fanOut || 0); }
  function isolateActive() { return !!(isolateEl && isolateEl.checked && focusId); }
  function inEgo(n) { return n.id === focusId || n.__ego; }

  var MUTED = function () { return cssVar("--text-soft") || "#888"; };
  var ACCENT = function () { return cssVar("--accent") || "#2f6fab"; };
  var Graph = null, DATA = { nodes: [], links: [] }, ADJ = {}, focusId = null, lastSig = null;

  function buildAdjacency(links) {
    ADJ = {};
    links.forEach(function (l) {
      var s = typeof l.source === "object" ? l.source.id : l.source;
      var t = typeof l.target === "object" ? l.target.id : l.target;
      (ADJ[s] = ADJ[s] || []).push(t); (ADJ[t] = ADJ[t] || []).push(s);
    });
  }
  function egoSet(startId, hops) {
    var dist = {}, queue = [startId]; dist[startId] = 0;
    while (queue.length) {
      var cur = queue.shift();
      if (dist[cur] >= hops) { continue; }
      (ADJ[cur] || []).forEach(function (n) { if (!(n in dist)) { dist[n] = dist[cur] + 1; queue.push(n); } });
    }
    return dist;
  }
  function colorForCoupling(n) {
    var t = maxDegree > 0 ? Math.min(1, degreeOf(n) / maxDegree) : 0;
    var cool = [47, 111, 171], hot = [192, 57, 43];
    var c = cool.map(function (v, i) { return Math.round(v + (hot[i] - v) * t); });
    return "#" + c.map(function (v) { return ("0" + v.toString(16)).slice(-2); }).join("");
  }
  function baseColor(n) {
    return colorMode === "coupling" ? colorForCoupling(n)
         : colorMode === "kind" ? colorForKind(n.lang)
         : colorForSchema(n.folder);
  }
  function nodeColor(n) {
    if (focusId) { if (n.id === focusId) { return ACCENT(); } return n.__ego ? baseColor(n) : MUTED(); }
    return baseColor(n);
  }
  function nodeVal(n) {
    var base = 1 + degreeOf(n);
    if (focusId && n.id === focusId) { return base * 2.2; }
    if (focusId && n.__ego) { return base * 1.3; }
    return base;
  }
  function linkColor(l) {
    var k = l.kind || "";
    if (k === "fk-cascade") { return cssVar("--danger") || "#c0392b"; }
    if (k === "insert" || k === "update" || k === "delete") { return cssVar("--warn") || "#b7791f"; }
    return ACCENT();
  }
  function applyChannels() {
    if (!Graph) { return; }
    Graph.nodeColor(nodeColor).nodeVal(nodeVal).linkColor(linkColor);
  }

  function applyGraphData() {
    if (!Graph) { return; }
    var iso = isolateActive();
    var hideOrphans = !!(hideOrphansEl && hideOrphansEl.checked);
    var nodes = DATA.nodes.filter(function (n) {
      if (iso && !inEgo(n)) { return false; }
      if (hideOrphans && degreeOf(n) === 0) { return false; }
      return true;
    });
    var live = {}; nodes.forEach(function (n) { live[n.id] = 1; });
    var links = (DATA.links || []).filter(function (l) {
      var s = typeof l.source === "object" ? l.source.id : l.source;
      var t = typeof l.target === "object" ? l.target.id : l.target;
      return live[s] && live[t];
    });
    var sig = nodes.length + "|" + links.length + "|" + nodes.map(function (n) { return n.id; }).join(",");
    if (sig === lastSig) { applyChannels(); return; }
    lastSig = sig;
    Graph.graphData({ nodes: nodes, links: links });
    buildAdjacency(DATA.links);
    applyChannels();
  }

  function setFocus(id) {
    focusId = id;
    var node = DATA.nodes.find(function (n) { return n.id === id; });
    if (!node) { return; }
    var hops = +(hopsEl && hopsEl.value || 1);
    var dist = egoSet(id, hops);
    DATA.nodes.forEach(function (n) { n.__ego = (n.id in dist) && n.id !== id; });
    if (isolateActive()) { applyGraphData(); } else { applyChannels(); }
    var p = node, d = 120;
    if (typeof p.x === "number") {
      var dist0 = Math.hypot(p.x, p.y, p.z || 0), ratio = dist0 > 1 ? 1 + d / dist0 : 1;
      Graph.cameraPosition({ x: p.x * ratio, y: p.y * ratio, z: (p.z || 0) * ratio + d }, { x: p.x, y: p.y, z: p.z || 0 }, 800);
    }
    showPanel(node);
  }
  function clearFocus() {
    var wasIso = isolateActive();
    focusId = null;
    DATA.nodes.forEach(function (n) { n.__ego = false; });
    if (wasIso) { applyGraphData(); } else { applyChannels(); }
    if (panel) { panel.hidden = true; }
  }

  function showPanel(node) {
    if (!panel) { return; }
    var byId = {}; DATA.nodes.forEach(function (n) { byId[n.id] = n; });
    var neighbours = (ADJ[node.id] || []).filter(function (v, i, a) { return a.indexOf(v) === i; });
    var items = neighbours.slice(0, 50).map(function (nid) {
      var nn = byId[nid];
      return '<li data-id="' + esc(nid) + '">' + esc(nn ? nn.path : nid) + '</li>';
    }).join("");
    panel.hidden = false;
    panel.innerHTML =
      '<div style="display:flex;justify-content:space-between;align-items:baseline;gap:.5rem">' +
      '<strong style="font-size:.95rem">' + esc(node.path) + '</strong>' +
      '<button class="btn" id="g3d-panel-close" type="button" title="Close">✕</button></div>' +
      '<p style="margin:.4rem 0"><span class="badge">' + esc(node.lang) + '</span> ' +
      '<span class="badge">in ' + (node.fanIn || 0) + '</span> <span class="badge">out ' + (node.fanOut || 0) + '</span>' +
      (node.execs >= 0 ? ' <span class="badge accent">' + node.execs.toLocaleString() + ' execs</span>' : '') + '</p>' +
      '<p style="margin:.5rem 0;display:flex;gap:.4rem;flex-wrap:wrap">' +
      '<a class="btn" href="' + node.href + '">Open object →</a>' +
      '<a class="btn" href="impact.html?id=' + encodeURIComponent(node.id) + '">Trace impact →</a></p>' +
      '<p class="note" style="margin:.4rem 0 .2rem">Neighbours (' + neighbours.length + ') — click to refocus</p>' +
      '<ul class="graph3d-neighbours">' + items + '</ul>';
    panel.querySelector("#g3d-panel-close").onclick = clearFocus;
    panel.querySelectorAll(".graph3d-neighbours li").forEach(function (li) {
      li.onclick = function () { setFocus(li.getAttribute("data-id")); };
    });
  }

  function fitView(ms) {
    if (!Graph || !Graph.zoomToFit || !DATA.nodes.length) { return; }
    var n = DATA.nodes[0];
    if (typeof n.x !== "number" || !isFinite(n.x)) { return; }
    var connected = DATA.nodes.some(function (d) { return degreeOf(d) > 0; });
    var filter = connected ? function (d) { return degreeOf(d) > 0; } : function () { return true; };
    try { Graph.zoomToFit(ms == null ? 400 : ms, 60, filter); } catch (e) { /* engine not ready */ }
  }
  function defaultSpread(count) { return count < 50 ? 2 : count < 200 ? 3 : count < 600 ? 4 : 5; }
  function applySpread(reheat) {
    if (!Graph || !Graph.d3Force) { return; }
    var f = spreadEl ? +spreadEl.value : 3;
    var charge = Graph.d3Force("charge"); if (charge && charge.strength) { charge.strength(-40 - f * 8); }
    var link = Graph.d3Force("link"); if (link && link.distance) { link.distance(20 + f * 16); }
    if (reheat && Graph.d3ReheatSimulation) { Graph.d3ReheatSimulation(); }
  }

  var SEARCH_CAP = 1000;
  function populateSearchList() {
    if (!searchListEl) { return; }
    var n = Math.min(DATA.nodes.length, SEARCH_CAP), html = "";
    for (var i = 0; i < n; i++) { html += '<option value="' + esc(DATA.nodes[i].path) + '">'; }
    searchListEl.innerHTML = html;
  }
  function findNode(q) {
    q = (q || "").trim(); if (!q) { return null; }
    var exact = DATA.nodes.find(function (n) { return n.path === q; });
    if (exact) { return exact; }
    var lq = q.toLowerCase();
    return DATA.nodes.find(function (n) { return (n.path && n.path.toLowerCase().indexOf(lq) >= 0); }) || null;
  }
  function runSearch() {
    if (!searchEl) { return; }
    var node = findNode(searchEl.value);
    if (!node) { searchEl.setAttribute("aria-invalid", "true"); return; }
    searchEl.removeAttribute("aria-invalid");
    setFocus(node.id);
  }
  function applyDeepLink() {
    var h = (location.hash || "").replace(/^#/, "");
    var m = /(?:^|&)(?:id|focus)=([^&]+)/.exec(h);
    if (!m) { return; }
    var val = decodeURIComponent(m[1]);
    var node = DATA.nodes.find(function (n) { return n.id === val || n.path === val; });
    if (node) { setTimeout(function () { setFocus(node.id); }, 700); }
  }

  function init(data) {
    DATA = { nodes: (data.nodes || []).map(function (n) { return Object.assign({}, n); }), links: (data.edges || data.links || []).slice() };
    buildAdjacency(DATA.links);
    maxDegree = DATA.nodes.reduce(function (m, n) { return Math.max(m, degreeOf(n)); }, 1);
    if (spreadEl) { spreadEl.value = defaultSpread(DATA.nodes.length); if (spreadValEl) { spreadValEl.textContent = spreadEl.value; } }
    if (colorEl) { colorMode = colorEl.value || "coupling"; }
    if (countEl) { countEl.textContent = DATA.nodes.length.toLocaleString() + " objects · " + DATA.links.length.toLocaleString() + " edges"; }
    var loading = document.createElement("div");
    loading.className = "note"; loading.textContent = "Laying out " + DATA.nodes.length + " objects…";
    loading.style.cssText = "position:absolute;top:.75rem;left:.75rem;z-index:5";
    root.appendChild(loading);

    Graph = ForceGraph3D()(canvas)
      .backgroundColor(cssVar("--diagram-bg") || (isDark() ? "#151a21" : "#ffffff"))
      .nodeLabel(function (n) { return n.path + " (" + n.lang + ")"; })
      .nodeOpacity(0.95).linkOpacity(0.5)
      .linkDirectionalArrowLength(3).linkDirectionalArrowRelPos(1)
      .warmupTicks(Math.min(120, 20 + Math.round((DATA.nodes.length || 0) / 4)))
      .cooldownTicks(60).cooldownTime(4000)
      .onNodeClick(function (n) { setFocus(n.id); })
      .onBackgroundClick(clearFocus)
      .graphData(DATA);

    populateSearchList();
    lastSig = null;
    applyGraphData();
    applySpread(false);
    applyDeepLink();

    Graph.onEngineStop(function () {
      if (loading.parentNode) { loading.parentNode.removeChild(loading); }
      if (!didInitialFit && !focusId) { fitView(500); didInitialFit = true; }
    });
    function resize() { Graph.width(canvas.clientWidth).height(canvas.clientHeight); }
    resize();
    window.addEventListener("resize", resize);
    setTimeout(function () { if (loading.parentNode) { loading.parentNode.removeChild(loading); } }, 8000);
  }

  // ---- Controls ----
  if (hopsEl) { hopsEl.addEventListener("input", function () { if (hopsValEl) { hopsValEl.textContent = hopsEl.value; } if (focusId) { setFocus(focusId); } }); }
  if (spreadEl) { spreadEl.addEventListener("input", function () { if (spreadValEl) { spreadValEl.textContent = spreadEl.value; } applySpread(true); }); }
  if (colorEl) { colorEl.addEventListener("change", function () { colorMode = colorEl.value; applyChannels(); }); }
  if (isolateEl) { isolateEl.addEventListener("change", applyGraphData); }
  if (hideOrphansEl) { hideOrphansEl.addEventListener("change", applyGraphData); }
  if (searchEl) {
    searchEl.addEventListener("change", runSearch);
    searchEl.addEventListener("keydown", function (e) { if (e.key === "Enter") { e.preventDefault(); runSearch(); } });
  }
  if (resetEl) { resetEl.onclick = function () { clearFocus(); fitView(500); }; }
  document.addEventListener("keydown", function (e) { if (e.key === "Escape" && focusId) { clearFocus(); } });

  new MutationObserver(function () {
    if (Graph) { Graph.backgroundColor(cssVar("--diagram-bg") || (isDark() ? "#151a21" : "#ffffff")); applyChannels(); }
  }).observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });

  var payload = window.ARCH_QUERY || window.ARCH_GRAPH;
  if (payload) { init(payload); }
  else { fetch("graph.json").then(function (r) { return r.json(); }).then(init).catch(function () { fail("Could not load the graph data."); }); }
})();
