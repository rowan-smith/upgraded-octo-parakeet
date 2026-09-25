# Scan commercial package output for accidental source leakage.
# Usage: pwsh packaging/Extensions/scan-commercial-package.ps1 -Path path/to/output

param(
  [Parameter(Mandatory = $true)]
  [string]$Path
)

if (-not (Test-Path $Path)) {
  Write-Error "Path not found: $Path"
  exit 1
}

$forbidden = @('*.cs', '*.ts', '*.tsx', '*.map', '*.pem')
$failures = @()

foreach ($pattern in $forbidden) {
  Get-ChildItem -Path $Path -Recurse -File -Filter $pattern -ErrorAction SilentlyContinue | ForEach-Object {
    # Allow public keys named *-public.pem only
    if ($_.Name -like '*-public.pem' -or $_.Name -like '*public*.pem') { return }
    if ($_.Extension -eq '.pem' -and $_.Name -match 'private|secret') {
      $failures += $_.FullName
      return
    }
    if ($_.Extension -ne '.pem') {
      $failures += $_.FullName
    }
  }
}

Get-ChildItem -Path $Path -Recurse -File -Filter '*.pdb' -ErrorAction SilentlyContinue | ForEach-Object {
  # Presence of PDB is a warning for commercial publish; treat as failure for scan job
  $failures += $_.FullName
}

if ($failures.Count -gt 0) {
  Write-Host "Commercial package scan FAILED. Unexpected files:"
  $failures | ForEach-Object { Write-Host "  $_" }
  exit 1
}

Write-Host "Commercial package scan OK: $Path"
exit 0
