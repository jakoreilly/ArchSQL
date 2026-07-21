using ArchSql.Live;
using ArchSql.Model;
using ArchSql.Site;
using Xunit;

namespace ArchSql.Tests;

public class Phase31_MaintenanceTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Build_UnavailableWhenNoQuerySucceeded()
    {
        var info = MaintenanceAggregate.Build([], [], [], anyQuerySucceeded: false, Now);
        Assert.False(info.Available);
        Assert.Null(info.DaysSinceLastBackup);
    }

    [Fact]
    public void Build_ComputesDaysSinceLastBackupFromMostRecent()
    {
        var backups = new List<BackupRow> { new(Now.AddDays(-10)), new(Now.AddDays(-3)) };
        var info = MaintenanceAggregate.Build(backups, [], [], anyQuerySucceeded: true, Now);
        Assert.Equal(3, info.DaysSinceLastBackup);
    }

    [Fact]
    public void Build_FlagsOnlyFragmentationAtOrAboveThreshold()
    {
        var frag = new List<FragmentationRow>
        {
            new("dbo", "T1", "IX_Low", 10.0, 500),
            new("dbo", "T2", "IX_High", 45.0, 500),
        };
        var info = MaintenanceAggregate.Build([], [], frag, anyQuerySucceeded: true, Now);
        var flagged = Assert.Single(info.FragmentedIndexes);
        Assert.Equal("IX_High", flagged.IndexName);
    }

    [Fact]
    public void Build_ComputesDaysSinceStatsUpdate()
    {
        var stats = new List<StatsAgeRow> { new("dbo", "T", "IX_Stat", Now.AddDays(-30)) };
        var info = MaintenanceAggregate.Build([], stats, [], anyQuerySucceeded: true, Now);
        var s = Assert.Single(info.StaleStatistics);
        Assert.Equal(30, s.DaysSinceUpdate);
    }

    [Fact]
    public void ActivityPage_ShowsEmptyStateWhenMaintenanceUnavailable()
    {
        var model = new SqlModel
        {
            RootName = "x", SourcePath = "x",
            Runtime = new RuntimeStats { Source = "live-mssql", Available = true, Note = "n" },
        };
        var body = Site.Pages.ActivityPage.Body(SiteContext.Build(model));
        Assert.Contains("needs additional permission", body);
    }

    [Fact]
    public void ActivityPage_ShowsFragmentedIndexesWhenPresent()
    {
        var model = new SqlModel
        {
            RootName = "x", SourcePath = "x",
            Runtime = new RuntimeStats
            {
                Source = "live-mssql", Available = true, Note = "n",
                Maintenance = new MaintenanceInfo
                {
                    Available = true, Note = "m", DaysSinceLastBackup = 1,
                    FragmentedIndexes = [new FragmentedIndex { ObjectId = "dbo.t", IndexName = "IX_X", FragmentationPercent = 55.5, PageCount = 900 }],
                },
            },
        };
        var body = Site.Pages.ActivityPage.Body(SiteContext.Build(model));
        Assert.Contains("Fragmented indexes", body);
        Assert.Contains("IX_X", body);
        Assert.Contains("55.5%", body);
    }
}
