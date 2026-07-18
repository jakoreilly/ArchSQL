namespace ArchSql.Site.Pages;

public static class GuidePage
{
    public static string Body(SiteContext ctx) => $"""
<h1>Guide</h1>
<p class="lede">ArchSql turns a folder of SQL scripts into this site. It supports T-SQL (SQL Server),
MySQL and PostgreSQL. T-SQL is parsed in full; MySQL and PostgreSQL use a lighter-weight parse,
so objects from those files are badged 'shallow parse' and a few complex references may be
missing. When a file's dialect can't be told apart, ArchSql assumes T-SQL.</p>

<h2>What each page shows</h2>
<table class="grid">
<tr><th>Page</th><th>What it shows</th></tr>
<tr><td>Overview</td><td>Stat tiles, dialect mix, overall grade, and the ER diagram.</td></tr>
<tr><td>Objects</td><td>Every table, view, procedure, function and trigger found in the scan.</td></tr>
<tr><td>ER Diagram</td><td>Tables and their foreign-key relationships.</td></tr>
<tr><td>Dependencies</td><td>Which objects reference which — procedures calling other procedures, views selecting from tables, and so on.</td></tr>
<tr><td>Lint</td><td>SonarQube-style findings: security, correctness, performance and maintainability issues.</td></tr>
<tr><td>Scorecard</td><td>A worst-wins health grade across the same signals as Lint, at a glance.</td></tr>
<tr><td>Metrics</td><td>Fan-in/fan-out coupling and procedure complexity.</td></tr>
<tr><td>Config &amp; Secrets</td><td>Files that embed a credential — the fact only, never the value.</td></tr>
</table>

<h2>What ArchSql does not do</h2>
<p class="note">ArchSql never connects to a database. It only reads the .sql files you point it at.
Live schema introspection is a designed-but-not-built feature for a future release.</p>
""";
}
