namespace ArchSql.Analysis;

/// <summary>CI gate: maps a --fail-on gate name to the Scorecard row it reads. Copies
/// ArchDiagram's CiGate idiom. Full Evaluate() logic lands in Phase 4 alongside SqlScorecard;
/// KnownGates exists from Phase 1 so CliOptions.Parse can validate --fail-on immediately.</summary>
public static class SqlCiGate
{
    public static readonly Dictionary<string, string> KnownGates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["secrets"] = "Credentials in DDL",
        ["injection"] = "Injection-risk dynamic SQL",
        ["no-pk"] = "Tables without a primary key",
        ["complexity"] = "Procedure complexity",
        ["scorecard"] = "",
    };

    /// <summary>Returns failure reasons (empty = all pass). Real implementation added in Phase 4
    /// once SqlScorecard.Card exists; until then this always passes (no false CI failures).</summary>
    public static List<string> Evaluate(IReadOnlyList<string> gates, SqlScorecard.Card card)
    {
        var reasons = new List<string>();
        var byMetric = card.Rows.ToDictionary(r => r.Metric, StringComparer.OrdinalIgnoreCase);
        foreach (var gate in gates)
        {
            if (!KnownGates.TryGetValue(gate, out var metric)) { continue; }
            if (metric.Length == 0)
            {
                if (card.Overall == SqlScorecard.Status.Fail) { reasons.Add($"{gate}: overall scorecard = Fail"); }
                continue;
            }
            if (byMetric.TryGetValue(metric, out var row) && row.Status == SqlScorecard.Status.Fail)
            {
                reasons.Add($"{gate}: {row.Metric} = {row.Value} — {row.Note}");
            }
        }
        return reasons;
    }
}
