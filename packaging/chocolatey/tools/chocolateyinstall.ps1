$ErrorActionPreference = 'Stop'

$arguments = @{
    packageName    = 'thermalyn'
    fileType       = 'exe'
    url64bit       = 'https://github.com/NoaSansH/Thermalyn/releases/download/v1.0.2/Thermalyn-Setup.exe'
    checksum64     = 'cb7ee32f06b49f0d218593986ea89400b71a267b5dd1e7057df794c9929b2cd2'
    checksumType64 = 'sha256'
    silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
    validExitCodes = @(0)
}

Install-ChocolateyPackage @arguments
