using ArchSql.Analysis;
using ArchSql.Model;

namespace ArchSql.Live;

/// <summary>Turns raw DMV rows into the model's RuntimeStats: normalizes ids the same way the
/// analyzer does, flags unused indexes, and builds the deterministic sorted lists plus the UI
/// note. Pure and DB-free.</summary>
public static class RuntimeAggregate
{
    private const string VolatilityNote =
        "Counters are cumulative since SQL Server statistics last reset (not a fixed time window) "
        + "and clear on restart.";

    public static RuntimeStats Build(
        IReadOnlyList<ProcStatRow> procStats,
        IReadOnlyList<IndexUsageRow> indexUsage,
        IReadOnlyList<MissingIndexRow> missing,
        bool available,
        string? unavailableReason = null)
    {
        if (!available)
        {
            return new RuntimeStats
            {
                Source = "live-mssql",
                Available = false,
                Note = unavailableReason ?? "Runtime data unavailable.",
            };
        }

        var objectStats = procStats
            .Select(p => new ObjectStat
            {
                ObjectId = Id(p.Schema, p.Name),
                ExecutionCount = p.ExecCount,
                TotalWorkerTimeMs = p.TotalWorkerMs,
                TotalLogicalReads = p.TotalLogicalReads,
            })
            .OrderByDescending(s => s.ExecutionCount).ThenBy(s => s.ObjectId, StringComparer.Ordinal)
            .ToList();

        var indexStats = indexUsage
            .Select(i => new IndexStat
            {
                ObjectId = Id(i.Schema, i.Table),
                IndexName = i.IndexName,
                UserSeeks = i.Seeks,
                UserScans = i.Scans,
                UserLookups = i.Lookups,
                UserUpdates = i.Updates,
                IsUnused = i.Seeks + i.Scans + i.Lookups == 0 && i.Updates > 0,
            })
            .OrderBy(s => s.ObjectId, StringComparer.Ordinal).ThenBy(s => s.IndexName, StringComparer.Ordinal)
            .ToList();

        var missingIndexes = missing
            .Select(m => new MissingIndex
            {
                ObjectId = Id(m.Schema, m.Table),
                EqualityColumns = m.EqualityColumns,
                InequalityColumns = m.InequalityColumns,
                IncludedColumns = m.IncludedColumns,
                ImpactScore = m.ImpactScore,
            })
            .OrderByDescending(m => m.ImpactScore).ThenBy(m => m.ObjectId, StringComparer.Ordinal)
            .ToList();

        var unusedCount = indexStats.Count(i => i.IsUnused);
        var note = $"{objectStats.Count} object(s) with execution stats, {missingIndexes.Count} missing-index "
            + $"suggestion(s), {unusedCount} unused index(es). {VolatilityNote}";

        // Two catalog/DMV rows can normalize to the same id on a messy schema; collapse them so
        // downstream id-keyed lookups never see a duplicate.
        return ModelNormalizer.DedupeRuntime(new RuntimeStats
        {
            Source = "live-mssql",
            Available = true,
            Note = note,
            ObjectStats = objectStats,
            IndexStats = indexStats,
            MissingIndexes = missingIndexes,
        });
    }

    private static string Id(string schema, string name) => IdentifierRules.NormalizeId(schema, name, "tsql");
}
