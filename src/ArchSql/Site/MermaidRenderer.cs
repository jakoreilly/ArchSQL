using System.Text;
using ArchSql.Model;

namespace ArchSql.Site;

/// <summary>Builds Mermaid diagram source for the ER and object-dependency views. Diagrams are
/// capped at maxNodes (by fan-in+fan-out, most-connected first) for readability.</summary>
public static class MermaidRenderer
{
    public static string BuildEr(SqlModel model, int maxNodes)
    {
        var tables = model.Objects.Where(o => o.Kind == "table").ToList();
        var fks = model.ForeignKeys.Where(fk => fk.ToObjectId.Length > 0).ToList();
        var (shown, trimmed) = Cap(tables, fks, maxNodes);

        var sb = new StringBuilder();
        sb.Append("erDiagram\n");
        foreach (var t in shown)
        {
            var safeId = SafeId(t.Id);
            sb.Append($"  {safeId}[\"{Escape(t.Schema)}.{Escape(t.Name)}\"] {{\n");
            foreach (var c in t.Columns.Take(20))
            {
                var pk = t.PrimaryKey.Contains(c.Name, StringComparer.OrdinalIgnoreCase) ? "PK" : "";
                sb.Append($"    {SafeType(c.DataType)} {SafeCol(c.Name)} {pk}\n");
            }
            sb.Append("  }\n");
        }
        var shownIds = shown.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var fk in fks.Where(fk => shownIds.Contains(fk.FromObjectId) && shownIds.Contains(fk.ToObjectId)))
        {
            sb.Append($"  {SafeId(fk.ToObjectId)} ||--o{{ {SafeId(fk.FromObjectId)} : \"{Escape(fk.Name)}\"\n");
        }
        return trimmed ? sb.ToString() + $"\n%% showing {shown.Count} of {tables.Count} tables (most-connected first)\n" : sb.ToString();
    }

    /// <summary>Logical ER built from InferredRelationships rather than declared foreign keys —
    /// for a database where referential integrity is not declared, this is often the only view of
    /// how tables actually relate. Inferred edges are drawn dashed (non-identifying) to distinguish
    /// them from a real, declared foreign key.</summary>
    public static string BuildInferredEr(SqlModel model, List<Analysis.InferredRelationships.Relationship> relationships, int maxNodes)
    {
        var tables = model.Objects.Where(o => o.Kind == "table").ToList();
        var touched = relationships.SelectMany(r => new[] { r.FromObjectId, r.ToObjectId }).ToHashSet(StringComparer.Ordinal);
        var candidates = tables.Where(t => touched.Contains(t.Id)).ToList();
        var (shown, trimmed) = Cap(candidates.Count > 0 ? candidates : tables, relationships, maxNodes);
        var shownIds = shown.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);

        var sb = new StringBuilder();
        sb.Append("erDiagram\n");
        foreach (var t in shown)
        {
            sb.Append($"  {SafeId(t.Id)}[\"{Escape(t.Schema)}.{Escape(t.Name)}\"] {{\n    string _ \"\"\n  }}\n");
        }
        foreach (var r in relationships.Where(r => shownIds.Contains(r.FromObjectId) && shownIds.Contains(r.ToObjectId)))
        {
            sb.Append($"  {SafeId(r.ToObjectId)} |o..o{{ {SafeId(r.FromObjectId)} : \"{Escape(r.FromColumn)} ({r.Confidence})\"\n");
        }
        return trimmed ? sb.ToString() + $"\n%% showing {shown.Count} of {candidates.Count} related tables (most-connected first)\n" : sb.ToString();
    }

    public static string BuildDependencies(SqlModel model, int maxNodes)
    {
        var objects = model.Objects;
        var deps = model.Dependencies.Where(d => d.ToObjectId.Length > 0 && d.Kind is "read" or "insert" or "update" or "delete" or "exec" or "fk").ToList();
        var (shown, _) = Cap(objects, deps, maxNodes);
        var shownIds = shown.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);

        var sb = new StringBuilder();
        sb.Append("flowchart LR\n");
        foreach (var o in shown)
        {
            var shape = o.Kind switch
            {
                "table" => $"{SafeId(o.Id)}[\"{Escape(o.Schema)}.{Escape(o.Name)}\"]",
                "view" => $"{SafeId(o.Id)}(\"{Escape(o.Schema)}.{Escape(o.Name)}\")",
                _ => $"{SafeId(o.Id)}{{{{\"{Escape(o.Schema)}.{Escape(o.Name)}\"}}}}",
            };
            sb.Append($"  {shape}\n");
        }
        foreach (var d in deps.Where(d => shownIds.Contains(d.FromObjectId) && shownIds.Contains(d.ToObjectId)).DistinctBy(d => (d.FromObjectId, d.ToObjectId)))
        {
            sb.Append($"  {SafeId(d.FromObjectId)} --> {SafeId(d.ToObjectId)}\n");
        }
        return sb.ToString();
    }

    private static (List<DbObject> Shown, bool Trimmed) Cap<TEdge>(List<DbObject> objects, List<TEdge> edges, int maxNodes)
    {
        if (objects.Count <= maxNodes) { return (objects, false); }
        // Rank by connectivity (approximate: count of edges touching the object's Id via reflection-free lookup).
        var touch = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var o in objects) { touch[o.Id] = 0; }
        foreach (var e in edges)
        {
            var (from, to) = e switch
            {
                ForeignKey fk => (fk.FromObjectId, fk.ToObjectId),
                ObjectDep d => (d.FromObjectId, d.ToObjectId),
                Analysis.InferredRelationships.Relationship r => (r.FromObjectId, r.ToObjectId),
                _ => ("", ""),
            };
            if (touch.ContainsKey(from)) { touch[from]++; }
            if (touch.ContainsKey(to)) { touch[to]++; }
        }
        var ranked = objects.OrderByDescending(o => touch.GetValueOrDefault(o.Id)).ThenBy(o => o.Id, StringComparer.Ordinal).Take(maxNodes).ToList();
        return (ranked, true);
    }

    private static string SafeId(string id) => "n" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)))[..12];
    private static string SafeCol(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray());
    private static string SafeType(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()) is { Length: > 0 } t ? t : "unknown";
    private static string Escape(string s) => s.Replace("\"", "'");
}
