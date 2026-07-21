using ArchSql.Model;

namespace ArchSql.Analysis;

/// <summary>A lint rule: metadata plus a pure function producing findings from the resolved
/// model. Rules are registered in a list, not a switch (Hard Constraint 5, cognitive complexity),
/// mirroring ArchDiagram's RefactoringBacklog composition idiom.</summary>
public sealed record SqlRule(string Id, int Severity, string Category, string Title, string Why, string Tip);

public static class SqlRules
{
    /// <summary>Sonar gate for procedure cyclomatic complexity — matches ArchDiagram's
    /// Severity.SonarGate=15 (same threshold, independently duplicated per Hard Constraint:
    /// Analysis must not depend on a future Site layer constant).</summary>
    public const int ComplexityGate = 15;

    public static readonly IReadOnlyList<(SqlRule Rule, Func<SqlModel, IEnumerable<LintFinding>> Check)> Rules =
    [
        (new SqlRule("SQL0001", 0, "Security", "Credential in DDL",
            "A password embedded in a CREATE LOGIN statement is a secret committed to source.",
            "Move the credential to a secrets manager and rotate it; script the login without an inline password."),
            CheckCredentials),

        (new SqlRule("SQL0002", 0, "Security", "Dynamic SQL injection risk",
            "A parameter concatenated into EXEC/sp_executesql can allow SQL injection if the caller controls the value.",
            "Use sp_executesql with parameterized placeholders instead of string concatenation."),
            CheckInjectionRisk),

        (new SqlRule("SQL0003", 1, "Schema", "Table without primary key",
            "Tables without a primary key can't be reliably referenced by foreign keys or replicated, and rows can't be uniquely identified.",
            "Add a PRIMARY KEY constraint on the natural or surrogate key column(s)."),
            CheckMissingPrimaryKey),

        (new SqlRule("SQL0004", 1, "Correctness", "Un-WHERE'd UPDATE/DELETE",
            "An UPDATE or DELETE without a WHERE clause affects every row in the table.",
            "Add a WHERE clause, or confirm the full-table change is intentional and gate it behind an explicit parameter."),
            CheckUnWhered),

        (new SqlRule("SQL0005", 2, "Maintainability", "SELECT * in view or procedure",
            "SELECT * breaks when the underlying table's columns change and pulls more data than the caller needs.",
            "List the columns the caller actually needs."),
            CheckSelectStar),

        (new SqlRule("SQL0006", 2, "Schema", "Nullable foreign-key column",
            "A nullable FK column allows orphaned or ambiguous relationships and often signals an incomplete data model.",
            "Make the column NOT NULL if every row must reference a parent, or document why it's optional."),
            CheckNullableForeignKey),

        (new SqlRule("SQL0007", 2, "Performance", "Missing index on foreign-key column",
            "Foreign-key columns without a supporting index force a table scan on every join or cascade check.",
            "Add a non-clustered index covering the FK column(s)."),
            CheckMissingFkIndex),

        (new SqlRule("SQL0008", 2, "Maintainability", "High procedure complexity",
            $"A cyclomatic complexity at or above {ComplexityGate} is hard to test and reason about.",
            "Split the procedure into smaller, single-purpose procedures or extract branches into functions."),
            CheckHighComplexity),

        (new SqlRule("SQL0009", 3, "Convention", "Non-standard naming",
            "An object name containing spaces or starting with a digit deviates from common T-SQL naming conventions.",
            "Rename to a letter-led, space-free identifier."),
            CheckNonStandardNaming),

        (new SqlRule("SQL0010", 3, "Maintainability", "No static caller found",
            "No object in this scan references this procedure/function/trigger, and it has no runtime "
            + "execution recorded. It may still be invoked by application code or dynamic SQL.",
            "Confirm it's still called externally, or remove it."),
            CheckDeadObjects),

        (new SqlRule("SQL0011", 0, "Security", "Privileged / dangerous command",
            "This object invokes a high-risk built-in (xp_cmdshell, OLE automation, or database mail) "
            + "that can run OS commands, instantiate COM objects, or send data off the server.",
            "Confirm the privilege is required; prefer a constrained alternative and least-privilege execution context."),
            CheckDangerousCommands),

        (new SqlRule("SQL0012", 2, "Schema", "Deprecated column type",
            "text, ntext and image are deprecated (removed in modern SQL Server) and don't support "
            + "many string/comparison operations.",
            "Migrate text/ntext to varchar(max)/nvarchar(max) and image to varbinary(max)."),
            CheckDeprecatedTypes),
    ];

    private static readonly string[] DangerousCommands =
        ["xp_cmdshell", "sp_oacreate", "sp_oamethod", "sp_oasetproperty", "sp_oagetproperty", "sp_oadestroy", "sp_oageterrorinfo", "sp_send_dbmail"];

    private static readonly string[] DeprecatedColumnTypes = ["text", "ntext", "image"];

    public static List<LintFinding> Run(SqlModel model) =>
        Rules.SelectMany(r => r.Check(model)).ToList();

    private static List<LintFinding> CheckCredentials(SqlModel model) =>
        model.Files.Where(f => f.HasCredential)
            .Select(f => new LintFinding
            {
                RuleId = "SQL0001", Severity = 0, Title = "Credential in DDL",
                Message = $"{f.RelPath} contains a CREATE LOGIN with an embedded password.",
                Slug = f.Slug,
            })
            .ToList();

    private static List<LintFinding> CheckInjectionRisk(SqlModel model) =>
        model.Dependencies.Where(d => d.Kind == "exec-dynamic")
            .Select(d => ForObject(model, d.FromObjectId, "SQL0002", 0,
                $"Dynamic SQL built by concatenation reaches EXEC in {ObjectName(model, d.FromObjectId)}."))
            .ToList();

    private static List<LintFinding> CheckMissingPrimaryKey(SqlModel model) =>
        SqlMetrics.TablesWithoutPrimaryKey(model)
            .Select(o => ForObject(model, o.Id, "SQL0003", 1, $"Table {o.Schema}.{o.Name} has no primary key."))
            .ToList();

    private static List<LintFinding> CheckUnWhered(SqlModel model) =>
        model.Dependencies.Where(d => d.Kind is "update-nowhere" or "delete-nowhere")
            .Select(d => ForObject(model, d.FromObjectId, "SQL0004", 1,
                $"{(d.Kind == "update-nowhere" ? "UPDATE" : "DELETE")} without a WHERE clause in {ObjectName(model, d.FromObjectId)}."))
            .ToList();

    private static List<LintFinding> CheckSelectStar(SqlModel model) =>
        model.Dependencies.Where(d => d.Kind == "select-star")
            .Select(d => ForObject(model, d.FromObjectId, "SQL0005", 2, $"SELECT * found in {ObjectName(model, d.FromObjectId)}."))
            .ToList();

    private static List<LintFinding> CheckNullableForeignKey(SqlModel model)
    {
        var findings = new List<LintFinding>();
        var byId = model.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        foreach (var fk in model.ForeignKeys)
        {
            if (!byId.TryGetValue(fk.FromObjectId, out var table)) { continue; }
            foreach (var colName in fk.FromColumns)
            {
                var col = table.Columns.FirstOrDefault(c => c.Name.Equals(colName, StringComparison.OrdinalIgnoreCase));
                if (col is { Nullable: true })
                {
                    findings.Add(new LintFinding
                    {
                        RuleId = "SQL0006", Severity = 2, ObjectId = fk.FromObjectId, Slug = table.DefinedInSlug,
                        Title = "Nullable foreign-key column",
                        Message = $"{table.Schema}.{table.Name}.{colName} is a nullable FK column.",
                    });
                }
            }
        }
        return findings;
    }

    private static List<LintFinding> CheckMissingFkIndex(SqlModel model)
    {
        var findings = new List<LintFinding>();
        var byId = model.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        foreach (var fk in model.ForeignKeys)
        {
            if (!byId.TryGetValue(fk.FromObjectId, out var table)) { continue; }
            var covered = CoveredColumns(table);
            if (fk.FromColumns.Any(c => !covered.Contains(c)))
            {
                findings.Add(new LintFinding
                {
                    RuleId = "SQL0007", Severity = 2, ObjectId = fk.FromObjectId, Slug = table.DefinedInSlug,
                    Title = "Missing index on foreign-key column",
                    Message = $"{table.Schema}.{table.Name} has FK column(s) [{string.Join(", ", fk.FromColumns)}] with no covering index.",
                });
            }
        }
        return findings;
    }

    /// <summary>Column names covered by any index or the primary key. Index entries are encoded
    /// "name(col1,col2)" by the analyzer; parse them back out here.</summary>
    private static HashSet<string> CoveredColumns(DbObject table)
    {
        var covered = new HashSet<string>(table.PrimaryKey, StringComparer.OrdinalIgnoreCase);
        foreach (var index in table.Indexes)
        {
            var open = index.IndexOf('(');
            var close = index.IndexOf(')');
            if (open < 0 || close <= open) { continue; }
            foreach (var col in index[(open + 1)..close].Split(',', StringSplitOptions.RemoveEmptyEntries)) { covered.Add(col); }
        }
        return covered;
    }

    private static List<LintFinding> CheckHighComplexity(SqlModel model) =>
        model.Objects.Where(o => o.Kind is "procedure" or "function" or "trigger" && o.Cyclomatic >= ComplexityGate)
            .Select(o => ForObject(model, o.Id, "SQL0008", 2, $"{o.Schema}.{o.Name} has cyclomatic complexity {o.Cyclomatic}."))
            .ToList();

    private static List<LintFinding> CheckNonStandardNaming(SqlModel model) =>
        model.Objects.Where(o => o.Name.Contains(' ') || (o.Name.Length > 0 && char.IsDigit(o.Name[0])))
            .Select(o => ForObject(model, o.Id, "SQL0009", 3, $"{o.Schema}.{o.Name} does not follow standard naming."))
            .ToList();

    private static List<LintFinding> CheckDeadObjects(SqlModel model)
    {
        // An object that actually executed (runtime evidence) is provably live — never flag it,
        // even if no static caller was found. This turns a false-positive-heavy signal into an
        // actionable one on live-connection scans.
        var executed = model.Runtime.Available
            ? model.Runtime.ObjectStats.Select(s => s.ObjectId).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        return SqlMetrics.DeadObjects(model)
            .Where(o => !executed.Contains(o.Id))
            .Select(o => ForObject(model, o.Id, "SQL0010", 3,
                $"{o.Schema}.{o.Name} has no static caller in this scan and no recorded execution."))
            .ToList();
    }

    private static List<LintFinding> CheckDangerousCommands(SqlModel model)
    {
        var findings = new List<LintFinding>();
        var seen = new HashSet<(string, string)>();
        foreach (var d in model.Dependencies)
        {
            var target = (d.ExternalTarget.Length > 0 ? d.ExternalTarget : d.ToObjectId).ToLowerInvariant();
            var hit = DangerousCommands.FirstOrDefault(c => target.Contains(c, StringComparison.Ordinal));
            if (hit is null || !seen.Add((d.FromObjectId, hit))) { continue; }
            findings.Add(ForObject(model, d.FromObjectId, "SQL0011", 0,
                $"{ObjectName(model, d.FromObjectId)} invokes {hit}."));
        }
        return findings;
    }

    private static List<LintFinding> CheckDeprecatedTypes(SqlModel model)
    {
        var findings = new List<LintFinding>();
        foreach (var o in model.Objects.Where(o => o.Kind == "table"))
        {
            foreach (var c in o.Columns)
            {
                var baseType = c.DataType.Split('(', 2)[0].Trim().ToLowerInvariant();
                if (Array.IndexOf(DeprecatedColumnTypes, baseType) < 0) { continue; }
                findings.Add(new LintFinding
                {
                    RuleId = "SQL0012", Severity = 2, ObjectId = o.Id, Slug = o.DefinedInSlug,
                    Title = "Deprecated column type",
                    Message = $"{o.Schema}.{o.Name}.{c.Name} uses deprecated type {baseType}.",
                    Line = o.DefinedAtLine,
                });
            }
        }
        return findings;
    }

    private static LintFinding ForObject(SqlModel model, string objectId, string ruleId, int severity, string message)
    {
        var obj = model.Objects.FirstOrDefault(o => o.Id == objectId);
        return new LintFinding
        {
            RuleId = ruleId, Severity = severity, ObjectId = objectId, Slug = obj?.DefinedInSlug ?? "",
            Title = Rules.First(r => r.Rule.Id == ruleId).Rule.Title,
            Message = message,
            Line = obj?.DefinedAtLine ?? 0,
        };
    }

    private static string ObjectName(SqlModel model, string objectId)
    {
        var obj = model.Objects.FirstOrDefault(o => o.Id == objectId);
        return obj is null ? objectId : $"{obj.Schema}.{obj.Name}";
    }
}
