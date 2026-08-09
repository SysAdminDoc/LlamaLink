$ErrorActionPreference = 'Stop'

$packageName = 'llamalink'
$url64 = 'https://github.com/SysAdminDoc/LlamaLink/releases/download/v0.5.1/LlamaLink.exe'
$checksum64 = '2E10CE0081A84FEBA9CE582C591C90F77F33E120124EE3D9B0A25BEC3D84B5DC'
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
