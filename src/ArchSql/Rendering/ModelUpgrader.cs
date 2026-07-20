using ArchSql.Model;

namespace ArchSql.Rendering;

/// <summary>Upgrades a deserialized model to the current schema version, filling collections added
/// in later versions with empty defaults. A v1 file has SchemaVersion == 0. Reject anything newer
/// than we understand (Hard Constraint: never crash mid-run — this is a clean, early failure).</summary>
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

        // v0/v1 -> v2 added Crud; v2 -> v3 added Runtime. Both are default-initialized on
        // deserialize (Crud to [], Runtime to an empty RuntimeStats with Available=false), so
        // nothing needs backfilling — stamp the version forward.
        return model.SchemaVersion == SchemaVersions.Current ? model : model with { SchemaVersion = SchemaVersions.Current };
    }
}
