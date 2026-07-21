using ArchSql;
using ArchSql.Analysis;
using ArchSql.Cli;
using ArchSql.Model;
using Xunit;

namespace ArchSql.Tests;

public class Phase23_RuleFixTests
{
    [Fact]
    public void DeadObject_WithRuntimeExecution_IsNotFlagged()
    {
        // A proc nothing statically references, but which actually executed, must not be "dead".
        var model = new SqlModel
        {
            RootName = "x", SourcePath = "x",
            Objects = [new DbObject { Id = "dbo.usp_live", Schema = "dbo", Name = "usp_live", Kind = "procedure", Dialect = "tsql" }],
            Runtime = new RuntimeStats { Source = "live-mssql", Available = true, ObjectStats = [new ObjectStat { ObjectId = "dbo.usp_live", ExecutionCount = 5 }] },
        };
        var findings = SqlRules.Run(model);
        Assert.DoesNotContain(findings, f => f.RuleId == "SQL0010" && f.ObjectId == "dbo.usp_live");
    }

    [Fact]
    public void DeadObject_WithoutRuntimeEvidence_IsStillFlagged()
    {
        var model = new SqlModel
        {
            RootName = "x", SourcePath = "x",
            Objects = [new DbObject { Id = "dbo.usp_cold", Schema = "dbo", Name = "usp_cold", Kind = "procedure", Dialect = "tsql" }],
        };
        Assert.Contains(SqlRules.Run(model), f => f.RuleId == "SQL0010" && f.ObjectId == "dbo.usp_cold");
    }

    [Fact]
    public void DangerousCommand_XpCmdShell_IsFlagged()
    {
        var model = new SqlModel
        {
            RootName = "x", SourcePath = "x",
            Objects = [new DbObject { Id = "dbo.usp_shell", Schema = "dbo", Name = "usp_shell", Kind = "procedure", Dialect = "tsql" }],
            Dependencies = [new ObjectDep { FromObjectId = "dbo.usp_shell", Kind = "exec", ExternalTarget = "master.dbo.xp_cmdshell" }],
        };
        var findings = SqlRules.Run(model);
        var f = Assert.Single(findings, x => x.RuleId == "SQL0011");
        Assert.Contains("xp_cmdshell", f.Message);
        Assert.Equal(0, f.Severity);
    }

    [Fact]
    public void DeprecatedType_TextColumn_IsFlagged()
    {
        var model = new SqlModel
        {
            RootName = "x", SourcePath = "x",
            Objects =
            [
                new DbObject { Id = "dbo.t", Schema = "dbo", Name = "T", Kind = "table", Dialect = "tsql",
                    Columns = [new Column { Name = "Notes", DataType = "text" }, new Column { Name = "Id", DataType = "int" }] },
            ],
        };
        var f = Assert.Single(SqlRules.Run(model), x => x.RuleId == "SQL0012");
        Assert.Contains("text", f.Message);
    }
}
