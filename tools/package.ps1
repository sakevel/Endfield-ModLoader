$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$package = Join-Path $root 'build\package\Release'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$zip = Join-Path $dist ('ZML-0.1.0-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-win-x64.zip')
# An explicit whitelist prevents private config, logs, source fixtures or tests
# from accidentally entering the redistributable package.
$scratch = Join-Path $dist ('staging-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $scratch | Out-Null
foreach ($name in @('ZML.exe','ZMLRuntime.dll','README.md','MinHook-LICENSE.txt','loader.ini.example')) {
    Copy-Item -LiteralPath (Join-Path $package $name) -Destination $scratch
}
New-Item -ItemType Directory (Join-Path $scratch 'mods\custom-menu') -Force | Out-Null
foreach ($name in @('CustomMenu.dll','mod.ini','custom-menu.lua')) {
    Copy-Item -LiteralPath (Join-Path $package "mods\custom-menu\$name") -Destination (Join-Path $scratch 'mods\custom-menu')
}
New-Item -ItemType Directory (Join-Path $scratch 'docs') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'docs\VALIDATION.md') -Destination (Join-Path $scratch 'docs')
Compress-Archive -Path (Join-Path $scratch '*') -DestinationPath $zip
$hash = Get-FileHash -LiteralPath $zip -Algorithm SHA256
$hash.Hash + '  ' + (Split-Path $zip -Leaf) | Set-Content -Encoding ascii ($zip + '.sha256')
# Only remove this script's own checked staging directory.
$resolved = (Resolve-Path -LiteralPath $scratch).Path
if (-not $resolved.StartsWith((Resolve-Path -LiteralPath $dist).Path + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Staging boundary check failed' }
Remove-Item -LiteralPath $resolved -Recurse -Force
Write-Output $zip
Write-Output $hash.Hash
