using ArchSql.Model;

namespace ArchSql.Analysis;

/// <summary>Per-schema coupling and health signals. Pure static methods (Hard Constraint 8:
/// coverage on new code ships with the logic).</summary>
public static class SqlMetrics
{
    public static Dictionary<string, int> FanIn(SqlModel model)
    {
        var fanIn = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var o in model.Objects) { fanIn.TryAdd(o.Id, 0); }
        foreach (var o in model.Objects)
        {
            foreach (var refId in o.ReferencesObjectIds)
            {
                if (fanIn.ContainsKey(refId)) { fanIn[refId]++; }
            }
        }
        return fanIn;
    }

    public static Dictionary<string, int> FanOut(SqlModel model) =>
        model.Objects.ToDictionary(o => o.Id, o => o.ReferencesObjectIds.Count, StringComparer.Ordinal);

    /// <summary>Tables with no primary key — the clearest normalization/lint signal.</summary>
    public static List<DbObject> TablesWithoutPrimaryKey(SqlModel model) =>
        model.Objects.Where(o => o.Kind == "table" && o.PrimaryKey.Count == 0).ToList();

    /// <summary>Objects nothing references, excluding entry-point kinds (tables/views are
    /// legitimately unreferenced when queried only by application code outside the scan).</summary>
    public static List<DbObject> DeadObjects(SqlModel model)
    {
        var fanIn = FanIn(model);
        return model.Objects
            .Where(o => o.Kind is "procedure" or "function" or "trigger" && fanIn.GetValueOrDefault(o.Id) == 0)
            .ToList();
    }

    /// <summary>Cyclomatic complexity for a procedure/function/trigger body: 1 + count of
    /// decision-point keywords. Deliberately simple (statement-text counting, not a full CFG) —
    /// matches the tokenizer-tier accuracy tradeoff documented in plan.md.</summary>
    public static int Cyclomatic(string body)
    {
        if (string.IsNullOrEmpty(body)) { return 1; }
        var count = 1;
        foreach (var keyword in DecisionKeywords)
        {
            var idx = 0;
            while ((idx = body.IndexOf(keyword, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                idx += keyword.Length;
            }
        }
        return count;
    }

    private static readonly string[] DecisionKeywords = ["IF ", "WHILE ", "CASE ", "CATCH"];
}
