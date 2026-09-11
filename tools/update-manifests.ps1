# Rewrites the Scoop and WinGet manifests for a published release. The checksums come from the
# SHA256SUMS.txt produced by build-release.ps1, so they describe the files that were actually
# uploaded rather than a value copied by hand.

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [string]$ChecksumFile,
    [string]$ReleaseDate = [DateTime]::UtcNow.ToString('yyyy-MM-dd')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $ChecksumFile) { $ChecksumFile = Join-Path $root 'artifacts\release\SHA256SUMS.txt' }

$checksums = @{}
foreach ($line in Get-Content $ChecksumFile) {
    if ($line -match '^([0-9a-fA-F]{64})\s+(\S+)$') { $checksums[$Matches[2]] = $Matches[1].ToLowerInvariant() }
}

function Get-Checksum([string]$name) {
    if (-not $checksums.ContainsKey($name)) { throw "$ChecksumFile has no entry for $name." }
    return $checksums[$name]
}

# Re-running the release workflow on an older tag must not walk the manifests backwards.
$current = [regex]::Match((Get-Content (Join-Path $root 'packaging\winget\NoaSansH.Thermalyn.yaml') -Raw),
    '(?m)^PackageVersion:\s*(\S+)\s*$').Groups[1].Value
if ($current -and [version]$current -ge [version]$Version) {
    Write-Host "manifests already at $current; $Version is not newer, nothing to do"
    exit 0
}

$portable = Get-Checksum "Thermalyn-Portable-$Version.exe"
$installer = Get-Checksum "Thermalyn-Setup-$Version.exe"
$download = "https://github.com/NoaSansH/Thermalyn/releases/download/v$Version"

function Set-Manifest([string]$relative, [hashtable[]]$edits) {
    $path = Join-Path $root $relative
    $text = Get-Content $path -Raw
    foreach ($edit in $edits) {
        $found = [regex]::Matches($text, $edit.Pattern)
        if ($found.Count -ne 1) { throw "$relative : $($found.Count) match for $($edit.Pattern)." }
        $text = [regex]::Replace($text, $edit.Pattern, $edit.Replacement)
    }
    # Keep the trailing newline the file already had rather than adding or dropping one.
    [IO.File]::WriteAllText($path, $text)
    Write-Host "updated $relative"
}

Set-Manifest 'packaging\scoop\thermalyn.json' @(
    @{ Pattern = '(?m)^(\s*"version"\s*:\s*")[^"]+(")'; Replacement = "`${1}$Version`${2}" }
    @{ Pattern = 'https://github\.com/NoaSansH/Thermalyn/releases/download/v\d[^/]*/Thermalyn-Portable-\d[^"#]*\.exe'
       Replacement = "$download/Thermalyn-Portable-$Version.exe" }
    @{ Pattern = '(?m)^(\s*"hash"\s*:\s*")[0-9a-fA-F]{64}(")'; Replacement = "`${1}$portable`${2}" }
)

Set-Manifest 'packaging\winget\NoaSansH.Thermalyn.yaml' @(
    @{ Pattern = '(?m)^PackageVersion:\s*\S+\s*$'; Replacement = "PackageVersion: $Version" }
)

Set-Manifest 'packaging\winget\NoaSansH.Thermalyn.installer.yaml' @(
    @{ Pattern = '(?m)^PackageVersion:\s*\S+\s*$'; Replacement = "PackageVersion: $Version" }
    @{ Pattern = '(?m)^ReleaseDate:\s*\S+\s*$'; Replacement = "ReleaseDate: $ReleaseDate" }
    @{ Pattern = 'https://github\.com/NoaSansH/Thermalyn/releases/download/v[^/]+/Thermalyn-Setup-[^\s]+\.exe'
       Replacement = "$download/Thermalyn-Setup-$Version.exe" }
    @{ Pattern = '(?m)^(\s*InstallerSha256:\s*)[0-9a-fA-F]{64}\s*$'; Replacement = "`${1}$($installer.ToUpperInvariant())" }
)

Set-Manifest 'packaging\winget\NoaSansH.Thermalyn.locale.en-US.yaml' @(
    @{ Pattern = '(?m)^PackageVersion:\s*\S+\s*$'; Replacement = "PackageVersion: $Version" }
    @{ Pattern = 'https://github\.com/NoaSansH/Thermalyn/releases/tag/v\S+'; Replacement = "https://github.com/NoaSansH/Thermalyn/releases/tag/v$Version" }
)
