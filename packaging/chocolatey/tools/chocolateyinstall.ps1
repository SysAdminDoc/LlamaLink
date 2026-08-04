$ErrorActionPreference = 'Stop'

$packageName = 'llamalink'
$url64 = 'https://github.com/SysAdminDoc/LlamaLink/releases/download/v0.5.0/LlamaLink.exe'
$checksum64 = '914CEDCADA4039CF47DCD88D0C958E6C9A43DB879C288575E2F6F8A35C82262F'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$target = Join-Path $toolsDir 'LlamaLink.exe'

if (-not [Environment]::Is64BitOperatingSystem) {
    throw "$packageName requires a 64-bit Windows operating system."
}

Get-ChocolateyWebFile `
    -PackageName $packageName `
    -FileFullPath $target `
    -Url64bit $url64 `
    -Checksum64 $checksum64 `
    -ChecksumType64 'sha256'

Install-BinFile -Name $packageName -Path $target
