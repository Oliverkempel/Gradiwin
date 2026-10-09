# Computes the next semantic version from the latest vX.Y.Z tag and the pull request's labels.
#   Labels: major | minor | patch (default when none) | skip-release
# Without any release tag yet, the version in Directory.Build.props is used as-is (first release).
# Writes version/tag/bump/release to $GITHUB_OUTPUT and a summary to $GITHUB_STEP_SUMMARY when present.
param(
    # Comma-separated label names.
    [string]$Labels = ""
)
$ErrorActionPreference = "Stop"

$labelList = @($Labels -split "," | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ })
$bumps = @($labelList | Where-Object { $_ -in "major", "minor", "patch" })
if ($bumps.Count -gt 1) {
    throw "Use only one version label (major, minor or patch); found: $($bumps -join ', ')"
}
$bump = if ($bumps.Count -eq 1) { $bumps[0] } else { "patch" }
$release = -not ($labelList -contains "skip-release")

$tags = @(git tag --list "v*" | ForEach-Object {
        if ($_ -match '^v(\d+)\.(\d+)\.(\d+)$') { [version]::new([int]$Matches[1], [int]$Matches[2], [int]$Matches[3]) }
    } | Sort-Object -Descending)

if ($tags.Count -eq 0) {
    $props = [xml](Get-Content (Join-Path $PSScriptRoot "..\..\Directory.Build.props"))
    $base = [version]($props.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
    $next = [version]::new($base.Major, $base.Minor, $base.Build)
    $previous = "(none)"
    $bump = "initial"
}
else {
    $latest = $tags[0]
    $previous = "v$latest"
    $next = switch ($bump) {
        "major" { [version]::new($latest.Major + 1, 0, 0) }
        "minor" { [version]::new($latest.Major, $latest.Minor + 1, 0) }
        default { [version]::new($latest.Major, $latest.Minor, $latest.Build + 1) }
    }
}

$version = "$($next.Major).$($next.Minor).$($next.Build)"
Write-Host "Previous: $previous  Bump: $bump  Next: v$version  Release: $release"

if ($env:GITHUB_OUTPUT) {
    "version=$version" >> $env:GITHUB_OUTPUT
    "tag=v$version" >> $env:GITHUB_OUTPUT
    "bump=$bump" >> $env:GITHUB_OUTPUT
    "release=$($release.ToString().ToLowerInvariant())" >> $env:GITHUB_OUTPUT
}
if ($env:GITHUB_STEP_SUMMARY) {
    $note = if ($release) { "Merging releases **v$version** ($bump bump from $previous)." } else { "Merging will **not** create a release (``skip-release`` label)." }
    @"
### Version
$note

Set the bump with one PR label: ``major``, ``minor`` or ``patch`` (default). Add ``skip-release`` to merge without releasing.
"@ >> $env:GITHUB_STEP_SUMMARY
}
