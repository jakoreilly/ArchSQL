using System.Text.Json;
using ArchSql.Model;

namespace ArchSql.Rendering;

public static class ModelJsonReader
{
    public static SqlModel Read(string path)
    {
        var json = File.ReadAllText(path);
        var model = JsonSerializer.Deserialize<SqlModel>(json, ModelJsonWriter.Options)
            ?? throw new InvalidDataException($"'{path}' did not deserialize to a SqlModel.");
        return ModelUpgrader.Upgrade(model);
    }
}
