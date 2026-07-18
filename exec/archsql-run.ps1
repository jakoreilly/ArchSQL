param(
    [string]$SourcePath = '',
    [string]$OutDir = '',
    [switch]$NoOpen,
    [int]$MaxNodes = 60,
    [string[]]$Exclude = @(),
    [ValidateSet('auto','tsql','mysql','postgres')][string]$Dialect = 'auto',
    [string]$FailOn = '',
    [string]$Sarif = ''
)

if (-not $SourcePath) {
    $SourcePath = Read-Host 'Enter path to SQL folder (required)'
}
if (-not $SourcePath) {
    Write-Error 'Source path is required.'
    exit 2
}

if (-not $Exclude -or $Exclude.Count -eq 0) {
    $excludeInput = Read-Host 'Exclude directories (comma-separated, optional)'
    if ($excludeInput) {
        $Exclude = $excludeInput -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }
    }
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path "$scriptDir\.."
Set-Location $repoRoot

$cmdArgs = @()
$cmdArgs += $SourcePath
if ($OutDir) { $cmdArgs += '--out'; $cmdArgs += $OutDir }
if ($NoOpen) { $cmdArgs += '--no-open' }
if ($MaxNodes -ne 60) { $cmdArgs += '--max-nodes'; $cmdArgs += $MaxNodes }
foreach ($ex in $Exclude) { $cmdArgs += '--exclude'; $cmdArgs += $ex }
if ($Dialect) { $cmdArgs += '--dialect'; $cmdArgs += $Dialect }
if ($FailOn) { $cmdArgs += '--fail-on'; $cmdArgs += $FailOn }
if ($Sarif) { $cmdArgs += '--sarif'; $cmdArgs += $Sarif }

Write-Host "Running: dotnet run --project src/ArchSql -- $($cmdArgs -join ' ')" -ForegroundColor Cyan
dotnet run --project src/ArchSql -- @cmdArgs
