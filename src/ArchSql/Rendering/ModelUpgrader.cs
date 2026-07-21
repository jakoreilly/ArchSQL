using ArchSql.Analysis;
using ArchSql.Model;

namespace ArchSql.Rendering;

/// <summary>Upgrades a deserialized model to the current schema version, filling collections added
/// in later versions with empty defaults. A v1 file has SchemaVersion == 0. Reject anything newer
/// than this build understands — fail cleanly and early rather than crashing mid-run.</summary>
public static class ModelUpgrader
{
    public static SqlModel Upgrade(SqlModel model)
    {
        if (model.SchemaVersion > SchemaVersions.Current)
        {
            throw new InvalidDataException(
                $"model.json schemaVersion {model.SchemaVersion} is newer than this build supports "
                + $"({SchemaVersions.Current}). Upgrade ArchSql.");
        }

        // v0/v1 -> v2 added Crud; v2 -> v3 added Runtime; v3 -> v4 added column detail, index
        // detail, row counts, and code flags; v4 -> v5 added maintenance/backup posture. All are
        // default-initialized on deserialize (empty collections, zero values, Available=false), so
        // nothing needs backfilling — stamp the version forward.
        var upgraded = model.SchemaVersion == SchemaVersions.Current ? model : model with { SchemaVersion = SchemaVersions.Current };

        // A loaded model.json may carry duplicate object ids or runtime keys (an older build, a
        // hand-edited file, or a brownfield export). Normalize so the --from-model path is as
        // resilient as a fresh scan and every id-keyed consumer downstream stays safe.
        return ModelNormalizer.Normalize(upgraded);
    }
}
