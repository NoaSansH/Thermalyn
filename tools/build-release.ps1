# Inno Setup writes through EndUpdateResource, which fails with error 110 on a synchronised or
# network-backed folder. Compile into a local temporary directory, then copy back.

[CmdletBinding()]
param(
    [switch]$SkipInstaller,
    [ValidatePattern('^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string]$Version = '1.0.2'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'Thermalyn\Thermalyn.csproj'
$artifacts = Join-Path $root 'artifacts'
$output = Join-Path $artifacts 'release'
$stage = $null

if (-not $SkipInstaller) { & (Join-Path $PSScriptRoot 'fetch-pawnio.ps1') }
New-Item -ItemType Directory -Force -Path $output | Out-Null

# A publish pins a runtime identifier and NuGet rewrites the lock files. CI restores them in
# locked mode, so the committed contents are put back afterwards.
$lockFiles = @(
    (Join-Path $root 'Thermalyn\packages.lock.json'),
    (Join-Path $root 'tests\Thermalyn.Tests\packages.lock.json')
) | Where-Object { Test-Path -LiteralPath $_ }
$lockContents = @{}
foreach ($lock in $lockFiles) { $lockContents[$lock] = Get-Content -LiteralPath $lock -Raw }

try {
    Write-Host '==> portable build (self-contained)'
    dotnet publish $project -c Release -r win-x64 --self-contained true `
        /p:PortableBuild=true /p:PublishSingleFile=true `
        /p:Version=$Version `
        -o (Join-Path $artifacts 'Thermalyn-win-x64') --nologo
    if ($LASTEXITCODE) { throw 'portable publish failed' }
    Copy-Item (Join-Path $artifacts 'Thermalyn-win-x64\Thermalyn.exe') (Join-Path $output 'Thermalyn-Portable.exe') -Force

    if ($SkipInstaller) { return }

    Write-Host '==> installed build (framework-dependent single file)'
    dotnet publish $project -c Release -r win-x64 --self-contained false `
        /p:InstallerBuild=true /p:PublishSingleFile=true `
        /p:Version=$Version `
        -o (Join-Path $artifacts 'Thermalyn-installed') --nologo
    if ($LASTEXITCODE) { throw 'installed publish failed' }

    $iscc = (Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue).Source
    if (-not $iscc) {
        $iscc = @(${env:ProgramFiles(x86)}, $env:ProgramFiles) |
            Where-Object { $_ } |
            ForEach-Object { Join-Path $_ 'Inno Setup 6\ISCC.exe' } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
    }
    if (-not $iscc) { throw 'Inno Setup 6 was not found in PATH or the standard program folders.' }

    $stage = Join-Path ([IO.Path]::GetTempPath()) ("ThermalynSetup-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    Write-Host '==> online installer'
    & $iscc "/DAppVersion=$Version" (Join-Path $root 'installer\Thermalyn.iss') "/O$stage" | Select-Object -Last 3
    if ($LASTEXITCODE) { throw 'online installer compilation failed' }
    Copy-Item (Join-Path $stage 'Thermalyn-Setup.exe') (Join-Path $output 'Thermalyn-Setup.exe') -Force

    Write-Host '==> offline installer'
    & $iscc '/DOfflineBuild=1' "/DAppVersion=$Version" (Join-Path $root 'installer\Thermalyn.iss') "/O$stage" | Select-Object -Last 3
    if ($LASTEXITCODE) { throw 'offline installer compilation failed' }
    Copy-Item (Join-Path $stage 'Thermalyn-Setup-Offline.exe') (Join-Path $output 'Thermalyn-Setup-Offline.exe') -Force
}
finally {
    foreach ($lock in $lockContents.Keys) { Set-Content -LiteralPath $lock -Value $lockContents[$lock] -NoNewline }
    if ($stage -and (Test-Path -LiteralPath $stage)) { Remove-Item -LiteralPath $stage -Recurse -Force }
    foreach ($intermediate in 'Thermalyn-win-x64', 'Thermalyn-installed') {
        $path = Join-Path $artifacts $intermediate
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
}

$deliverables = @((Join-Path $output 'Thermalyn-Portable.exe'))
if (-not $SkipInstaller) { $deliverables += (Join-Path $output 'Thermalyn-Setup.exe'), (Join-Path $output 'Thermalyn-Setup-Offline.exe') }
Get-Item $deliverables |
    Select-Object Name, @{ n = 'MB'; e = { [Math]::Round($_.Length / 1MB, 1) } }, LastWriteTime
