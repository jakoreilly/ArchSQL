namespace ArchSql.Scanning;

/// <summary>One scanned file: absolute + root-relative path, extension, size. Sorted output is
/// load-bearing (Constraint 3, determinism) — callers must not re-sort with a different comparer.</summary>
public sealed record FileEntry(string AbsPath, string RelPath, string Extension, long SizeBytes);

public static class SqlFileScanner
{
    private static readonly string[] SqlExtensions = [".sql", ".ddl", ".tsql", ".psql"];
    private static readonly string[] AlwaysSkipDirs = [".git", "bin", "obj", "node_modules"];

    public static List<FileEntry> Scan(string root, IReadOnlyList<string> exclude, List<string> diagnostics)
    {
        var skip = new HashSet<string>(AlwaysSkipDirs, StringComparer.OrdinalIgnoreCase);
        foreach (var e in exclude) { skip.Add(e); }

        var entries = new List<FileEntry>();
        WalkDirectory(root, root, skip, entries, diagnostics);
        entries.Sort((a, b) => string.CompareOrdinal(a.RelPath, b.RelPath));
        return entries;
    }

    private static void WalkDirectory(string root, string dir, HashSet<string> skip, List<FileEntry> entries, List<string> diagnostics)
    {
        IEnumerable<string> subDirs;
        IEnumerable<string> files;
        try
        {
            subDirs = Directory.EnumerateDirectories(dir);
            files = Directory.EnumerateFiles(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add($"Could not read directory {dir}: {ex.Message}");
            return;
        }

        foreach (var file in files)
        {
            var ext = Path.GetExtension(file);
            if (!SqlExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) { continue; }
            var relPath = Path.GetRelativePath(root, file).Replace('\\', '/');
            long size;
            try { size = new FileInfo(file).Length; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add($"Could not stat {relPath}: {ex.Message}");
                continue;
            }
            entries.Add(new FileEntry(file, relPath, ext, size));
        }

        foreach (var sub in subDirs)
        {
            var name = Path.GetFileName(sub);
            if (skip.Contains(name)) { continue; }
            WalkDirectory(root, sub, skip, entries, diagnostics);
        }
    }
}
