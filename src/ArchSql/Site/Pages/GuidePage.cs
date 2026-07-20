namespace ArchSql.Site.Pages;

public static class GuidePage
{
    public static string Body(SiteContext ctx)
    {
        var live = ctx.Model.Runtime.Source == "live-mssql";
        var sourceLine = live
            ? "This site was built from a read-only connection to a live SQL Server: schema is read from catalog views and runtime figures from DMVs. It only issues SELECT queries and never writes."
            : "ArchSql read the .sql files you pointed it at; no database was connected. It can also build this site from a read-only live connection (the `connect` verb).";
        return $$"""
<h1>Guide</h1>
<p class="lede">ArchSql turns a folder of SQL scripts (or a live SQL Server) into this site. It
supports T-SQL (SQL Server), MySQL and PostgreSQL. T-SQL is parsed in full; MySQL and PostgreSQL use
a lighter-weight parse, so objects from those files are badged 'shallow parse' and a few complex
references may be missing. When a file's dialect can't be told apart, ArchSql assumes T-SQL.</p>

<h2>Where to start</h2>
<p class="lede">New here? Use <a href="explore.html">Explore</a> to search objects and ask the graph
questions, open any object to see its <a href="objects.html">neighborhood</a>, or orbit the whole
schema in the <a href="graph.html">3D Graph</a>.</p>

<h2>What each page shows</h2>
<table class="grid">
<tr><th>Page</th><th>What it shows</th></tr>
<tr><td>Overview</td><td>Stat tiles, dialect mix, overall grade, and the ER diagram.</td></tr>
<tr><td>Explore</td><td>A query console over the dependency graph — <code>referencedby:</code>, <code>affects:</code>, <code>orphans</code>, numeric filters.</td></tr>
<tr><td>Objects</td><td>Every table, view, procedure, function and trigger found. Each links to its detail + neighborhood diagram.</td></tr>
<tr><td>ER Diagram</td><td>Tables and their foreign-key relationships.</td></tr>
<tr><td>Dependencies</td><td>Which objects reference which — procedures calling procedures, views selecting from tables, and so on.</td></tr>
<tr><td>3D Graph</td><td>The whole schema as an interactive force-directed 3D graph; click a node to focus its neighbourhood.</td></tr>
<tr><td>CRUD Matrix</td><td>Which procedures/triggers/views Create, Read, Update or Delete each table.</td></tr>
<tr><td>Impact</td><td>What breaks if you change an object — its transitive dependents (blast radius).</td></tr>
<tr><td>Lint</td><td>SonarQube-style findings: security, correctness, performance and maintainability issues.</td></tr>
<tr><td>Scorecard</td><td>A worst-wins health grade across the same signals as Lint, at a glance.</td></tr>
<tr><td>Metrics</td><td>Fan-in/fan-out coupling and procedure complexity.</td></tr>
<tr><td>Activity</td><td>Runtime hotspots, index issues and issue concentration — populated only from a live connection with DMV permission.</td></tr>
<tr><td>Config &amp; Secrets</td><td>Files that embed a credential — the fact only, never the value.</td></tr>
</table>

<h2>How this site was built</h2>
<p class="note">{{sourceLine}} Everything works from file:// with no network.</p>
""";
    }
}
