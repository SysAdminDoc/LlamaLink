$ErrorActionPreference = 'Stop'

$packageName = 'llamalink'
$url64 = 'https://github.com/SysAdminDoc/LlamaLink/releases/download/v0.4.0/LlamaLink.exe'
$checksum64 = '8DD7FFDE727B363D880BD5156E7B8D34BE06B12187F4150B180D16813D2AC774'
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
