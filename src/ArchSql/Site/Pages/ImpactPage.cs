using System.Text;
using System.Text.Json;
using ArchSql.Analysis;

namespace ArchSql.Site.Pages;

/// <summary>"What breaks if I change X?" — pick an object, see its transitive dependents. The BFS
/// (ImpactGraph) runs once per object at generation time in C# (single-sourced, tested engine); the
/// page just looks up the precomputed result for the selected object. Static analysis can't see
/// dynamic SQL or application code, so results are a floor, not a ceiling.</summary>
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

        var reverse = ImpactGraph.BuildReverse(model);
        var byId = ctx.ById;
        // When a live connection supplied runtime facts, annotate each dependent with its real
        // execution count so "change this table -> N dependents, M of them hot" is visible.
        var execs = model.Runtime.Available
            ? model.Runtime.ObjectStats.ToDictionary(s => s.ObjectId, s => s.ExecutionCount, StringComparer.Ordinal)
            : null;
        var data = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var o in model.Objects)
        {
            var (hits, capped) = ImpactGraph.Dependents(reverse, o.Id);
            // name/kind/viaKind are HTML-encoded here (not at render time) because the JS below uses
            // innerHTML for simplicity; T-SQL bracketed identifiers can contain arbitrary characters.
            var rows = hits.Select(h =>
            {
                var obj = byId.GetValueOrDefault(h.ObjectId);
                var name = obj is null ? Html.Encode(h.ObjectId) : $"{Html.Encode(obj.Schema)}.{Html.Encode(obj.Name)}";
                var kind = Html.Encode(obj?.Kind ?? "");
                var href = obj is null ? "" : $"files/{obj.DefinedInSlug}.html";
                var exec = execs is not null && execs.TryGetValue(h.ObjectId, out var e) ? e : -1L;
                return new object[] { name, kind, h.Depth, Html.Encode(h.ViaKind), href, exec };
            }).ToList();
            data[o.Id] = new { hits = rows, capped };
        }

        sb.Append($"""<script>window.ARCH_IMPACT={JsonSerializer.Serialize(data)};window.ARCH_IMPACT_RUNTIME={(execs is not null ? "true" : "false")};</script>""");

        sb.Append("""<select id="impact-object" class="filter-input">""");
        sb.Append("""<option value="">Choose an object…</option>""");
        foreach (var o in model.Objects.OrderBy(o => o.Schema, StringComparer.OrdinalIgnoreCase).ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append($"""<option value="{Html.Encode(o.Id)}">{Html.Encode(o.Schema)}.{Html.Encode(o.Name)} ({Html.Encode(o.Kind)})</option>""");
        }
        sb.Append("</select>");

        sb.Append("""<div id="impact-results"></div>""");

        sb.Append("""
<script>
(function () {
  var sel = document.getElementById("impact-object");
  var out = document.getElementById("impact-results");
  if (!sel || !out) { return; }
  function render() {
    var id = sel.value;
    if (!id) { out.innerHTML = ""; return; }
    var data = (window.ARCH_IMPACT || {})[id];
    if (!data || data.hits.length === 0) {
      out.innerHTML = '<div class="panel empty-state"><div class="big">◇</div><p>Nothing in this scan depends on this object. It may still be used by application code or dynamic SQL outside these scripts.</p></div>';
      return;
    }
    var rt = window.ARCH_IMPACT_RUNTIME;
    var html = '<table class="grid"><tr><th>Object</th><th>Kind</th><th>Depth</th><th>Via</th>' + (rt ? '<th>Executions</th>' : '') + '</tr>';
    data.hits.forEach(function (h) {
      var name = h[4] ? '<a href="' + h[4] + '">' + h[0] + '</a>' : h[0];
      var viaBadge = h[3] === "fk-cascade" ? '<span class="badge warn">' + h[3] + '</span>' : h[3];
      var execCell = '';
      if (rt) {
        execCell = h[5] >= 0
          ? '<td>' + h[5].toLocaleString() + (h[5] > 0 ? ' <span class="badge accent">hot</span>' : '') + '</td>'
          : '<td>—</td>';
      }
      html += '<tr><td>' + name + '</td><td>' + h[1] + '</td><td>' + h[2] + '</td><td>' + viaBadge + '</td>' + execCell + '</tr>';
    });
    html += '</table>';
    if (data.capped) { html += '<p class="note">Traversal stopped at depth 32 (a dependency cycle or an unusually deep chain). Results above are complete to that depth.</p>'; }
    out.innerHTML = html;
  }
  sel.addEventListener("change", render);
})();
</script>
""");

        return sb.ToString();
    }
}
