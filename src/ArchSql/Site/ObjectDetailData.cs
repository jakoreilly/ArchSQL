using System.Text.Json;
using ArchSql.Analysis;
using ArchSql.Model;

namespace ArchSql.Site;

/// <summary>Builds the per-object detail payload (columns, primary key, lint findings, purpose,
/// source availability) that object.html reads via window.ARCH_OBJDETAIL. Kept separate from
/// GraphData's lean node/edge payload so the graph used by Explore/neighborhood stays small; this
/// one is read once per object.html visit, keyed by id. Written once as a shared asset.</summary>
public static class ObjectDetailData
{
    public static void WriteAsset(SiteContext ctx, string outDir)
    {
        var model = ctx.Model;
        var findingsByObject = model.Findings.Where(f => f.ObjectId.Length > 0).ToLookup(f => f.ObjectId, StringComparer.Ordinal);

        var detail = model.Objects.ToDictionary(
            o => o.Id,
            o => new
            {
                schema = o.Schema,
                name = o.Name,
                kind = o.Kind,
                purpose = SqlPurpose.ForObject(o),
                columns = o.Columns.Select(c => new
                {
                    name = c.Name,
                    type = c.DataType,
                    nullable = c.Nullable,
                    pk = o.PrimaryKey.Contains(c.Name, StringComparer.OrdinalIgnoreCase),
                }).ToList(),
                findings = findingsByObject[o.Id].Select(f => new { ruleId = f.RuleId, severity = f.Severity, message = f.Message }).ToList(),
                fileHref = o.DefinedInSlug.Length > 0 ? $"files/{o.DefinedInSlug}.html" : "",
            },
            StringComparer.Ordinal);

        var json = JsonSerializer.Serialize(detail);
        var assetsDir = Path.Combine(outDir, "assets");
        Directory.CreateDirectory(assetsDir);
        File.WriteAllText(Path.Combine(assetsDir, "object-detail.js"), $"window.ARCH_OBJDETAIL={json};");
    }

    public static string ScriptSrc(string relRoot) => $"<script src=\"{relRoot}assets/object-detail.js\"></script>";
}
