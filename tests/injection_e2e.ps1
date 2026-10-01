param([string]$Launcher, [string]$Fixture, [string]$Rejected)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('zml-injection-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $root | Out-Null
function ChildPid($output) {
    $line = $output | Where-Object { $_ -match '^Created PID (\d+)$' } | Select-Object -First 1
    if (-not $line) { throw 'Launcher did not identify its own child' }
    [int]([regex]::Match($line, '\d+').Value)
}
try {
    # Positive path: the real runtime, no mods, harmless fixture instead of game.
    Copy-Item -LiteralPath $Launcher -Destination (Join-Path $root 'ZML.exe')
    Copy-Item -LiteralPath (Join-Path (Split-Path $Launcher -Parent) 'ZMLRuntime.dll') -Destination $root
    Copy-Item -LiteralPath $Fixture -Destination $root
    $run = Join-Path $root 'ZML.exe'
    $game = Join-Path $root (Split-Path $Fixture -Leaf)
    $marker = Join-Path $root 'marker.txt'
    $output = & $run --game $game -- --marker $marker 2>&1
    $exit = $LASTEXITCODE
    $output | Write-Output
    if ($exit -ne 0) { throw 'Positive injection failed' }
    $child = ChildPid $output
    Start-Sleep -Seconds 4
    if ((Get-Content -LiteralPath $marker -Raw) -ne 'runtime_loaded') { throw 'Fixture did not confirm runtime in-process' }
    if (Get-Process -Id $child -ErrorAction SilentlyContinue) { throw 'Fixture should have exited by itself' }

    # LoadLibrary failure / rollback: a DLL whose DllMain deliberately rejects.
    Copy-Item -LiteralPath $Rejected -Destination (Join-Path $root 'ZMLRuntime.dll') -Force
    $ErrorActionPreference = 'Continue' # native stderr is expected for this test
    $output = & $run --game $game 2>&1
    $exit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    $output | Write-Output
    if ($exit -eq 0) { throw 'Rejected runtime unexpectedly passed' }
    $child = ChildPid $output
    if (Get-Process -Id $child -ErrorAction SilentlyContinue) { throw 'Failed child was not rolled back' }
    Write-Output 'PASS: real LoadLibrary injection and exact-child failure rollback'
} finally {
    $resolved = (Resolve-Path -LiteralPath $root).Path
    $boundary = [IO.Path]::GetTempPath().TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Temp path boundary failed' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
