using System.Text.Json;
using ArchSql.Model;

namespace ArchSql.Rendering;

/// <summary>SARIF 2.1.0 output mapping every LintFinding to a SARIF result. Uses its own
/// JsonSerializerOptions (not the camelCase model options) because SARIF keys like "$schema" are
/// case-sensitive. The top level is built as a Dictionary&lt;string,object&gt; (not an anonymous
/// type) so "$schema" can be a key.</summary>
public static class SarifWriter
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void Write(SqlModel model, string path)
    {
        var results = model.Findings.Select(f => BuildResult(model, f)).ToList();
        var rules = model.Findings.Select(f => f.RuleId).Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .Select(id => new Dictionary<string, object> { ["id"] = id })
            .ToList();
        var top = new Dictionary<string, object>
        {
            ["$schema"] = "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/main/Schemata/sarif-schema-2.1.0.json",
            ["version"] = "2.1.0",
            ["runs"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["tool"] = new Dictionary<string, object>
                    {
                        ["driver"] = new Dictionary<string, object> { ["name"] = "ArchSql", ["rules"] = rules },
                    },
                    ["results"] = results,
                },
            },
        };
        using var stream = new FileStream(path, FileMode.Create);
        JsonSerializer.Serialize(stream, top, Options);
    }

    private static Dictionary<string, object> BuildResult(SqlModel model, LintFinding f)
    {
        var result = new Dictionary<string, object>
        {
            ["ruleId"] = f.RuleId,
            ["level"] = LevelFor(f.Severity),
            ["message"] = new Dictionary<string, object> { ["text"] = f.Message },
        };
        var location = LocationFor(model, f);
        if (location is not null) { result["locations"] = new object[] { location }; }
        return result;
    }

    private static Dictionary<string, object>? LocationFor(SqlModel model, LintFinding f)
    {
        var file = model.Files.FirstOrDefault(x => x.Slug == f.Slug);
        if (file is null) { return null; }
        return new Dictionary<string, object>
        {
            ["physicalLocation"] = new Dictionary<string, object>
            {
                ["artifactLocation"] = new Dictionary<string, object> { ["uri"] = file.RelPath },
                ["region"] = new Dictionary<string, object> { ["startLine"] = Math.Max(1, f.Line) },
            },
        };
    }

    private static string LevelFor(int severity) => severity switch
    {
        0 or 1 => "error",
        2 => "warning",
        _ => "note",
    };
}
