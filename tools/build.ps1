$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'bootstrap.ps1')
& cmake -S $root -B (Join-Path $root 'build') -G 'Visual Studio 17 2022' -A x64
if ($LASTEXITCODE -ne 0) { throw 'CMake configure failed' }
& cmake --build (Join-Path $root 'build') --config Release --parallel 6
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
& (Join-Path $PSScriptRoot 'build-launcher-options.ps1')
& ctest --test-dir (Join-Path $root 'build') -C Release --output-on-failure
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
Write-Output "Package: $(Join-Path $root 'build\package\Release')"
