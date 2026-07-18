using System.Security.Cryptography;
using System.Text;
using ArchSql.Analysis;
using ArchSql.Cli;
using ArchSql.Model;

namespace ArchSql;

public static class Pipeline
{
    private const long MaxAnalyzeBytes = 1024 * 1024; // skip deep analysis beyond 1 MB
    private const long EstimatedBytesPerLine = 40;

    /// <summary>Lines in a text: newline count, plus one for a final unterminated line.</summary>
    internal static int CountLines(string content)
    {
        if (content.Length == 0) { return 0; }
        var newlines = content.AsSpan().Count('\n');
        return content[^1] == '\n' ? newlines : newlines + 1;
    }

    /// <summary>Everything one file's analysis learns, before slug/id assignment (assigned
    /// serially in the reduce pass, since slug de-dup depends on processing order). No shared
    /// mutable state — safe to compute on any thread.</summary>
    private readonly record struct FileResult(SqlFile FileNoSlug, SqlFileFacts Facts);

    public static SqlModel BuildModel(CliOptions options)
    {
        var diagnostics = new List<string>();
        var source = new SqlFileSchemaSource(options.SourcePath, options.Exclude, options.ForceDialect, diagnostics);
        var units = source.Read().ToList(); // materialize once: Read() enumerates the disk scan

        var analyzers = new List<ISqlDialectAnalyzer> { new TSqlScriptDomAnalyzer() };

        var results = new FileResult[units.Count];
        Parallel.For(0, units.Count, idx => { results[idx] = AnalyzeOne(units[idx], analyzers, options); });

        // Serial reduce — order-dependent shared state (determinism, Hard Constraint 3).
        var files = new List<SqlFile>(units.Count);
        var objects = new List<DbObject>();
        var foreignKeys = new List<ForeignKey>();
        var dependencies = new List<ObjectDep>();
        var usedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dialectLoc = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var r in results)
        {
            diagnostics.AddRange(r.Facts.Diagnostics);
            var slug = MakeSlug(r.FileNoSlug.RelPath, usedSlugs);
            var objectIds = r.Facts.Objects.Select(o => o.Id).ToList();
            files.Add(r.FileNoSlug with { Slug = slug, ObjectIds = objectIds });
            // DefinedInSlug arrives from the analyzer holding the file's RelPath (slugs aren't
            // assigned until this serial reduce); rewrite it to the real slug now.
            objects.AddRange(r.Facts.Objects.Select(o => o with { DefinedInSlug = slug }));
            foreignKeys.AddRange(r.Facts.ForeignKeys);
            dependencies.AddRange(r.Facts.Dependencies);
            if (r.FileNoSlug.Loc > 0)
            {
                dialectLoc[r.FileNoSlug.Dialect] = dialectLoc.GetValueOrDefault(r.FileNoSlug.Dialect) + r.FileNoSlug.Loc;
            }
        }

        var rootName = Path.GetFileName(options.SourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(rootName)) { rootName = "project"; }

        var overallDialect = dialectLoc.Count == 0 ? "unknown"
            : dialectLoc.Count == 1 ? dialectLoc.Keys.First()
            : "mixed";

        var model = new SqlModel
        {
            RootName = rootName,
            SourcePath = options.SourcePath,
            Files = files,
            Objects = objects,
            ForeignKeys = foreignKeys,
            Dependencies = dependencies,
            Diagnostics = diagnostics,
            DialectLoc = dialectLoc,
            Dialect = overallDialect,
        };

        model = DependencyResolver.Resolve(model);
        model = ApplyPurposeAndComplexity(model);
        model = model with { Findings = SqlRules.Run(model) };
        return model;
    }

    /// <summary>Fills Purpose text (objects+files) and per-object Cyclomatic complexity — both
    /// pure post-processing passes over the resolved model, so they run once, after resolution,
    /// rather than duplicated per-file during the parallel map.</summary>
    private static SqlModel ApplyPurposeAndComplexity(SqlModel model)
    {
        var objects = model.Objects
            .Select(o => o with { Cyclomatic = SqlMetrics.Cyclomatic(o.Body) })
            .ToList();

        var objectsBySlug = objects.ToLookup(o => o.DefinedInSlug, StringComparer.Ordinal);
        var files = model.Files
            .Select(f => f with { Purpose = SqlPurpose.ForFile(f, objectsBySlug[f.Slug].ToList()) })
            .ToList();

        return model with { Objects = objects, Files = files };
    }

    private static FileResult AnalyzeOne((string RelPath, string Content, string Dialect) unit, List<ISqlDialectAnalyzer> analyzers, CliOptions options)
    {
        var content = unit.Content;
        var sizeBytes = Encoding.UTF8.GetByteCount(content);
        var loc = 0;
        var diagnostics = new List<string>();
        SqlFileFacts facts;

        if (sizeBytes > MaxAnalyzeBytes)
        {
            loc = (int)(sizeBytes / EstimatedBytesPerLine);
            diagnostics.Add($"Skipped deep analysis of {unit.RelPath} ({sizeBytes / (1024.0 * 1024.0):F1} MB exceeds 1 MB limit); LOC estimated from size.");
            facts = new SqlFileFacts { Dialect = unit.Dialect, ParsedCleanly = false, Diagnostics = diagnostics };
        }
        else
        {
            loc = CountLines(content);
            var analyzer = analyzers.FirstOrDefault(a => a.CanHandle(unit.Dialect));
            if (analyzer is null)
            {
                // v1 only deep-analyzes T-SQL; mysql/postgres are detected but not parsed (Phase 2b, deferred).
                diagnostics.Add($"{unit.RelPath}: detected as {unit.Dialect}; only T-SQL is analyzed in v1.");
                facts = new SqlFileFacts { Dialect = unit.Dialect, ParsedCleanly = false, Diagnostics = diagnostics };
            }
            else
            {
                try
                {
                    facts = analyzer.Analyze(unit.RelPath, content);
                }
                catch (Exception ex)
                {
                    diagnostics.Add($"Analysis failed for {unit.RelPath}: {ex.Message}");
                    facts = new SqlFileFacts { Dialect = unit.Dialect, ParsedCleanly = false, Diagnostics = diagnostics };
                }
            }
        }

        var file = new SqlFile
        {
            RelPath = unit.RelPath,
            Slug = "", // assigned serially in BuildModel's reduce pass
            Dialect = unit.Dialect,
            SizeBytes = sizeBytes,
            Loc = loc,
            StatementCount = facts.StatementCount,
            ParsedCleanly = facts.ParsedCleanly,
            HasCredential = facts.HasCredential,
        };

        return new FileResult(file, facts);
    }

    /// <summary>Slug: relative path with non-alphanumerics -> '_', capped at 100 chars with an
    /// 8-char hash suffix on overflow or collision.</summary>
    public static string MakeSlug(string relPath, HashSet<string> used)
    {
        var sb = new StringBuilder(relPath.Length);
        foreach (var ch in relPath)
        {
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }
        var slug = sb.ToString();
        if (slug.Length > 100 || !used.Add(slug))
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(relPath)))[..8];
            slug = (slug.Length > 91 ? slug[..91] : slug) + "_" + hash;
            var candidate = slug;
            var i = 2;
            while (!used.Add(candidate)) { candidate = slug + "_" + i++; }
            slug = candidate;
        }
        return slug;
    }
}
