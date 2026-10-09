# Builds the release artifacts for one platform into -OutDir:
#   Gradiwin-<version>-<platform>-portable.zip   self-contained, unpackaged (runs without installing)
#   Gradiwin-<version>-<platform>.msix           MSIX package, signed when a certificate is given
param(
    [Parameter(Mandatory)][string]$Version,
    [ValidateSet("x64", "arm64", "x86")][string]$Platform = "x64",
    [Parameter(Mandatory)][string]$OutDir,
    [string]$CertificatePath = "",
    [string]$CertificatePassword = ""
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$project = Join-Path $root "src\Gradiwin\Gradiwin.csproj"
$work = Join-Path ([IO.Path]::GetTempPath()) "gradiwin-release-$Platform"
New-Item -ItemType Directory -Force $OutDir | Out-Null
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue

function Invoke-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed ($LASTEXITCODE)" }
}

# Portable build. A separate BaseOutputPath keeps it from mixing with the packaged build's outputs.
$portableDir = Join-Path $work "portable"
Invoke-Dotnet @("publish", $project, "-c", "Release", "-p:Platform=$Platform", "-r", "win-$Platform", "--self-contained",
    "-p:WindowsPackageType=None", "-p:WindowsAppSDKSelfContained=true",
    "-p:PublishDir=$portableDir\", "-p:BaseOutputPath=$work\obj-portable\")
$zip = Join-Path $OutDir "Gradiwin-$Version-$Platform-portable.zip"
Compress-Archive -Path (Join-Path $portableDir "*") -DestinationPath $zip -Force
Write-Host "Created $zip"

# MSIX package.
$packageDir = Join-Path $work "msix"
$msixArgs = @("build", $project, "-c", "Release", "-p:Platform=$Platform", "-p:RuntimeIdentifier=win-$Platform",
    "-p:GenerateAppxPackageOnBuild=true", "-p:AppxPackageDir=$packageDir\", "-p:AppxBundle=Never",
    "-p:BaseOutputPath=$work\obj-msix\")
if ($CertificatePath) {
    $msixArgs += @("-p:AppxPackageSigningEnabled=true", "-p:PackageCertificateKeyFile=$CertificatePath",
        "-p:PackageCertificatePassword=$CertificatePassword")
}
Invoke-Dotnet $msixArgs
$built = Get-ChildItem $packageDir -Recurse -Filter "*.msix" | Where-Object { $_.FullName -notmatch '\\Dependencies\\' } | Select-Object -First 1
if (-not $built) { throw "No MSIX produced" }
$msix = Join-Path $OutDir "Gradiwin-$Version-$Platform.msix"
Copy-Item $built.FullName $msix -Force
Write-Host "Created $msix (signed: $([bool]$CertificatePath))"
