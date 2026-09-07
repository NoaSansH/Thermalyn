$ErrorActionPreference = 'Stop'

# Inno Setup registers its own uninstaller, which removes the program files, the per-profile
# settings, the scheduled task and the run-at-logon entries.
$key = Get-UninstallRegistryKey -SoftwareName 'Thermalyn*'
if (-not $key) { return }

$key | ForEach-Object {
    Uninstall-ChocolateyPackage -PackageName 'thermalyn' -FileType 'exe' `
        -SilentArgs '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' `
        -File $_.UninstallString.Trim('"')
}
