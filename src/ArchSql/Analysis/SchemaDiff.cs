using ArchSql.Model;

namespace ArchSql.Analysis;

public enum ChangeRisk { Safe, Degrading, Breaking }

public sealed record SchemaChange(string Kind, string Target, ChangeRisk Risk, string Detail);

/// <summary>Model-level schema diff (never SQL text). Identity keying by normalized object id and
/// id.column. Each change kind is its own small helper (Sonar S3776: keeps cognitive complexity
/// down as the rule set grows) rather than one large switch.</summary>
public static class SchemaDiff
{
    public static List<SchemaChange> Compute(SqlModel oldM, SqlModel newM)
    {
        var changes = new List<SchemaChange>();
        var oldObj = oldM.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        var newObj = newM.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);

        foreach (var id in oldObj.Keys.Where(k => !newObj.ContainsKey(k)))
        {
            changes.Add(DroppedObject(oldObj[id]));
        }
        foreach (var id in newObj.Keys.Where(k => !oldObj.ContainsKey(k)))
        {
            changes.Add(new SchemaChange("object-added", id, ChangeRisk.Safe, $"New {newObj[id].Kind}."));
        }
        foreach (var id in oldObj.Keys.Where(newObj.ContainsKey))
        {
            changes.AddRange(DiffColumns(oldObj[id], newObj[id]));
        }

        changes.AddRange(DiffForeignKeys(oldM, newM));

        return changes
            .OrderByDescending(c => c.Risk)
            .ThenBy(c => c.Target, StringComparer.Ordinal)
            .ThenBy(c => c.Kind, StringComparer.Ordinal)
            .ToList();
    }

    private static SchemaChange DroppedObject(DbObject o) =>
        new("object-dropped", o.Id, ChangeRisk.Breaking, $"Dropped {o.Kind} {o.Schema}.{o.Name}.");

    private static IEnumerable<SchemaChange> DiffColumns(DbObject oldO, DbObject newO)
    {
        var oldCols = oldO.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var newCols = newO.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var c in oldCols.Values.Where(c => !newCols.ContainsKey(c.Name)))
        {
            yield return new SchemaChange("column-dropped", $"{oldO.Id}.{c.Name}", ChangeRisk.Breaking, $"Dropped column {c.Name}.");
        }
        foreach (var c in newCols.Values.Where(c => !oldCols.ContainsKey(c.Name)))
        {
            yield return new SchemaChange("column-added", $"{newO.Id}.{c.Name}",
                c.Nullable ? ChangeRisk.Safe : ChangeRisk.Breaking,
                c.Nullable ? "New nullable column." : "New NOT NULL column with no default breaks existing INSERTs.");
        }
        foreach (var c in oldCols.Values.Where(c => newCols.ContainsKey(c.Name)))
        {
            foreach (var ch in DiffOneColumn(oldO.Id, c, newCols[c.Name])) { yield return ch; }
        }
    }

    private static IEnumerable<SchemaChange> DiffOneColumn(string objId, Column oldC, Column newC)
    {
        var target = $"{objId}.{oldC.Name}";
        var typeChange = SqlTypeComparer.Compare(oldC.DataType, newC.DataType);
        if (typeChange is SqlTypeComparer.TypeChange.Narrowing or SqlTypeComparer.TypeChange.Incompatible)
        {
            yield return new SchemaChange("column-type-narrowed", target, ChangeRisk.Breaking, $"{oldC.DataType} -> {newC.DataType}.");
        }
        else if (typeChange == SqlTypeComparer.TypeChange.Compatible)
        {
            yield return new SchemaChange("column-type-widened", target, ChangeRisk.Safe, $"{oldC.DataType} -> {newC.DataType}.");
        }
        if (oldC.Nullable && !newC.Nullable)
        {
            yield return new SchemaChange("column-not-null-tightened", target, ChangeRisk.Breaking, "NULL -> NOT NULL breaks rows/inserts relying on NULL.");
        }
    }

    private static IEnumerable<SchemaChange> DiffForeignKeys(SqlModel oldM, SqlModel newM)
    {
        var newFkKeys = newM.ForeignKeys.Select(FkKey).ToHashSet(StringComparer.Ordinal);
        foreach (var fk in oldM.ForeignKeys.Where(fk => !newFkKeys.Contains(FkKey(fk))))
        {
            yield return new SchemaChange("fk-dropped", FkKey(fk), ChangeRisk.Degrading, $"Dropped FK {fk.Name}.");
        }
    }

    private static string FkKey(ForeignKey fk) => $"{fk.FromObjectId}->{fk.ToObjectId}:{string.Join(",", fk.FromColumns)}";
}
