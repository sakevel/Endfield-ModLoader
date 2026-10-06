param([string[]]$ModPackage=@(), [switch]$SkipFrameworkBuild, [switch]$SkipTests)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(-not $SkipFrameworkBuild){ & (Join-Path $PSScriptRoot 'build.ps1') }
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if(-not(Test-Path -LiteralPath $compiler)){throw '.NET Framework 4.x C# compiler is required'}
$build=Join-Path $root 'build/installer'; New-Item -ItemType Directory -Force $build | Out-Null
$source=Join-Path $root 'installer'
$refs=@('/r:System.dll','/r:System.Core.dll','/r:System.Web.Extensions.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll')
$common=@((Join-Path $source 'Shared.cs'),(Join-Path $source 'Catalog.cs'),(Join-Path $source 'NativeLaunch.cs'))
$bridge=Join-Path $build 'ZMLLauncherBridge.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /warnaserror+ @refs "/out:$bridge" "/win32manifest:$(Join-Path $source 'bridge.manifest')" @common (Join-Path $source 'Bridge.cs')
if($LASTEXITCODE -ne 0){throw 'Bridge compilation failed'}
$scratch=Join-Path $build ('payload-'+[guid]::NewGuid().ToString('N')); New-Item -ItemType Directory $scratch | Out-Null
$package=Join-Path $root 'build/package/Release'
foreach($file in @('ZML.exe','ZMLRuntime.dll','ZMLNativeLaunch.dll','README.md','MinHook-LICENSE.txt','loader.ini.example','lua/zml.lua','docs/MOD_API.md')){
    $dest=Join-Path $scratch $file; New-Item -ItemType Directory -Force (Split-Path $dest -Parent) | Out-Null
    Copy-Item -LiteralPath (Join-Path $package $file) -Destination $dest
}
Copy-Item -LiteralPath $bridge -Destination $scratch
Copy-Item -LiteralPath (Join-Path $root 'docs/INSTALLER.md') -Destination $scratch
New-Item -ItemType Directory (Join-Path $scratch 'web') | Out-Null
foreach($file in @('zml-client.js','zml-client.css')){Copy-Item -LiteralPath (Join-Path $source "web/$file") -Destination (Join-Path $scratch 'web')}
$seen=@{}
foreach($mod in $ModPackage){
    $id=(Get-Content -LiteralPath (Join-Path $mod 'zml-package.json') -Raw -Encoding utf8 | ConvertFrom-Json).id
    if($seen.ContainsKey($id)){throw 'Duplicate Mod package id'}; $seen[$id]=$true
    & (Join-Path $PSScriptRoot 'install-mod.ps1') -ModPackage $mod -PackageRoot $scratch
}
$zip=Join-Path $build 'payload.zip'; if(Test-Path -LiteralPath $zip){Remove-Item -LiteralPath $zip}
Compress-Archive -Path (Join-Path $scratch '*') -DestinationPath $zip
$setup=Join-Path $build 'ZMLSetup.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /warnaserror+ @refs "/out:$setup" "/win32manifest:$(Join-Path $source 'installer.manifest')" "/resource:$zip,ZML.Payload.zip" @common (Join-Path $source 'InstallEngine.cs') (Join-Path $source 'Installer.cs')
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
if(-not $SkipTests){
    $tests=Join-Path $build 'InstallerTests.exe'
    & $compiler /nologo /target:exe /platform:x64 /optimize+ /warnaserror+ @refs "/out:$tests" /main:ZmlSetup.InstallerTests @common (Join-Path $source 'InstallEngine.cs') (Join-Path $source 'Bridge.cs') (Join-Path $source 'Installer.cs') (Join-Path $root 'tests/installer_tests.cs')
    if($LASTEXITCODE -ne 0){throw 'Installer test compilation failed'}
    & $tests $zip (Join-Path $root 'build/tests/Release/InjectionFixture.exe')
    if($LASTEXITCODE -ne 0){throw 'Installer tests failed'}
}
$dist=Join-Path $root 'dist';New-Item -ItemType Directory -Force $dist | Out-Null
$flavor=if($ModPackage.Count){'-with-mods'}else{''}
$release=Join-Path $dist ('ZMLSetup-0.1.5-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+$flavor+'-win-x64.exe')
Copy-Item -LiteralPath $setup -Destination $release
$sha=[Security.Cryptography.SHA256]::Create()
try {$hash=[BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($release))).Replace('-','')}finally{$sha.Dispose()}
$hash+'  '+(Split-Path $release -Leaf) | Set-Content -Encoding ascii ($release+'.sha256')
$resolved=(Resolve-Path -LiteralPath $scratch).Path
if(-not $resolved.StartsWith((Resolve-Path -LiteralPath $build).Path+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Staging boundary failed'}
Remove-Item -LiteralPath $resolved -Recurse -Force
Write-Output $release
Write-Output $hash
