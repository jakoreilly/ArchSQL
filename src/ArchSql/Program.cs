// ArchSql — point it at a folder of .sql files, get a fully-offline static HTML documentation
// site: schema inventory, foreign-key ER diagram, object dependency graph, a SonarQube-style
// lint report, and a health scorecard. v1 deep-analyzes T-SQL (MSSQL); MySQL/PostgreSQL files are
// detected but not parsed yet (Phase 2b, deferred). Nothing is fetched at runtime; no database
// connection is ever opened.
//
// Usage: archsql <path-to-folder> [--out <dir>] [--no-open] [--max-nodes <n>] [--exclude <dirname>]...
using ArchSql.Cli;

if (args.Length > 0 && args[0] == "--from-model") { return Verbs.RunFromModel(args); }
if (args.Length > 0 && args[0] == "--format") { return Verbs.RunFormat(args); }
return Verbs.RunDefault(args);
