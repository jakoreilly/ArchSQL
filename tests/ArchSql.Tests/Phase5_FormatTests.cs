using ArchSql.Analysis;
using Xunit;

namespace ArchSql.Tests;

public class Phase5_FormatTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Format_IsIdempotent()
    {
        var content = Fixture("tsql_schema.sql");
        var once = TSqlFormatter.Format(content);
        var twice = TSqlFormatter.Format(once);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Format_SemanticRoundTrip_ObjectSetUnchanged()
    {
        var analyzer = new TSqlScriptDomAnalyzer();
        var content = Fixture("tsql_schema.sql");
        var before = analyzer.Analyze("tsql_schema.sql", content);

        var formatted = TSqlFormatter.Format(content);
        var after = analyzer.Analyze("tsql_schema.sql", formatted);

        Assert.True(after.ParsedCleanly);
        Assert.Equal(
            before.Objects.Select(o => o.Id).OrderBy(x => x, StringComparer.Ordinal),
            after.Objects.Select(o => o.Id).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(before.ForeignKeys.Count, after.ForeignKeys.Count);

        var beforeColumns = before.Objects.SelectMany(o => o.Columns.Select(c => (o.Id, c.Name))).OrderBy(x => x).ToList();
        var afterColumns = after.Objects.SelectMany(o => o.Columns.Select(c => (o.Id, c.Name))).OrderBy(x => x).ToList();
        Assert.Equal(beforeColumns, afterColumns);
    }

    [Fact]
    public void Format_UnparseableSnippetReturnedByteIdentical()
    {
        var broken = Fixture("tsql_broken.sql");
        var formatted = TSqlFormatter.Format(broken);
        Assert.Equal(broken, formatted);
    }
}
