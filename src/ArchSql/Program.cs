// ArchSql — point it at a folder of .sql files (or, with `connect`, a live SQL Server), get a
// fully-offline static HTML documentation site: schema inventory, foreign-key ER diagram, object
// dependency graph, CRUD matrix, impact analysis, a SonarQube-style lint report, and a health
// scorecard. The `connect` verb additionally reads runtime facts (execution stats, index usage,
// missing indexes) from DMVs, read-only. The default folder-scan path opens no connection.
//
// Usage: archsql <path-to-folder> [--out <dir>] [--no-open] [--max-nodes <n>] [--exclude <dirname>]...
//        archsql connect (--conn-file <path> | --env) [--out <dir>] [--timeout <sec>] ...
using ArchSql.Cli;

if (args.Length > 0 && args[0] == "--from-model") { return Verbs.RunFromModel(args); }
if (args.Length > 0 && args[0] == "--format") { return Verbs.RunFormat(args); }
if (args.Length > 0 && args[0] == "connect") { return Verbs.RunConnect(args); }
if (args.Length > 0 && args[0] == "impact") { return Verbs.RunImpact(args); }
if (args.Length > 0 && args[0] == "diff") { return Verbs.RunDiff(args); }
return Verbs.RunDefault(args);
