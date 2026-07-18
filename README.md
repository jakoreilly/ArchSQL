# ArchSql

> Turn a folder of SQL scripts into a beautiful, searchable architecture website.

ArchSql is a .NET 10 command-line tool that scans a folder of `.sql` files — schema dumps,
migrations, stored-procedure scripts — and produces a fully offline, static HTML site: a
foreign-key ER diagram, an object dependency graph, a SonarQube-style lint report, and a health
scorecard. It never connects to a database; it only reads the files you point it at.

**v1 scope:** T-SQL (SQL Server) is deep-parsed via
[ScriptDom](https://www.nuget.org/packages/Microsoft.SqlServer.TransactSql.ScriptDom). MySQL and
PostgreSQL files are detected but not yet analyzed (a designed, not-yet-built Tier-1 tokenizer —
see `plan.md`).

## Quick start

Prerequisites:

- .NET 10 SDK
- A folder containing SQL files to analyze

Generate a static site:

```bash
dotnet run --project src/ArchSql -- <path-to-folder> [--out <dir>] [--no-open] [--dialect <tsql|mysql|postgres|auto>] [--fail-on <gate>[,<gate>...]] [--sarif <path>]
```

Example:

```bash
dotnet run --project src/ArchSql -- tests/Fixtures --out out/site --dialect tsql
```

Format SQL files:

```bash
dotnet run --project src/ArchSql -- --format <path-to-file-or-folder> [--check]
```

Regenerate from an existing model file:

```bash
dotnet run --project src/ArchSql -- --from-model <model.json> [--out <dir>]
```

## What gets generated

- **Overview** — stat tiles, dialect mix, overall health grade, and the ER diagram
- **Objects** — every table, view, procedure, function and trigger found in the scan
- **ER Diagram** — tables and their foreign-key relationships
- **Dependencies** — which objects reference which
- **Lint** — SonarQube-style findings (security, correctness, performance, maintainability)
- **Scorecard** — a worst-wins health grade across the same signals
- **Metrics** — fan-in/fan-out coupling and procedure complexity
- **Config & Secrets** — files that embed a credential (the fact only, never the value)

Plus `model.json` (a complete, round-trippable archive) and optional SARIF 2.1.0 output for
code-scanning dashboards.

## Development

```bash
dotnet build src/ArchSql/ArchSql.csproj
dotnet test tests/ArchSql.Tests/ArchSql.Tests.csproj
```

## Project status

ArchSql is a working prototype for SQL schema analysis and static-site generation. It is suitable for experimentation and internal use, and it is being prepared for broader public sharing.

## Contributing

Please see [CONTRIBUTING.md](CONTRIBUTING.md) for development and PR guidance.

## Security

Please report vulnerabilities privately via [SECURITY.md](SECURITY.md).

## Design lineage

ArchSql's pipeline (scan → parallel analyze → serial reduce → immutable model → static site) is a
structural clone of [ArchDiagram](../ArchDiagram-main/ArchDiagram-main)'s proven architecture, with
the C#/Roslyn analysis core swapped for a SQL parsing core. See `plan.md` for the full design
rationale, phase-by-phase implementation plan, and the ArchDiagram precedents each part copies.

## License

MIT (matching the license of the ArchDiagram project this is patterned on).
