[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$InstallDirectory = "$env:ProgramFiles\MiniPrint",
    [switch]$KeepData
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ServiceName = 'MiniPrint'

$CurrentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$Principal = [Security.Principal.WindowsPrincipal]::new($CurrentIdentity)
if (-not $Principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from PowerShell as administrator.'
}

if ($PSCmdlet.ShouldProcess($ServiceName, 'Stop and remove MiniPrint service and firewall rules')) {
    $Service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($Service) {
        if ($Service.Status -ne 'Stopped') {
            Stop-Service -Name $ServiceName -Force
        }
        & sc.exe delete $ServiceName | Out-Null
    }
    Remove-NetFirewallRule -Name 'MiniPrint-IPP' -ErrorAction SilentlyContinue
    Remove-NetFirewallRule -Name 'MiniPrint-mDNS' -ErrorAction SilentlyContinue
}

if (-not $KeepData -and $PSCmdlet.ShouldProcess("$env:ProgramData\MiniPrint", 'Remove MiniPrint spool data')) {
    Remove-Item "$env:ProgramData\MiniPrint" -Recurse -Force -ErrorAction SilentlyContinue
}
if ($PSCmdlet.ShouldProcess($InstallDirectory, 'Remove MiniPrint program files')) {
    Remove-Item $InstallDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
