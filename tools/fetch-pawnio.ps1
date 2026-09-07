# Downloads the PawnIO installer that the Thermalyn installers bundle.
#
# PawnIO is published under the GPL by its author and the repository does not carry a copy: see
# NOTICE.md. The version is pinned and the download is checked against its hash, so a change
# upstream fails the build instead of silently shipping a different binary.

[CmdletBinding()]
param(
    [string]$Version = '2.2.0',
    [string]$Sha256 = '1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root 'Thermalyn\Assets\PawnIO_setup.exe'

function Test-Hash([string]$path) {
    (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ieq $Sha256
}

if ((Test-Path -LiteralPath $target) -and -not $Force) {
    if (Test-Hash $target) {
        Write-Host "PawnIO $Version already present."
        return
    }
    Write-Host 'Present copy does not match the pinned hash; downloading again.'
}

$url = "https://github.com/namazso/PawnIO.Setup/releases/download/$Version/PawnIO_setup.exe"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
$temporary = "$target.download"

Write-Host "Downloading PawnIO $Version"
Invoke-WebRequest -Uri $url -OutFile $temporary -UseBasicParsing

if (-not (Test-Hash $temporary)) {
    $actual = (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash.ToLowerInvariant()
    Remove-Item -LiteralPath $temporary -Force
    throw "PawnIO $Version hash mismatch.`n  expected $Sha256`n  actual   $actual"
}

Move-Item -LiteralPath $temporary -Destination $target -Force
Write-Host "Written: $target"
