using ArchSql.Model;

namespace ArchSql.Analysis;

/// <summary>Resolves object-to-object references across files: for every ForeignKey/ObjectDep
/// whose target wasn't known at parse time (single-file scope), looks up the real object by Id
/// and either fills ToObjectId or records ExternalTarget.
/// Pure/deterministic — same input always resolves the same way.</summary>
public static class DependencyResolver
{
    public static SqlModel Resolve(SqlModel model)
    {
        var byId = model.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);

        var foreignKeys = model.ForeignKeys.Select(fk => ResolveForeignKey(fk, byId)).ToList();
        var dependencies = model.Dependencies.Select(d => ResolveDep(d, byId)).ToList();

        // Populate DbObject.ReferencesObjectIds from the resolved deps + FKs, sorted for determinism.
        var refsByFrom = dependencies.Concat(foreignKeys.Select(fk => new ObjectDep
            {
                FromObjectId = fk.FromObjectId,
                ToObjectId = fk.ToObjectId,
                Kind = "fk",
            }))
            .Where(d => d.ToObjectId.Length > 0)
            .GroupBy(d => d.FromObjectId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(d => d.ToObjectId).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList());

        var objects = model.Objects
            .Select(o => o with { ReferencesObjectIds = refsByFrom.GetValueOrDefault(o.Id) ?? [] })
            .ToList();

        return model with { Objects = objects, ForeignKeys = foreignKeys, Dependencies = dependencies };
    }

    private static ForeignKey ResolveForeignKey(ForeignKey fk, Dictionary<string, DbObject> byId) =>
        byId.ContainsKey(fk.ToObjectId) ? fk : fk with { ToObjectId = "" };

    private static ObjectDep ResolveDep(ObjectDep dep, Dictionary<string, DbObject> byId)
    {
        if (dep.ToObjectId.Length == 0) { return dep; }
        return byId.ContainsKey(dep.ToObjectId)
            ? dep
            : dep with { ExternalTarget = dep.ToObjectId, ToObjectId = "" };
    }
}
