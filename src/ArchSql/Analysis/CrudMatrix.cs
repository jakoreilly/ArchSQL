using ArchSql.Model;

namespace ArchSql.Analysis;

/// <summary>Reduces typed ObjectDep records into the CrudEntry projection. Pure and deterministic:
/// the same deps always fold to the same sorted entries. No ScriptDom here, so it is fully
/// unit-testable with hand-built ObjectDep lists.</summary>
public static class CrudMatrix
{
    // Read comes from "read"; write kinds map to their letters. fk/exec/select-star/*-nowhere/
    // exec-dynamic are not CRUD letters (fk and exec are structural, not CRUD; exec-dynamic is
    // handled separately as a blind spot below).
    private static char? Letter(string kind) => kind switch
    {
        "read" => 'R',
        "insert" => 'C',
        "update" => 'U',
        "delete" => 'D',
        _ => null,
    };

    public static List<CrudEntry> Build(SqlModel model)
    {
        var ops = new Dictionary<(string Actor, string Target), SortedSet<char>>();
        var blind = new HashSet<string>(StringComparer.Ordinal);

        foreach (var d in model.Dependencies)
        {
            if (d.Kind == "exec-dynamic") { blind.Add(d.FromObjectId); continue; }
            var letter = Letter(d.Kind);
            if (letter is null || d.ToObjectId.Length == 0) { continue; }
            var key = (d.FromObjectId, d.ToObjectId);
            if (!ops.TryGetValue(key, out var set)) { set = new SortedSet<char>(); ops[key] = set; }
            set.Add(letter.Value);
        }

        var entries = ops
            .Select(kv => new CrudEntry { Actor = kv.Key.Actor, Target = kv.Key.Target, Ops = new string(kv.Value.ToArray()) })
            .Concat(blind.Select(a => new CrudEntry { Actor = a, Target = "", Ops = "?", IsBlindSpot = true }))
            .OrderBy(e => e.Target, StringComparer.Ordinal).ThenBy(e => e.Actor, StringComparer.Ordinal)
            .ToList();
        return entries;
    }
}
