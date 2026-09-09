[CmdletBinding()]
param(
    [string]$Root = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\release'),
    [double]$PortableLimitMiB = 60,
    [double]$OnlineInstallerLimitMiB = 15,
    [double]$OfflineInstallerLimitMiB = 65
)

$ErrorActionPreference = 'Stop'
$limits = [ordered]@{
    'Thermalyn-Portable.exe' = $PortableLimitMiB
    'Thermalyn-Setup.exe' = $OnlineInstallerLimitMiB
    'Thermalyn-Setup-Offline.exe' = $OfflineInstallerLimitMiB
}

$results = foreach ($entry in $limits.GetEnumerator()) {
    $path = Join-Path $Root $entry.Key
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing release artifact: $($entry.Key)"
    }

    $file = Get-Item -LiteralPath $path
    $sizeMiB = $file.Length / 1MB
    if ($sizeMiB -gt $entry.Value) {
        throw ('{0} is {1:N2} MiB; limit is {2:N2} MiB.' -f $entry.Key, $sizeMiB, $entry.Value)
    }

    $stream = [IO.File]::OpenRead($path)
    try {
        if (($stream.ReadByte() -ne 0x4D) -or ($stream.ReadByte() -ne 0x5A)) {
            throw "$($entry.Key) is not a Windows PE executable."
        }
    }
    finally {
        $stream.Dispose()
    }

    [pscustomobject]@{
        Name = $entry.Key
        MiB = [Math]::Round($sizeMiB, 2)
        SHA256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
}

$unexpectedSymbols = Get-ChildItem -LiteralPath $Root -Filter '*.pdb' -File
if ($unexpectedSymbols) {
    throw "Published PDB files found beside the release artifacts: $($unexpectedSymbols.Name -join ', ')"
}

$results | Format-Table -AutoSize
