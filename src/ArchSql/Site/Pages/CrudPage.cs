using System.Text;

namespace ArchSql.Site.Pages;

/// <summary>Object x actor CRUD projection: which procedures/triggers/views Create, Read, Update or
/// Delete each table. Answers "what writes to this table?" — the most common question when
/// debugging data issues.</summary>
public static class CrudPage
{
    public static string Body(SiteContext ctx)
    {
        var model = ctx.Model;
        var sb = new StringBuilder();
        sb.Append("<h1>CRUD Matrix</h1>");
        sb.Append("""
<p class="lede">Which procedures, triggers and views Create, Read, Update or Delete each table.
Answers "what writes to this table?" — the single most common question when debugging data issues.
Reads are R, inserts C, updates U, deletes D. Rows are targets; columns list the actors that touch
them.</p>
""");

        var entries = model.Crud.Where(e => !e.IsBlindSpot).ToList();
        var blindSpots = model.Crud.Where(e => e.IsBlindSpot).ToList();

        if (entries.Count == 0 && blindSpots.Count == 0)
        {
            sb.Append("""
<div class="panel empty-state"><div class="big">◇</div>
<p>No CRUD relationships were found. Once the scan parses procedures, views or triggers that read or
write tables, their operations appear here.</p>
</div>
""");
            return sb.ToString();
        }

        var byId = ctx.ById;
        sb.Append("""<input class="filter-input" type="search" data-filter-target="#crud-rows" placeholder="Filter by table or actor…" autocomplete="off" spellcheck="false"> <span class="filter-count"></span>""");

        if (entries.Count == 0)
        {
            sb.Append("""<div class="panel empty-state"><div class="big">◇</div><p>No resolvable CRUD relationships were found (see the analysis blind spots below).</p></div>""");
        }
        else
        {
            sb.Append("""<table class="grid"><tr><th>Table</th><th>Actor</th><th>Ops</th><th>File</th></tr><tbody id="crud-rows">""");
            foreach (var e in entries.OrderBy(e => e.Target, StringComparer.Ordinal).ThenBy(e => e.Actor, StringComparer.Ordinal))
            {
                var target = byId.GetValueOrDefault(e.Target);
                var actor = byId.GetValueOrDefault(e.Actor);
                var opsDisplay = Normalize(e.Ops);
                var search = $"{target?.Schema}.{target?.Name} {actor?.Schema}.{actor?.Name}".ToLowerInvariant();
                var actorFile = actor is null ? "" : $"""<a href="files/{Html.Encode(actor.DefinedInSlug)}.html">{Html.Encode(actor.DefinedInSlug)}</a>""";
                sb.Append($"""
<tr class="filterable" data-search="{Html.Encode(search)}">
<td>{(target is null ? Html.Encode(e.Target) : $"""<a href="files/{Html.Encode(target.DefinedInSlug)}.html">{Html.Encode(target.Schema)}.{Html.Encode(target.Name)}</a>""")}</td>
<td>{(actor is null ? Html.Encode(e.Actor) : $"{Html.Encode(actor.Schema)}.{Html.Encode(actor.Name)}")}</td>
<td><span class="badge">{Html.Encode(opsDisplay)}</span></td>
<td>{actorFile}</td>
</tr>
""");
            }
            sb.Append("</tbody></table>");
        }

        if (blindSpots.Count > 0)
        {
            sb.Append($"""
<p class="note">{blindSpots.Count} actor(s) build SQL dynamically (EXEC of a concatenated string).
Their targets can't be determined statically and are listed as analysis blind spots (?) below —
they are not missing, just unresolvable from the scripts.</p>
<ul class="diag-list">
""");
            foreach (var b in blindSpots)
            {
                var actor = byId.GetValueOrDefault(b.Actor);
                sb.Append($"""<li><span class="badge warn">?</span> {(actor is null ? Html.Encode(b.Actor) : $"{Html.Encode(actor.Schema)}.{Html.Encode(actor.Name)}")}</li>""");
            }
            sb.Append("</ul>");
        }

        sb.Append("""<p class="note">Only cleanly-parsed files contribute to this matrix.</p>""");
        return sb.ToString();
    }

    /// <summary>Normalizes the ops set (built as a sorted-by-char-value set, e.g. "CDRU") to the
    /// conventional CRUD display order.</summary>
    private static string Normalize(string ops)
    {
        const string order = "CRUD";
        return new string(order.Where(ops.Contains).ToArray());
    }
}
