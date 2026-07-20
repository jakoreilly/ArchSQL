using System.Diagnostics;
using ArchSql.Analysis;
using ArchSql.Rendering;

namespace ArchSql.Cli;

internal static class Verbs
{
    public static int RunDefault(string[] args)
    {
        var options = CliOptions.Parse(args, out var exitCode);
        if (options is null) { return exitCode; }
        return BuildAndEmit(options);
    }

    /// <summary>Builds the model from the configured source (file scan or live connection) and
    /// writes the model.json, site, SARIF, and evaluates CI gates. Shared verbatim by the default
    /// and connect verbs so both paths behave identically once a source is chosen.</summary>
    private static int BuildAndEmit(CliOptions options)
    {
        Model.SqlModel model;
        try
        {
            model = Pipeline.BuildModel(options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {Redact.Message(ex.Message)}");
            return 1;
        }

        Directory.CreateDirectory(options.OutDir);
        var modelPath = Path.Combine(options.OutDir, "model.json");
        ModelJsonWriter.Write(model, modelPath);
        Console.Error.WriteLine($"Wrote {modelPath}");

        var indexPath = Site.SiteGenerator.Generate(model, options.OutDir, options.MaxNodes);

        if (options.SarifPath is not null)
        {
            SarifWriter.Write(model, options.SarifPath);
            Console.Error.WriteLine($"Wrote {options.SarifPath}");
        }

        if (options.FailOn.Count > 0)
        {
            var card = SqlScorecard.Build(model);
            var reasons = SqlCiGate.Evaluate(options.FailOn, card);
            if (reasons.Count > 0)
            {
                foreach (var reason in reasons) { Console.Error.WriteLine($"GATE FAILED: {reason}"); }
                return 3;
            }
        }

        if (options.Open && indexPath is not null)
        {
            try { Process.Start(new ProcessStartInfo(indexPath) { UseShellExecute = true }); }
            catch { /* best-effort; not opening the browser is not a failure */ }
        }

        return 0;
    }

    public static int RunConnect(string[] args)
    {
        // archsql connect (--conn-file <path> | --env) [--out <dir>] [--timeout <sec>]
        //   [--no-open] [--max-nodes <n>] [--fail-on <gate>...]
        var parsed = ConnectOptions.Parse(args, out var exitCode);
        if (parsed is null) { return exitCode; }
        Console.Error.WriteLine("Connecting read-only. ArchSql issues only SELECT queries and never writes — "
            + "but this is enforced by convention, not the server. Use a least-privilege read-only login.");
        return BuildAndEmit(parsed);
    }

    public static int RunFromModel(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: archsql --from-model <model.json> [--out <dir>]");
            return 2;
        }
        var model = ModelJsonReader.Read(args[1]);
        var outDir = "site-" + model.RootName;
        for (var i = 2; i < args.Length - 1; i++)
        {
            if (args[i] == "--out") { outDir = args[i + 1]; }
        }
        Directory.CreateDirectory(outDir);
        Site.SiteGenerator.Generate(model, outDir, 60);
        return 0;
    }

    public static int RunFormat(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: archsql --format <path-to-file-or-folder> [--check] [--dialect <tsql|mysql|postgres>]");
            return 2;
        }

        var path = args[1];
        var check = args.Contains("--check");
        var dialect = "tsql";
        for (var i = 2; i < args.Length - 1; i++)
        {
            if (args[i] == "--dialect") { dialect = args[i + 1]; }
        }

        var files = Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*.sql", SearchOption.AllDirectories).ToList()
            : [path];

        var analyzer = new TSqlScriptDomAnalyzer();
        var changed = 0;
        var unchanged = 0;
        var skipped = 0;

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            if (dialect != "tsql")
            {
                Console.Error.WriteLine($"formatting not available for {dialect} yet: {file}");
                skipped++;
                continue;
            }

            var formatted = analyzer.Format(content);
            if (formatted.Length == 0)
            {
                Console.Error.WriteLine($"skipped (could not parse): {file}");
                skipped++;
                continue;
            }
            if (TSqlFormatter.HasInlineComments(content))
            {
                Console.Error.WriteLine($"note: {file} has comment(s) inside a statement; those cannot be preserved by the formatter and were dropped. Statement-level comments are kept.");
            }

            if (formatted == content) { unchanged++; continue; }

            if (check)
            {
                Console.Error.WriteLine($"would reformat: {file}");
                changed++;
            }
            else
            {
                File.WriteAllText(file, formatted);
                changed++;
            }
        }

        Console.Error.WriteLine($"formatted: {changed} file(s), {unchanged} unchanged, {skipped} skipped (unparseable)");
        return check && changed > 0 ? 3 : 0;
    }

    public static int RunImpact(string[] args)
    {
        // archsql impact <schema.object> [--model <model.json>]
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: archsql impact <schema.object> [--model <model.json>]");
            return 2;
        }
        var target = args[1];
        var modelPath = "model.json";
        for (var i = 2; i < args.Length - 1; i++) { if (args[i] == "--model") { modelPath = args[i + 1]; } }
        if (!File.Exists(modelPath))
        {
            Console.Error.WriteLine($"error: model '{modelPath}' not found. Generate a site first, or pass --model.");
            return 2;
        }

        var model = ModelJsonReader.Read(modelPath);
        var dot = target.IndexOf('.');
        var schema = dot >= 0 ? target[..dot] : "";
        var name = dot >= 0 ? target[(dot + 1)..] : target;
        var id = IdentifierRules.NormalizeId(schema, name, model.Dialect == "tsql" ? "tsql" : model.Dialect);

        if (model.Objects.All(o => o.Id != id))
        {
            // Fallback: retry as a bare name under the default schema, then suggest closest matches.
            var byName = model.Objects.Where(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byName.Count == 1) { id = byName[0].Id; }
            else
            {
                Console.Error.WriteLine($"error: no object '{id}' in the model.");
                if (byName.Count > 1) { Console.Error.WriteLine($"  did you mean: {string.Join(", ", byName.Select(o => o.Id))}?"); }
                return 2;
            }
        }

        var reverse = ImpactGraph.BuildReverse(model);
        var (hits, capped) = ImpactGraph.Dependents(reverse, id);
        var execs = model.Runtime.Available
            ? model.Runtime.ObjectStats.ToDictionary(s => s.ObjectId, s => s.ExecutionCount, StringComparer.Ordinal)
            : null;
        Console.WriteLine($"Impact of changing {id}: {hits.Count} object(s) affected" + (capped ? " (depth cap hit)" : ""));
        foreach (var h in hits)
        {
            var execNote = execs is not null && execs.TryGetValue(h.ObjectId, out var e) ? $" [execs: {e:N0}]" : "";
            Console.WriteLine($"  {new string(' ', h.Depth * 2)}{h.ObjectId}  (depth {h.Depth}, via {h.ViaKind}){execNote}");
        }
        return 0;
    }

    public static int RunDiff(string[] args)
    {
        // archsql diff <old.json> <new.json> [--out <report.md>] [--html <report.html>]
        //   [--fail-on breaking-change] [--baseline <baseline.json>] [--write-baseline]
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: archsql diff <old.json> <new.json> [--out <md>] [--html <html>] [--fail-on breaking-change] [--baseline <f>] [--write-baseline]");
            return 2;
        }
        if (!File.Exists(args[1]) || !File.Exists(args[2]))
        {
            Console.Error.WriteLine("error: both model files must exist.");
            return 2;
        }

        var changes = SchemaDiff.Compute(ModelJsonReader.Read(args[1]), ModelJsonReader.Read(args[2]));

        string? outMd = null, outHtml = null, baseline = null;
        var writeBaseline = false;
        var failOnBreaking = false;
        for (var i = 3; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out" when i + 1 < args.Length: outMd = args[++i]; break;
                case "--html" when i + 1 < args.Length: outHtml = args[++i]; break;
                case "--baseline" when i + 1 < args.Length: baseline = args[++i]; break;
                case "--write-baseline": writeBaseline = true; break;
                case "--fail-on" when i + 1 < args.Length && args[i + 1] == "breaking-change": failOnBreaking = true; i++; break;
                default:
                    Console.Error.WriteLine($"error: unknown diff argument '{args[i]}'.");
                    return 2;
            }
        }

        if (writeBaseline && baseline is not null)
        {
            DiffBaseline.Write(changes, baseline);
            Console.Error.WriteLine($"Wrote baseline {baseline}");
        }
        var suppressed = baseline is not null && File.Exists(baseline) && !writeBaseline
            ? DiffBaseline.Load(baseline)
            : new HashSet<string>(StringComparer.Ordinal);

        var report = DiffReport.Markdown(changes, suppressed);
        if (outMd is not null) { File.WriteAllText(outMd, report); } else { Console.WriteLine(report); }
        if (outHtml is not null) { File.WriteAllText(outHtml, DiffReport.RenderHtml(changes, suppressed)); }

        var newBreaking = changes.Where(c => c.Risk == ChangeRisk.Breaking && !suppressed.Contains(DiffBaseline.Key(c))).ToList();
        if (failOnBreaking && newBreaking.Count > 0)
        {
            foreach (var c in newBreaking) { Console.Error.WriteLine($"GATE FAILED (breaking-change): {c.Kind} {c.Target} — {c.Detail}"); }
            return 3;
        }
        return 0;
    }
}
