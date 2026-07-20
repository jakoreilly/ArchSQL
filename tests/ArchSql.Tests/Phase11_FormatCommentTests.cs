using ArchSql.Analysis;
using Xunit;

namespace ArchSql.Tests;

public class Phase11_FormatCommentTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Format_RetainsFileHeaderAndPerObjectDocComments()
    {
        var formatted = TSqlFormatter.Format(Fixture("commented.sql"));
        Assert.Contains("File header: this script creates the Widgets table", formatted);
        Assert.Contains("Widgets: the core catalog table.", formatted);
        Assert.Contains("usp_GetWidget: fetch a single widget by id.", formatted);
    }

    [Fact]
    public void Format_PreservesGoBatchSeparators()
    {
        var formatted = TSqlFormatter.Format(Fixture("commented.sql"));
        Assert.Contains("GO", formatted);
    }

    [Fact]
    public void Format_CommentPreservationIsIdempotent()
    {
        var content = Fixture("commented.sql");
        var once = TSqlFormatter.Format(content);
        var twice = TSqlFormatter.Format(once);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void HasInlineComments_TrueForCommentInsideAStatement()
    {
        Assert.True(TSqlFormatter.HasInlineComments(Fixture("commented.sql")));
    }

    [Fact]
    public void HasInlineComments_FalseWhenCommentsAreOnlyBetweenStatements()
    {
        var sql = "-- header\nCREATE TABLE dbo.T (Id INT);\nGO\n-- footer\n";
        Assert.False(TSqlFormatter.HasInlineComments(sql));
    }

    [Fact]
    public void Format_UnparseableFileReturnedByteIdentical()
    {
        var broken = Fixture("tsql_broken.sql");
        Assert.Equal(broken, TSqlFormatter.Format(broken));
    }
}
