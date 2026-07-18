using System.Text.Json;

namespace ArchSql.Site;

/// <summary>Term -> plain-English explanation, rendered into the page's circled-i popovers by
/// site.js (reads #arch-glossary). Copies ArchDiagram's Glossary idiom.</summary>
public static class Glossary
{
    private static readonly Dictionary<string, string> Terms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fan-in"] = "How many other objects reference this one. High fan-in means changes here ripple widely.",
        ["fan-out"] = "How many other objects this one references. High fan-out means it knows a lot about the rest of the schema.",
        ["cyclomatic"] = "A count of decision points (IF/WHILE/CASE) in a procedure's body — a rough proxy for how many test cases it needs.",
        ["shallow parse"] = "This file's dialect wasn't deep-parsed (only T-SQL is analyzed in v1) — object and dependency detection here is best-effort.",
    };

    public static string Info(string term) =>
        Terms.ContainsKey(term) ? $"<button class=\"explain\" data-term=\"{Html.Encode(term)}\" type=\"button\" title=\"What is {Html.Encode(term)}?\">ⓘ</button>" : "";

    public static string Json() => JsonSerializer.Serialize(Terms);
}
