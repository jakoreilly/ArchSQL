namespace ArchSql.Analysis;

/// <summary>Optional companion to ISchemaSource: a source that can also supply live runtime facts
/// (execution stats, index usage, missing indexes). Only the live DB source implements it; the
/// file source does not, so the pipeline leaves RuntimeStats empty for file scans. Kept separate
/// from ISchemaSource so the text-unit contract is unchanged and every existing caller compiles
/// untouched.</summary>
public interface IRuntimeStatsSource
{
    Model.RuntimeStats ReadRuntime();
}
