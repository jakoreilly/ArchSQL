using ArchSql.Model;

namespace ArchSql.Analysis;

/// <summary>Health scorecard. Copies ArchDiagram's ScorecardBuilder idiom (worst-wins grading).
/// Stub in Phase 1 so SqlCiGate compiles; full rows populated in Phase 4.</summary>
public static class SqlScorecard
{
    public enum Status { Ok, Watch, Fail, NA }

    public sealed record Row(string Metric, string Value, Status Status, string Note, string Action = "", string Link = "");
    public sealed record Card(IReadOnlyList<Row> Rows, Status Overall);

    public static Card Build(SqlModel model)
    {
        var rows = new List<Row>
        {
            BuildPkRow(model),
            BuildFkIndexRow(model),
            BuildCredentialsRow(model),
            BuildInjectionRow(model),
            BuildComplexityRow(model),
            BuildDeadObjectRow(model),
        };
        // Worst-wins: NA never worsens the grade (mirrors ArchDiagram's ScorecardBuilder).
        var overall = rows.Select(r => r.Status).Where(s => s != Status.NA).DefaultIfEmpty(Status.Ok).Max();
        return new Card(rows, overall);
    }

    private static Row BuildPkRow(SqlModel model)
    {
        var tables = model.Objects.Count(o => o.Kind == "table");
        if (tables == 0) { return new Row("Tables without a primary key", "n/a", Status.NA, "No tables scanned."); }
        var missing = SqlMetrics.TablesWithoutPrimaryKey(model).Count;
        var status = missing == 0 ? Status.Ok : missing <= tables / 10 + 1 ? Status.Watch : Status.Fail;
        return new Row("Tables without a primary key", $"{missing}/{tables}", status,
            missing == 0 ? "Every table has a primary key." : $"{missing} table(s) have no primary key.",
            "Add PRIMARY KEY constraints.", "lint.html");
    }

    private static Row BuildFkIndexRow(SqlModel model)
    {
        if (model.ForeignKeys.Count == 0) { return new Row("FK columns without a covering index", "n/a", Status.NA, "No foreign keys scanned."); }
        var findings = model.Findings.Count(f => f.RuleId == "SQL0007");
        var status = findings == 0 ? Status.Ok : findings <= model.ForeignKeys.Count / 4 + 1 ? Status.Watch : Status.Fail;
        return new Row("FK columns without a covering index", $"{findings}/{model.ForeignKeys.Count}", status,
            findings == 0 ? "Every FK column is indexed." : $"{findings} FK(s) lack a covering index.",
            "Add non-clustered indexes on FK columns.", "lint.html");
    }

    private static Row BuildCredentialsRow(SqlModel model)
    {
        var count = model.Files.Count(f => f.HasCredential);
        return new Row("Credentials in DDL", count.ToString(), count == 0 ? Status.Ok : Status.Fail,
            count == 0 ? "No embedded credentials found." : $"{count} file(s) embed a login password.",
            "Move credentials to a secrets manager.", "config.html");
    }

    private static Row BuildInjectionRow(SqlModel model)
    {
        var count = model.Dependencies.Count(d => d.Kind == "exec-dynamic");
        return new Row("Injection-risk dynamic SQL", count.ToString(), count == 0 ? Status.Ok : Status.Fail,
            count == 0 ? "No risky dynamic SQL found." : $"{count} procedure(s) build EXEC strings by concatenation.",
            "Use sp_executesql with parameters.", "lint.html");
    }

    private static Row BuildComplexityRow(SqlModel model)
    {
        var procs = model.Objects.Where(o => o.Kind is "procedure" or "function" or "trigger").ToList();
        if (procs.Count == 0) { return new Row("Average procedure complexity", "n/a", Status.NA, "No procedures/functions/triggers scanned."); }
        var avg = procs.Average(o => o.Cyclomatic);
        var status = avg <= 5 ? Status.Ok : avg <= 10 ? Status.Watch : Status.Fail;
        return new Row("Average procedure complexity", avg.ToString("F1"), status,
            $"Average cyclomatic complexity across {procs.Count} procedure(s)/function(s)/trigger(s).",
            "Split high-complexity procedures.", "metrics.html");
    }

    private static Row BuildDeadObjectRow(SqlModel model)
    {
        var candidates = model.Objects.Count(o => o.Kind is "procedure" or "function" or "trigger");
        if (candidates == 0) { return new Row("Dead / unreferenced objects", "n/a", Status.NA, "No procedures/functions/triggers scanned."); }
        var dead = SqlMetrics.DeadObjects(model).Count;
        var ratio = (double)dead / candidates;
        var status = ratio <= 0.10 ? Status.Ok : ratio <= 0.30 ? Status.Watch : Status.Fail;
        return new Row("Dead / unreferenced objects", $"{dead}/{candidates}", status,
            $"{dead} of {candidates} procedure(s)/function(s)/trigger(s) are unreferenced in this scan.",
            "Confirm still-called from application code, or remove.", "objects.html");
    }
}
