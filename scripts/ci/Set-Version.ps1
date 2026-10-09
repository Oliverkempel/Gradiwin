# Stamps a version (X.Y.Z) into Directory.Build.props and the MSIX manifest (X.Y.Z.0) for a CI build.
param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = "Stop"
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must be X.Y.Z, got '$Version'" }
$root = Join-Path $PSScriptRoot "..\.."

$propsPath = Join-Path $root "Directory.Build.props"
$props = Get-Content $propsPath -Raw
$props = [regex]::Replace($props, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
Set-Content $propsPath $props -NoNewline

$manifestPath = Join-Path $root "src\Gradiwin\Package.appxmanifest"
$manifest = Get-Content $manifestPath -Raw
$manifest = [regex]::Replace($manifest, '(<Identity[^>]*\sVersion=")[^"]*(")', "`${1}$Version.0`${2}")
Set-Content $manifestPath $manifest -NoNewline

Write-Host "Version set to $Version"
