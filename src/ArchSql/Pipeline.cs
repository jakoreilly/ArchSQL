using System.Security.Cryptography;
using System.Text;
using ArchSql.Analysis;
using ArchSql.Cli;
using ArchSql.Live;
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
        var source = SchemaSourceFactory.Create(options, diagnostics);
        var units = source.Read().ToList(); // materialize once: Read() enumerates the disk scan or live catalog

        var analyzers = new List<ISqlDialectAnalyzer> { new TSqlScriptDomAnalyzer() };

        var results = new FileResult[units.Count];
        Parallel.For(0, units.Count, idx => { results[idx] = AnalyzeOne(units[idx], analyzers, options); });

        // Serial reduce — order-dependent shared state, kept serial for deterministic output.
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

        // Exclusion runs BEFORE dedup: an excluded object never reaches the duplicate-id check, so
        // excluding a redundant copy produces neither a false collision warning nor a dropped
        // surviving object.
        if (options.ExcludePatterns.Count > 0)
        {
            ApplyExclusions(options.ExcludePatterns, objects, foreignKeys, dependencies, diagnostics);
        }

        // Object ids must be unique — the resolver and every id-keyed lookup depend on it. A
        // brownfield database can still yield collisions (e.g. two modules whose authored definition
        // text names the same schema.object), so keep the first occurrence and record the rest as
        // diagnostics rather than crashing.
        objects = ModelNormalizer.DedupeObjects(objects, diagnostics);

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
            SchemaVersion = SchemaVersions.Current,
        };

        model = DependencyResolver.Resolve(model);
        model = ApplyPurposeAndComplexity(model);
        model = model with { Crud = CrudMatrix.Build(model) };

        // Runtime facts join to resolved object ids, so enrich after the reduce and outside the
        // parallel map. Only a live source supplies them; file scans leave Runtime empty. Attached
        // BEFORE SqlRules so rules can use runtime evidence (e.g. an object that actually executed
        // is not "dead").
        if (source is IRuntimeStatsSource live)
        {
            model = model with { Runtime = live.ReadRuntime() };
        }
        if (options.ExcludePatterns.Count > 0 && model.Runtime.Available)
        {
            model = model with { Runtime = FilterRuntimeByExcludedIds(model) };
        }

        // Column width/precision/collation, static index definitions, and row counts join to
        // resolved objects by id, so merge after the reduce, same as runtime facts.
        if (source is ICatalogDetailSource catalog)
        {
            model = CatalogDetailMerge.Merge(model, catalog.ReadCatalogDetail());
        }

        model = model with { Findings = SqlRules.Run(model) };
        return model;
    }

    /// <summary>Drops objects matching any exclude pattern (by name or full id) and every FK/
    /// dependency touching them. Mutates the lists in place — called once, before dedup.</summary>
    private static void ApplyExclusions(List<string> patterns, List<DbObject> objects, List<ForeignKey> foreignKeys, List<ObjectDep> dependencies, List<string> diagnostics)
    {
        var excludedIds = objects
            .Where(o => patterns.Any(p => Analysis.Glob.IsMatch(o.Name, p) || Analysis.Glob.IsMatch(o.Id, p)))
            .Select(o => o.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (excludedIds.Count == 0) { return; }

        objects.RemoveAll(o => excludedIds.Contains(o.Id));
        foreignKeys.RemoveAll(fk => excludedIds.Contains(fk.FromObjectId) || excludedIds.Contains(fk.ToObjectId));
        dependencies.RemoveAll(d => excludedIds.Contains(d.FromObjectId) || excludedIds.Contains(d.ToObjectId));
        diagnostics.Add($"Excluded {excludedIds.Count} object(s) matching {patterns.Count} pattern(s): {string.Join(", ", patterns)}.");
    }

    /// <summary>Runtime facts are fetched after object exclusion decided which ids survive, so this
    /// only needs to drop stats whose object was excluded (belt-and-braces: a live source's DMV rows
    /// join by id, and an excluded id simply won't appear in model.Objects any more).</summary>
    private static RuntimeStats FilterRuntimeByExcludedIds(SqlModel model)
    {
        var survivingIds = model.Objects.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        var rt = model.Runtime;
        return rt with
        {
            ObjectStats = rt.ObjectStats.Where(s => survivingIds.Contains(s.ObjectId)).ToList(),
            IndexStats = rt.IndexStats.Where(s => survivingIds.Contains(s.ObjectId)).ToList(),
            MissingIndexes = rt.MissingIndexes.Where(m => survivingIds.Contains(m.ObjectId)).ToList(),
        };
    }

    /// <summary>Fills Purpose text (objects+files) and per-object Cyclomatic complexity — both
    /// pure post-processing passes over the resolved model, so they run once, after resolution,
    /// rather than duplicated per-file during the parallel map.</summary>
    private static SqlModel ApplyPurposeAndComplexity(SqlModel model)
    {
        var objects = model.Objects
            // Procedures/functions/triggers get their complexity from the analyzer (which has the
            // token source); views/others fall back to scoring their stored Body here.
            .Select(o => o with { Cyclomatic = o.Cyclomatic > 0 ? o.Cyclomatic : SqlMetrics.Cyclomatic(o.Body) })
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
                // Only T-SQL is deep-analyzed; mysql/postgres are detected but not parsed.
                diagnostics.Add($"{unit.RelPath}: detected as {unit.Dialect}; only T-SQL is deep-analyzed.");
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
