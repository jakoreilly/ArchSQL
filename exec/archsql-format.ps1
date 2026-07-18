param(
    [string]$Path = '',
    [switch]$Check,
    [ValidateSet('tsql','mysql','postgres')][string]$Dialect = 'tsql'
)

if (-not $Path) {
    $Path = Read-Host 'Enter path to SQL file or folder (required)'
}
if (-not $Path) {
    Write-Error 'Path is required.'
    exit 2
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path "$scriptDir\.."
Set-Location $repoRoot

$cmdArgs = @('--format', $Path)
if ($Check) { $cmdArgs += '--check' }
if ($Dialect) { $cmdArgs += '--dialect'; $cmdArgs += $Dialect }

Write-Host "Running: dotnet run --project src/ArchSql -- $($cmdArgs -join ' ')" -ForegroundColor Cyan
dotnet run --project src/ArchSql -- @cmdArgs
