using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArchSql.Analysis;

/// <summary>Suppression file for the schema differ: SHA256 of (kind + normalized target), never a
/// line number — lines shift on every edit and would make a baseline useless.</summary>
public static class DiffBaseline
{
    public static string Key(SchemaChange c) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(c.Kind + "|" + c.Target)));

    public static void Write(IEnumerable<SchemaChange> changes, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(changes.Select(Key).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList()));

    public static HashSet<string> Load(string path) =>
        JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path))?.ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
}
