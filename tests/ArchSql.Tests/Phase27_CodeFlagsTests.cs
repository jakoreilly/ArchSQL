using ArchSql.Analysis;
using Xunit;

namespace ArchSql.Tests;

public class Phase27_CodeFlagsTests
{
    [Fact]
    public void Scan_DetectsNolockHint()
    {
        var flags = CodeFlagsScanner.Scan("SELECT * FROM dbo.T WITH (NOLOCK)");
        Assert.True(flags.UsesNolock);
    }

    [Fact]
    public void Scan_DetectsReadUncommittedPhrase()
    {
        var flags = CodeFlagsScanner.Scan("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED");
        Assert.True(flags.UsesNolock);
    }

    [Fact]
    public void Scan_DoesNotFlagIdentifierContainingNolockSubstring()
    {
        var flags = CodeFlagsScanner.Scan("SELECT NolockedValue FROM dbo.T");
        Assert.False(flags.UsesNolock);
    }

    [Fact]
    public void Scan_DetectsCursor()
    {
        Assert.True(CodeFlagsScanner.Scan("DECLARE cur CURSOR FOR SELECT 1").UsesCursor);
    }

    [Fact]
    public void Scan_DetectsAtAtIdentity()
    {
        Assert.True(CodeFlagsScanner.Scan("SELECT @@IDENTITY").UsesAtAtIdentity);
    }

    [Fact]
    public void Scan_DetectsSetNoCountOn()
    {
        Assert.True(CodeFlagsScanner.Scan("SET NOCOUNT ON; SELECT 1;").HasSetNoCount);
    }

    [Fact]
    public void Scan_MissingSetNoCountIsFalse()
    {
        Assert.False(CodeFlagsScanner.Scan("SELECT 1;").HasSetNoCount);
    }

    [Fact]
    public void Scan_DetectsExecuteAs()
    {
        Assert.True(CodeFlagsScanner.Scan("CREATE PROCEDURE dbo.P WITH EXECUTE AS OWNER AS SELECT 1").UsesExecuteAs);
    }

    [Fact]
    public void Scan_EmptySourceReturnsAllFalse()
    {
        var flags = CodeFlagsScanner.Scan("");
        Assert.False(flags.UsesNolock);
        Assert.False(flags.UsesCursor);
        Assert.False(flags.UsesAtAtIdentity);
        Assert.False(flags.HasSetNoCount);
        Assert.False(flags.UsesExecuteAs);
    }

    [Fact]
    public void Analyzer_SetsCodeFlagsOnParsedProcedure()
    {
        const string sql = """
            CREATE PROCEDURE dbo.usp_NoCount AS
            BEGIN
                SELECT Id FROM dbo.T WITH (NOLOCK);
            END
            """;
        var facts = new TSqlScriptDomAnalyzer().Analyze("a.sql", sql);
        var proc = Assert.Single(facts.Objects, o => o.Kind == "procedure");
        Assert.True(proc.CodeFlags.UsesNolock);
        Assert.False(proc.CodeFlags.HasSetNoCount);
    }

    [Fact]
    public void SqlRules_MissingSetNoCount_OnlyAppliesToProcedures()
    {
        const string sql = """
            CREATE PROCEDURE dbo.usp_A AS SELECT 1;
            GO
            CREATE FUNCTION dbo.fn_B() RETURNS INT AS BEGIN RETURN 1; END
            """;
        var facts = new TSqlScriptDomAnalyzer().Analyze("a.sql", sql);
        var model = new ArchSql.Model.SqlModel { RootName = "x", SourcePath = "x", Objects = facts.Objects };
        var findings = SqlRules.Run(model);
        Assert.Contains(findings, f => f.RuleId == "SQL0020" && f.ObjectId == "dbo.usp_a");
        Assert.DoesNotContain(findings, f => f.RuleId == "SQL0020" && f.ObjectId == "dbo.fn_b");
    }
}
