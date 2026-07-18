using System.Text;

namespace ArchSql.Site.Pages;

public static class ConfigPage
{
    public static string Body(SiteContext ctx)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Config &amp; Secrets</h1>");
        sb.Append("""<p class="lede">Files that embed a credential in DDL — the fact and location only, never the secret value.</p>""");

        var withCred = ctx.Model.Files.Where(f => f.HasCredential).ToList();
        if (withCred.Count == 0)
        {
            sb.Append("""<div class="panel empty-state"><div class="big">✓</div><p>No embedded credentials were found in this scan.</p></div>""");
            return sb.ToString();
        }

        sb.Append("""<table class="grid"><tr><th>File</th><th>Finding</th></tr>""");
        foreach (var f in withCred)
        {
            sb.Append($"""<tr><td><a href="files/{f.Slug}.html">{Html.Encode(f.RelPath)}</a></td><td><span class="badge warn">Credential in DDL</span></td></tr>""");
        }
        sb.Append("</table>");
        return sb.ToString();
    }
}
