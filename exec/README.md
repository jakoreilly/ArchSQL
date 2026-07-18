# exec helpers

This folder contains PowerShell helper scripts and clickable `.cmd` launchers for the `ArchSql` CLI.

## Available helpers

- `archsql-run.ps1` / `archsql-run.cmd`
  - Runs the main site generation command.
  - Parameters:
    - `-SourcePath <path>`
    - `-OutDir <dir>`
    - `-NoOpen`
    - `-MaxNodes <n>`
    - `-Exclude <dir1>,<dir2>`
    - `-Dialect <auto|tsql|mysql|postgres>`
    - `-FailOn <gate1,gate2>`
    - `-Sarif <path>`

- `archsql-format.ps1` / `archsql-format.cmd`
  - Runs the formatter mode.
  - Parameters:
    - `-Path <fileOrFolder>`
    - `-Check`
    - `-Dialect <tsql|mysql|postgres>`

- `archsql-from-model.ps1` / `archsql-from-model.cmd`
  - Regenerates a site from `model.json`.
  - Parameters:
    - `-ModelJson <path>`
    - `-OutDir <dir>`

## Usage

From PowerShell:

```powershell
cd exec
.\archsql-run.ps1 -SourcePath "..\samples\sql" -OutDir "..\out\site" -NoOpen
```

From Explorer:

Double-click `archsql-run.cmd`, `archsql-format.cmd`, or `archsql-from-model.cmd`.
