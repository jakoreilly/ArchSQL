using ArchSql.Model;

namespace ArchSql.Analysis;

/// <summary>Infers likely table relationships from column-naming conventions, for databases where
/// referential integrity is not declared as foreign keys. Makes no assumption about which naming
/// convention is in use (camelCase, snake_case, or no separator at all) and emits nothing when a
/// column's stem matches more than one table — an ambiguous guess is worse than no guess. Pure and
/// deterministic.</summary>
public static class InferredRelationships
{
    public sealed record Relationship(string FromObjectId, string FromColumn, string ToObjectId, string Confidence);

    private static readonly char[] SeparatorChars = ['_', '-', '.', ' '];

    public static List<Relationship> Compute(SqlModel model)
    {
        var tables = model.Objects.Where(o => o.Kind == "table").ToList();
        if (tables.Count == 0) { return []; }

        // Canonical (singularized, lowercase) name -> the table ids that produce it. A canonical
        // name produced by more than one table is ambiguous and is never matched against.
        var byCanonical = tables
            .GroupBy(t => CanonicalSingular(t.Name), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var declaredFks = model.ForeignKeys.Select(fk => (fk.FromObjectId, fk.ToObjectId)).ToHashSet();

        var results = new List<Relationship>();
        foreach (var table in tables)
        {
            foreach (var column in table.Columns)
            {
                var match = MatchColumn(column.Name, table, byCanonical);
                if (match is null) { continue; }
                if (declaredFks.Contains((table.Id, match.Value.TableId))) { continue; }
                results.Add(new Relationship(table.Id, column.Name, match.Value.TableId, match.Value.Confidence));
            }
        }

        return results
            .OrderBy(r => r.FromObjectId, StringComparer.Ordinal).ThenBy(r => r.FromColumn, StringComparer.Ordinal)
            .ToList();
    }

    private static (string TableId, string Confidence)? MatchColumn(string columnName, DbObject owner, Dictionary<string, List<DbObject>> byCanonical)
    {
        var lower = columnName.ToLowerInvariant();
        if (lower.Length <= 2 || !lower.EndsWith("id", StringComparison.Ordinal)) { return null; }

        var stem = columnName[..^2].TrimEnd(SeparatorChars);
        if (stem.Length == 0) { return null; }

        var canonicalStem = CanonicalSingular(stem);
        if (!byCanonical.TryGetValue(canonicalStem, out var matches) || matches.Count != 1) { return null; }

        var target = matches[0];
        if (target.Id == owner.Id) { return null; } // a table's own id-like column is its own key, not a reference

        var confidence = stem.Equals(target.Name, StringComparison.OrdinalIgnoreCase) ? "high" : "medium";
        return (target.Id, confidence);
    }

    /// <summary>A rough, convention-agnostic singularization: lowercases and strips a common plural
    /// ending. Good enough to unify "Order"/"Orders" or "Category"/"Categories" style pairs without
    /// assuming any particular naming scheme.</summary>
    private static string CanonicalSingular(string name)
    {
        var s = name.ToLowerInvariant();
        if (s.EndsWith("ies", StringComparison.Ordinal) && s.Length > 3) { return s[..^3] + "y"; }
        if (s.EndsWith("es", StringComparison.Ordinal) && s.Length > 2
            && (s.EndsWith("ses", StringComparison.Ordinal) || s.EndsWith("xes", StringComparison.Ordinal)
                || s.EndsWith("ches", StringComparison.Ordinal) || s.EndsWith("shes", StringComparison.Ordinal)))
        {
            return s[..^2];
        }
        if (s.EndsWith('s') && !s.EndsWith("ss", StringComparison.Ordinal) && s.Length > 1) { return s[..^1]; }
        return s;
    }
}
