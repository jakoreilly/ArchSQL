using ArchSql.Analysis;
using ArchSql.Model;

namespace ArchSql.Live;

/// <summary>Turns raw backup-history/statistics/fragmentation rows into MaintenanceInfo. Pure and
/// DB-free; the "now" instant is passed in rather than read from the clock so the aggregation is
/// deterministic and testable.</summary>
public static class MaintenanceAggregate
{
    private const double FragmentationThresholdPercent = 30.0;

    public static MaintenanceInfo Build(
        IReadOnlyList<BackupRow> backups,
        IReadOnlyList<StatsAgeRow> statsRows,
        IReadOnlyList<FragmentationRow> fragRows,
        bool anyQuerySucceeded,
        DateTime nowUtc)
    {
        if (!anyQuerySucceeded)
        {
            return new MaintenanceInfo
            {
                Available = false,
                Note = "Maintenance data unavailable: the login lacks permission to read backup history, statistics, or fragmentation.",
            };
        }

        int? daysSinceBackup = backups.Count > 0
            ? (int)(nowUtc - backups.Max(b => b.BackupFinishDate)).TotalDays
            : null;

        var staleStats = statsRows
            .Select(s => new StaleStatistic
            {
                ObjectId = IdentifierRules.NormalizeId(s.Schema, s.Table, "tsql"),
                StatsName = s.StatsName,
                DaysSinceUpdate = (int)(nowUtc - s.LastUpdated).TotalDays,
            })
            .OrderByDescending(s => s.DaysSinceUpdate).ThenBy(s => s.ObjectId, StringComparer.Ordinal)
            .ToList();

        var fragmented = fragRows
            .Where(f => f.FragmentationPercent >= FragmentationThresholdPercent)
            .Select(f => new FragmentedIndex
            {
                ObjectId = IdentifierRules.NormalizeId(f.Schema, f.Table, "tsql"),
                IndexName = f.IndexName,
                FragmentationPercent = f.FragmentationPercent,
                PageCount = f.PageCount,
            })
            .OrderByDescending(f => f.FragmentationPercent).ThenBy(f => f.ObjectId, StringComparer.Ordinal)
            .ToList();

        var note = daysSinceBackup is { } d
            ? $"Most recent backup was {d} day(s) ago. {staleStats.Count} statistics object(s) and {fragmented.Count} index(es) at or above {FragmentationThresholdPercent:0}% fragmentation are listed below."
            : $"Backup history could not be read. {staleStats.Count} statistics object(s) and {fragmented.Count} index(es) at or above {FragmentationThresholdPercent:0}% fragmentation are listed below.";

        return new MaintenanceInfo
        {
            Available = true,
            Note = note,
            DaysSinceLastBackup = daysSinceBackup,
            StaleStatistics = staleStats,
            FragmentedIndexes = fragmented,
        };
    }
}
