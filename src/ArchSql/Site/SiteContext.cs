using ArchSql.Analysis;
using ArchSql.Model;

namespace ArchSql.Site;

/// <summary>Precomputed, shared-across-pages view of a SqlModel: lookups, fan-in/out, scorecard.
/// Built once per site generation so pages never recompute.</summary>
public sealed class SiteContext
{
    public required SqlModel Model { get; init; }
    public required Dictionary<string, DbObject> ById { get; init; }
    public required Dictionary<string, SqlFile> BySlug { get; init; }
    public required Dictionary<string, int> FanIn { get; init; }
    public required Dictionary<string, int> FanOut { get; init; }
    public required SqlScorecard.Card Scorecard { get; init; }

    public static SiteContext Build(SqlModel model) => new()
    {
        Model = model,
        ById = model.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal),
        BySlug = model.Files.ToDictionary(f => f.Slug, StringComparer.Ordinal),
        FanIn = SqlMetrics.FanIn(model),
        FanOut = SqlMetrics.FanOut(model),
        Scorecard = SqlScorecard.Build(model),
    };
}
