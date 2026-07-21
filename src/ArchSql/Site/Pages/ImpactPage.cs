using System.Text;
using System.Text.Json;
using ArchSql.Analysis;

namespace ArchSql.Site.Pages;

/// <summary>"What breaks if I change X?" — pick an object, see its transitive dependents. The
/// reverse dependency graph is emitted once as a compact adjacency map and the breadth-first
/// traversal runs in the browser on selection, so the page stays small and fast regardless of how
/// many objects the database has (precomputing every object's dependents server-side does not scale
/// to enterprise schemas with tens of thousands of objects). Static analysis can't see dynamic SQL
/// or application code, so results are a floor, not a ceiling.</summary>
public static class ImpactPage
{
    public static string Body(SiteContext ctx)
    {
        var model = ctx.Model;
        var sb = new StringBuilder();
        sb.Append("<h1>Impact</h1>");
        sb.Append("""
<p class="lede">Pick an object to see everything that could break if you change or drop it — the
transitive set of dependents: procedures that read/write it, views built on it, and foreign-key
children (cascades highlighted). Static analysis can't see dynamic SQL or application code, so treat
this as a floor, not a ceiling.</p>
""");

        if (model.Objects.Count == 0)
        {
            sb.Append("""<div class="panel empty-state"><div class="big">◇</div><p>No schema objects were found, so there is nothing to trace impact for.</p></div>""");
            return sb.ToString();
        }

        // Reverse adjacency (dependents-of), emitted once. Keys/values are ids; the browser does the
        // BFS. Values are [fromId, kind] pairs.
        var reverse = ImpactGraph.BuildReverse(model);
        var revJson = reverse.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Select(e => new[] { e.From, e.Kind }).ToArray(),
            StringComparer.Ordinal);

        // Per-object display metadata: label, kind, href, execution count (-1 = no runtime data).
        // Strings are HTML-encoded here because the client renders them with innerHTML.
        var execs = model.Runtime.Available
            ? model.Runtime.ObjectStats.GroupBy(s => s.ObjectId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().ExecutionCount, StringComparer.Ordinal)
            : null;
        var meta = model.Objects.ToDictionary(
            o => o.Id,
            o => new object[]
            {
                $"{Html.Encode(o.Schema)}.{Html.Encode(o.Name)}",
                Html.Encode(o.Kind),
                $"object.html?id={Uri.EscapeDataString(o.Id)}",
                execs is not null && execs.TryGetValue(o.Id, out var e) ? e : -1L,
            },
            StringComparer.Ordinal);

        sb.Append($"""<script>window.ARCH_REV={JsonSerializer.Serialize(revJson)};window.ARCH_META={JsonSerializer.Serialize(meta)};window.ARCH_IMPACT_RUNTIME={(execs is not null ? "true" : "false")};window.ARCH_MAXDEPTH={ImpactGraph.MaxDepth};</script>""");

        // Type-ahead search instead of a full <select> — a database with thousands of objects made
        // the dropdown itself slow to open and scroll. Accepts ?id= to preselect (e.g. a "Trace full
        // impact" link from object.html).
        sb.Append("""
<div class="select-row">
  <input class="filter-input" id="impact-search" type="search" autocomplete="off" spellcheck="false" placeholder="Search for an object…">
  <span class="filter-count" id="impact-search-count"></span>
</div>
<ul class="palette-results" id="impact-search-results" style="position:static;max-height:220px;overflow:auto"></ul>
<div id="impact-selected"></div>
<div id="impact-results"></div>
""");

        sb.Append("""
<script>
(function () {
  var input = document.getElementById("impact-search");
  var resultsList = document.getElementById("impact-search-results");
  var countEl = document.getElementById("impact-search-count");
  var selectedEl = document.getElementById("impact-selected");
  var out = document.getElementById("impact-results");
  if (!input || !out) { return; }
  var rev = window.ARCH_REV || {}, meta = window.ARCH_META || {};
  var rt = window.ARCH_IMPACT_RUNTIME, maxDepth = window.ARCH_MAXDEPTH || 32;
  var ids = Object.keys(meta);

  function dependents(rootId) {
    var visited = {}; visited[rootId] = true;
    var hits = [], capped = false;
    var queue = [[rootId, 0, ""]];
    while (queue.length) {
      var cur = queue.shift(), id = cur[0], depth = cur[1], via = cur[2];
      if (depth > 0) { hits.push({ id: id, depth: depth, via: via }); }
      if (depth >= maxDepth) { capped = true; continue; }
      var edges = rev[id] || [];
      for (var i = 0; i < edges.length; i++) {
        var from = edges[i][0];
        if (!visited[from]) { visited[from] = true; queue.push([from, depth + 1, edges[i][1]]); }
      }
    }
    // Within a depth band, order by real execution count (hottest first) when runtime data is
    // present, so the dependents most likely to matter surface at the top of each band.
    function execOf(id) { var m = meta[id]; return m && m[3] >= 0 ? m[3] : -1; }
    hits.sort(function (a, b) {
      if (a.depth !== b.depth) { return a.depth - b.depth; }
      var ea = execOf(a.id), eb = execOf(b.id);
      if (rt && ea !== eb) { return eb - ea; }
      return a.id < b.id ? -1 : a.id > b.id ? 1 : 0;
    });
    return { hits: hits, capped: capped };
  }

  function select(id) {
    var m = meta[id];
    if (!m) { return; }
    history.replaceState(null, "", "impact.html?id=" + encodeURIComponent(id));
    input.value = m[0];
    resultsList.innerHTML = ""; countEl.textContent = "";
    selectedEl.innerHTML = '<p class="lede">Tracing impact of <strong>' + m[0] + '</strong> ('
      + m[1] + ') · <a href="object.html?id=' + encodeURIComponent(id) + '">View neighborhood →</a></p>';
    render(id);
  }

  function searchResults() {
    var q = input.value.trim().toLowerCase();
    resultsList.innerHTML = "";
    if (!q) { countEl.textContent = ""; return; }
    var hits = ids.filter(function (id) { return meta[id][0].toLowerCase().indexOf(q) >= 0; }).slice(0, 20);
    countEl.textContent = hits.length + " match" + (hits.length === 1 ? "" : "es");
    hits.forEach(function (id) {
      var li = document.createElement("li");
      var a = document.createElement("a");
      a.href = "#"; a.textContent = meta[id][0];
      a.addEventListener("click", function (e) { e.preventDefault(); select(id); });
      li.appendChild(a);
      var kind = document.createElement("span");
      kind.className = "palette-detail"; kind.textContent = meta[id][1];
      li.appendChild(kind);
      resultsList.appendChild(li);
    });
  }

  function render(id) {
    var res = dependents(id);
    if (res.hits.length === 0) {
      out.innerHTML = '<div class="panel empty-state"><div class="big">◇</div><p>Nothing in this scan depends on this object. It may still be used by application code or dynamic SQL outside these scripts.</p></div>';
      return;
    }
    var hotCount = rt ? res.hits.filter(function (h) { var m = meta[h.id]; return m && m[3] > 0; }).length : 0;
    var html = '<p class="note">' + res.hits.length + ' dependent object(s)'
      + (rt ? ', ' + hotCount + ' with recorded execution (hot).' : '.') + '</p>';
    html += '<table class="grid"><tr><th>Object</th><th>Kind</th><th>Depth</th><th>Via</th>' + (rt ? '<th>Executions</th>' : '') + '</tr>';
    res.hits.forEach(function (h) {
      var m = meta[h.id] || [h.id, "", "", -1];
      var name = m[2] ? '<a href="' + m[2] + '">' + m[0] + '</a>' : m[0];
      var viaBadge = h.via === "fk-cascade" ? '<span class="badge warn">' + h.via + '</span>' : h.via;
      var execCell = "";
      if (rt) { execCell = m[3] >= 0 ? '<td>' + m[3].toLocaleString() + (m[3] > 0 ? ' <span class="badge accent">hot</span>' : '') + '</td>' : '<td>—</td>'; }
      html += '<tr><td>' + name + '</td><td>' + m[1] + '</td><td>' + h.depth + '</td><td>' + viaBadge + '</td>' + execCell + '</tr>';
    });
    html += '</table>';
    if (res.capped) { html += '<p class="note">Traversal stopped at depth ' + maxDepth + ' (a dependency cycle or an unusually deep chain). Results above are complete to that depth.</p>'; }
    out.innerHTML = html;
  }

  input.addEventListener("input", searchResults);
  input.addEventListener("focus", searchResults);

  var m = /[?&]id=([^&]+)/.exec(window.location.search);
  if (m && meta[decodeURIComponent(m[1])]) { select(decodeURIComponent(m[1])); }
})();
</script>
""");
        return sb.ToString();
    }
}
