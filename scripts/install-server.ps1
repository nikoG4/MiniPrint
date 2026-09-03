[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Source,
    [string]$InstallDirectory = "$env:ProgramFiles\MiniPrint"
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ServiceName = 'MiniPrint'
$Source = (Resolve-Path $Source).Path
$SourceExecutable = Join-Path $Source 'MiniPrint.Server.exe'

$CurrentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$Principal = [Security.Principal.WindowsPrincipal]::new($CurrentIdentity)
if (-not $Principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from PowerShell as administrator.'
}
if (-not (Test-Path $SourceExecutable)) {
    throw "MiniPrint.Server.exe was not found in $Source"
}

$ExistingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($ExistingService -and $ExistingService.Status -ne 'Stopped') {
    Stop-Service -Name $ServiceName -Force
}

New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
$ExistingSettings = Join-Path $InstallDirectory 'appsettings.json'
$SettingsBackup = Join-Path $env:TEMP "MiniPrint-appsettings-$([Guid]::NewGuid().ToString('N')).json"
if (Test-Path $ExistingSettings) {
    Copy-Item $ExistingSettings $SettingsBackup -Force
}

Copy-Item -Path "$Source\*" -Destination $InstallDirectory -Recurse -Force
if (Test-Path $SettingsBackup) {
    Copy-Item $SettingsBackup $ExistingSettings -Force
    Remove-Item $SettingsBackup -Force
}

$Executable = Join-Path $InstallDirectory 'MiniPrint.Server.exe'
if (-not $ExistingService) {
    New-Service -Name $ServiceName `
        -DisplayName 'MiniPrint IPP Server' `
        -Description 'Publishes local Windows printers through IPP.' `
        -BinaryPathName "`"$Executable`"" `
        -StartupType Automatic | Out-Null
}
else {
    & sc.exe config $ServiceName "binPath= `"$Executable`"" start= auto | Out-Null
}

$FirewallRules = @(
    @{ Name = 'MiniPrint-IPP'; DisplayName = 'MiniPrint IPP (Private LAN)'; Protocol = 'TCP'; LocalPort = 631 },
    @{ Name = 'MiniPrint-mDNS'; DisplayName = 'MiniPrint mDNS (Private LAN)'; Protocol = 'UDP'; LocalPort = 5353 }
)
foreach ($Rule in $FirewallRules) {
    Remove-NetFirewallRule -Name $Rule.Name -ErrorAction SilentlyContinue
    New-NetFirewallRule `
        -Name $Rule.Name `
        -DisplayName $Rule.DisplayName `
        -Direction Inbound `
        -Action Allow `
        -Protocol $Rule.Protocol `
        -LocalPort $Rule.LocalPort `
        -Profile Private `
        -RemoteAddress LocalSubnet | Out-Null
}

Start-Service -Name $ServiceName
Write-Host 'MiniPrint installed and started.' -ForegroundColor Green
Write-Host "Open http://$env:COMPUTERNAME`:631/ from another LAN computer."
