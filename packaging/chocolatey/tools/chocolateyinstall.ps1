$ErrorActionPreference = 'Stop'

$arguments = @{
    packageName    = 'thermalyn'
    fileType       = 'exe'
    url64bit       = 'https://github.com/NoaSansH/Thermalyn/releases/download/v1.0.3/Thermalyn-Setup.exe'
    checksum64     = '03d3e2330051c959381fe57fceb0037f0cfc840c431fc05178e2dbf18cf42de4'
    checksumType64 = 'sha256'
    silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
    validExitCodes = @(0)
}

Install-ChocolateyPackage @arguments
