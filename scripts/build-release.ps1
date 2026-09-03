[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepositoryRoot = Split-Path -Parent $PSScriptRoot
$Artifacts = Join-Path $RepositoryRoot 'artifacts'
$Architecture = 'x64'
$Publish = Join-Path $Artifacts 'publish-win-x64'
$Archive = Join-Path $Artifacts 'MiniPrint-win-x64.zip'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 8 SDK is required. Download it from https://dotnet.microsoft.com/download/dotnet/8.0'
}

New-Item -ItemType Directory -Path $Artifacts -Force | Out-Null
if (Test-Path $Publish) {
    Remove-Item -Path $Publish -Recurse -Force
}
if (Test-Path $Archive) {
    Remove-Item -Path $Archive -Force
}

Push-Location $RepositoryRoot
try {
    dotnet restore .\MiniPrint.sln
    dotnet test .\MiniPrint.sln --configuration Release --no-restore
    dotnet publish .\src\MiniPrint.Server\MiniPrint.Server.csproj `
        --configuration Release `
        --runtime "win-$Architecture" `
        --self-contained true `
        --output $Publish `
        -p:PublishSingleFile=false

    Copy-Item .\scripts\install-server.ps1 $Publish
    Copy-Item .\scripts\uninstall-server.ps1 $Publish
    Copy-Item .\scripts\add-printer.ps1 $Publish
    Copy-Item .\scripts\test-ipp.ps1 $Publish
    Copy-Item .\README.md $Publish
    Compress-Archive -Path "$Publish\*" -DestinationPath $Archive -CompressionLevel Optimal
}
finally {
    Pop-Location
}

Write-Host "Package created: $Archive" -ForegroundColor Green
