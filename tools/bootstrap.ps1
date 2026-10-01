$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$destination = Join-Path $root 'third_party\minhook'
$revision = 'c3fcafdc10146beb5919319d0683e44e3c30d537'
if (-not (Test-Path -LiteralPath (Join-Path $destination '.git'))) {
    if (Test-Path -LiteralPath $destination) { throw 'Existing non-Git MinHook directory; refusing to overwrite.' }
    New-Item -ItemType Directory -Force (Split-Path $destination -Parent) | Out-Null
    & gh repo clone TsudaKageyu/minhook $destination -- --branch v1.3.4 --depth 1
    if ($LASTEXITCODE -ne 0) { throw 'Authenticated gh clone failed' }
}
$actual = (& git -C $destination rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actual -ne $revision) { throw "MinHook revision mismatch: $actual" }
$dirty = & git -C $destination status --porcelain
if ($dirty) { throw 'MinHook has local changes; inspect them instead of overwriting.' }
Write-Output "MinHook v1.3.4 verified: $revision"
