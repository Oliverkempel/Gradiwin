# Builds the packaged app and registers it for the current user (requires Developer Mode).
# Use this when Visual Studio's "WinUI application development" workload isn't installed.
#   pwsh scripts/deploy-dev.ps1 [-Configuration Debug|Release] [-Platform x64|arm64|x86] [-Launch]
param(
    [string]$Configuration = "Debug",
    [string]$Platform = "x64",
    [switch]$Launch
)
$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "..\src\Gradiwin\Gradiwin.csproj"

Get-Process Gradiwin -ErrorAction SilentlyContinue | Stop-Process -Force

dotnet build $project -c $Configuration -p:Platform=$Platform
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$manifest = Join-Path $PSScriptRoot "..\src\Gradiwin\bin\$Platform\$Configuration\net10.0-windows10.0.22621.0\AppxManifest.xml"
Add-AppxPackage -Register (Resolve-Path $manifest) -ForceApplicationShutdown
$package = Get-AppxPackage Gradiwin
Write-Host "Registered $($package.PackageFullName)"

if ($Launch) {
    Start-Process "shell:AppsFolder\$($package.PackageFamilyName)!App"
}
