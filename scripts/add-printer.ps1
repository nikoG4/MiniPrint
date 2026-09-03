[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Server,
    [Parameter(Mandatory)]
    [string]$PrinterSlug,
    [Parameter(Mandatory)]
    [string]$DisplayName,
    [ValidateRange(1, 65535)]
    [int]$Port = 631
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PrinterSlug -notmatch '^[a-z0-9-]+$') {
    throw 'PrinterSlug may contain only lowercase letters, numbers and hyphens.'
}

$IppUrl = "http://$Server`:$Port/ipp/printers/$PrinterSlug"
$HealthUrl = "http://$Server`:$Port/health"

try {
    Invoke-RestMethod -Uri $HealthUrl -Method Get -TimeoutSec 5 | Out-Null
}
catch {
    throw "MiniPrint is not reachable at $HealthUrl. $($_.Exception.Message)"
}

$AddPrinter = Get-Command Add-Printer -ErrorAction SilentlyContinue
if ($AddPrinter -and $AddPrinter.Parameters.ContainsKey('IppURL')) {
    Add-Printer -Name $DisplayName -IppURL $IppUrl
    Write-Host "Printer added: $DisplayName" -ForegroundColor Green
    exit 0
}

Write-Warning 'This Windows version does not expose Add-Printer -IppURL.'
Set-Clipboard -Value $IppUrl -ErrorAction SilentlyContinue
Start-Process 'ms-settings:printers'
Write-Host 'The IPP URL was copied to the clipboard. Use Add manually > IPP device:'
Write-Host $IppUrl -ForegroundColor Cyan
