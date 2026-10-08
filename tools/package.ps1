param([string[]]$ModPackage=@())
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$package = Join-Path $root 'build\package\Release'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$flavor=if($ModPackage.Count){'-with-mods'}else{''}
$zip = Join-Path $dist ('ZML-0.4.1-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + $flavor + '-win-x64.zip')
# An explicit whitelist prevents private config, logs, source fixtures or tests
# from accidentally entering the redistributable package.
$scratch = Join-Path $dist ('staging-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $scratch | Out-Null
foreach ($name in @('ZMLModLoader.exe','ZMLRuntime.dll','README.md','MinHook-LICENSE.txt','loader.ini.example')) {
    Copy-Item -LiteralPath (Join-Path $package $name) -Destination $scratch
}
# Optional, separately built Mod packages. No specific Mod id or source path.
$seenMods=@{}
foreach($mod in $ModPackage){
    $id=(Get-Content -LiteralPath (Join-Path $mod 'zml-package.json') -Raw -Encoding utf8 | ConvertFrom-Json).id
    if($seenMods.ContainsKey($id)){throw 'Duplicate Mod package id'}
    $seenMods[$id]=$true
    & (Join-Path $PSScriptRoot 'install-mod.ps1') -ModPackage $mod -PackageRoot $scratch
}
New-Item -ItemType Directory (Join-Path $scratch 'lua') | Out-Null
Copy-Item -LiteralPath (Join-Path $package 'lua\zml.lua') -Destination (Join-Path $scratch 'lua')
New-Item -ItemType Directory (Join-Path $scratch 'docs') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'docs\VALIDATION.md') -Destination (Join-Path $scratch 'docs')
Copy-Item -LiteralPath (Join-Path $root 'docs\MOD_API.md') -Destination (Join-Path $scratch 'docs')
Compress-Archive -Path (Join-Path $scratch '*') -DestinationPath $zip
$hash = Get-FileHash -LiteralPath $zip -Algorithm SHA256
$hash.Hash + '  ' + (Split-Path $zip -Leaf) | Set-Content -Encoding ascii ($zip + '.sha256')
# Only remove this script's own checked staging directory.
$resolved = (Resolve-Path -LiteralPath $scratch).Path
if (-not $resolved.StartsWith((Resolve-Path -LiteralPath $dist).Path + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Staging boundary check failed' }
Remove-Item -LiteralPath $resolved -Recurse -Force
Write-Output $zip
Write-Output $hash.Hash
