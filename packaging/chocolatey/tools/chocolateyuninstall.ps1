$ErrorActionPreference = 'Stop'

$key = Get-UninstallRegistryKey -SoftwareName 'Thermalyn*'
if (-not $key) { return }

$key | ForEach-Object {
    Uninstall-ChocolateyPackage -PackageName 'thermalyn' -FileType 'exe' `
        -SilentArgs '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' `
        -File $_.UninstallString.Trim('"')
}
