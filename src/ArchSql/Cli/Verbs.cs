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

        Model.SqlModel model;
        try
        {
            model = Pipeline.BuildModel(options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
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
}
