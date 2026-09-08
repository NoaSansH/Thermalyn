$ErrorActionPreference = 'Stop'

$arguments = @{
    packageName    = 'thermalyn'
    fileType       = 'exe'
    url64bit       = 'https://github.com/NoaSansH/Thermalyn/releases/download/v1.0.1/Thermalyn-Setup.exe'
    checksum64     = 'f34fb165ee6de16ee4d1bfbcd1fb88d741edf9c03f88d23be59d229f9eab62af'
    checksumType64 = 'sha256'
    silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
    validExitCodes = @(0)
}

Install-ChocolateyPackage @arguments
