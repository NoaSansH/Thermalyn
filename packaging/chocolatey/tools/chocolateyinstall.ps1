$ErrorActionPreference = 'Stop'

# The installer is fetched from the GitHub release and checked against the checksum published in
# SHA256SUMS.txt beside it. Every release binary is also covered by a signed build provenance
# attestation: gh attestation verify Thermalyn-Setup.exe --repo NoaSansH/Thermalyn
$arguments = @{
    packageName    = 'thermalyn'
    fileType       = 'exe'
    url64bit       = 'https://github.com/NoaSansH/Thermalyn/releases/download/v1.0.0/Thermalyn-Setup.exe'
    checksum64     = '2a6077f3caf9c8dff620820ee9768e4771a7b5069ad77910047a298edbb82246'
    checksumType64 = 'sha256'
    silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
    validExitCodes = @(0)
}

Install-ChocolateyPackage @arguments
