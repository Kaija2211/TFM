param(
    [ValidatePattern('^\d{4}-\d{2}-\d{2}$')]
    [string]$SnapshotDate = '2026-06-01'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$auditDirectory = Join-Path $repositoryRoot 'Temp\ClubEloAudit'
$snapshotPath = Join-Path $auditDirectory "clubelo-$SnapshotDate.csv"
$importOutput = Join-Path $repositoryRoot 'Temp\OpenFootballAudit'

New-Item -ItemType Directory -Path $auditDirectory -Force | Out-Null
if (-not (Test-Path -LiteralPath $snapshotPath)) {
    Invoke-WebRequest -Uri "http://api.clubelo.com/$SnapshotDate" -OutFile $snapshotPath -UseBasicParsing
}

dotnet run --project (Join-Path $repositoryRoot 'Tools\OpenFootballImport') -- `
    --output $importOutput `
    --club-elo-snapshot $snapshotPath

if ($LASTEXITCODE -ne 0) {
    throw "Club Elo audit failed with exit code $LASTEXITCODE."
}

Write-Output "Club Elo snapshot (development-only): $snapshotPath"
Write-Output "Calibration report: $(Join-Path $importOutput 'club_elo_audit.md')"
