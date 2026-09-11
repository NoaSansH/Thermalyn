# Opens the microsoft/winget-pkgs pull request for a published version, using the manifests in
# packaging/winget. It builds the commit through the git data API rather than cloning, because
# winget-pkgs holds hundreds of thousands of files and none of them are needed here.
#
# Needs GH_TOKEN to carry a token that can push to the NoaSansH/winget-pkgs fork; the token a
# workflow gets by default only reaches this repository.

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [string]$Fork = 'NoaSansH/winget-pkgs',
    [string]$Upstream = 'microsoft/winget-pkgs',
    [string]$Identifier = 'NoaSansH.Thermalyn'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$branch = "$Identifier-$Version"
$directory = "manifests/n/NoaSansH/Thermalyn/$Version"

function Invoke-Api([string]$Method, [string]$Path, $Body) {
    if ($null -eq $Body) { return gh api -X $Method $Path | ConvertFrom-Json }
    return ($Body | ConvertTo-Json -Depth 10 -Compress | gh api -X $Method $Path --input -) | ConvertFrom-Json
}

$existing = gh pr list --repo $Upstream --search "$Identifier $Version in:title" --state all --json number,title |
    ConvertFrom-Json | Where-Object { $_.title -match [regex]::Escape($Version) }
if ($existing) {
    Write-Host "A pull request for $Version already exists: #$($existing[0].number). Nothing to submit."
    exit 0
}

$base = (Invoke-Api GET "repos/$Upstream/git/ref/heads/master").object.sha
Write-Host "upstream master $base"

$tree = @()
foreach ($name in @("$Identifier.yaml", "$Identifier.installer.yaml", "$Identifier.locale.en-US.yaml")) {
    $bytes = [IO.File]::ReadAllBytes((Join-Path $root "packaging\winget\$name"))
    $blob = Invoke-Api POST "repos/$Fork/git/blobs" @{ content = [Convert]::ToBase64String($bytes); encoding = 'base64' }
    $tree += @{ path = "$directory/$name"; mode = '100644'; type = 'blob'; sha = $blob.sha }
}

$baseTree = (Invoke-Api GET "repos/$Fork/git/commits/$base").tree.sha
$newTree = Invoke-Api POST "repos/$Fork/git/trees" @{ base_tree = $baseTree; tree = $tree }
$commit = Invoke-Api POST "repos/$Fork/git/commits" @{
    message = "New version: $Identifier version $Version"; tree = $newTree.sha; parents = @($base)
}
Write-Host "commit $($commit.sha)"

# A leftover branch from an earlier attempt is replaced rather than treated as a failure.
gh api -X DELETE "repos/$Fork/git/refs/heads/$branch" 2>$null | Out-Null
Invoke-Api POST "repos/$Fork/git/refs" @{ ref = "refs/heads/$branch"; sha = $commit.sha } | Out-Null

$body = @"
## Description

Thermalyn $Version, a hardware monitor for Windows 11.

- Installer type: Inno Setup, machine scope
- Silent switches: ``/VERYSILENT /SUPPRESSMSGBOXES /NORESTART``
- ``Microsoft.DotNet.DesktopRuntime.8`` is declared as a dependency, so an unattended install never
  reaches the installer's runtime prompt
- ``InstallerSha256`` is taken from the checksum file published with the release
- Release: https://github.com/NoaSansH/Thermalyn/releases/tag/v$Version

Submitted automatically by the release workflow of the NoaSansH/Thermalyn repository.

## Manifest checklist

- [x] Checked that there aren't other open pull requests for the same manifest update/change
- [x] This PR only modifies one (1) manifest
- [x] Validated manifest locally with ``winget validate --manifest <path>``
"@

gh pr create --repo $Upstream --base master --head "$($Fork.Split('/')[0]):$branch" `
    --title "New version: $Identifier version $Version" --body $body
