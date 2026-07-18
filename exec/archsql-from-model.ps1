param(
    [string]$ModelJson = '',
    [string]$OutDir = ''
)

if (-not $ModelJson) {
    $ModelJson = Read-Host 'Enter path to model.json (required)'
}
if (-not $ModelJson) {
    Write-Error 'Model JSON path is required.'
    exit 2
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path "$scriptDir\.."
Set-Location $repoRoot

$cmdArgs = @('--from-model', $ModelJson)
if ($OutDir) { $cmdArgs += '--out'; $cmdArgs += $OutDir }

Write-Host "Running: dotnet run --project src/ArchSql -- $($cmdArgs -join ' ')" -ForegroundColor Cyan
dotnet run --project src/ArchSql -- @cmdArgs
