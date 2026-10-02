param([Parameter(Mandatory=$true)][string]$Installer)
$ErrorActionPreference='Stop'
function Hash([string]$path){
    $stream=[IO.File]::OpenRead($path); $sha=[Security.Cryptography.SHA256]::Create()
    try { return [Convert]::ToBase64String($sha.ComputeHash($stream)) } finally { $sha.Dispose();$stream.Dispose() }
}
$root=Join-Path ([IO.Path]::GetTempPath()) ('zml-install-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $root | Out-Null
try {
    $source=Join-Path $root 'source'; $target=Join-Path $root 'package'
    New-Item -ItemType Directory $source | Out-Null
    'id=fixture'+"`nlibrary=Fixture.dll" | Set-Content (Join-Path $source 'mod.ini') -Encoding ascii
    'v1' | Set-Content (Join-Path $source 'Fixture.dll') -Encoding ascii
    $valid=@{id='fixture';files=@('mod.ini','Fixture.dll')}
    $valid | ConvertTo-Json | Set-Content (Join-Path $source 'zml-package.json') -Encoding utf8
    & $Installer -ModPackage $source -PackageRoot $target | Out-Null
    $installed=Join-Path $target 'mods/fixture/Fixture.dll'
    $old=(Hash $installed)
    'v2' | Set-Content (Join-Path $source 'Fixture.dll') -Encoding ascii
    & $Installer -ModPackage $source -PackageRoot $target | Out-Null
    $new=(Hash $installed)
    if($new -eq $old){throw 'Update did not replace package'}
    $backup=@(Get-ChildItem -LiteralPath (Join-Path $target '.mod-backups') -Directory)
    if($backup.Count -ne 1 -or (Hash (Join-Path $backup[0].FullName 'Fixture.dll')) -ne $old){throw 'Backup did not preserve previous release'}
    # Exercise rollback on test copy.
    $current=Join-Path $target 'mods/fixture'
    foreach($path in @($current,$backup[0].FullName)){
        if(-not [IO.Path]::GetFullPath($path).StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Rollback boundary'}
    }
    Move-Item -LiteralPath $current -Destination (Join-Path $root 'updated-release')
    Move-Item -LiteralPath $backup[0].FullName -Destination $current
    if((Hash $installed) -ne $old){throw 'Rollback did not restore original package'}
    foreach($bad in @(
        @{id='fixture';files=@('mod.ini','../outside.dll')},
        @{id='fixture';files=@('mod.ini','missing.dll')},
        @{id='other';files=@('mod.ini','Fixture.dll')},
        @{id='fixture';files=@('mod.ini','Fixture.dll','Fixture.dll')}
    )) {
        $bad | ConvertTo-Json | Set-Content (Join-Path $source 'zml-package.json') -Encoding utf8
        $failed=$false
        try { & $Installer -ModPackage $source -PackageRoot $target | Out-Null } catch { $failed=$true }
        if(-not $failed -or (Hash $installed) -ne $old){throw 'Bad package was accepted or changed current release'}
    }
    Write-Output 'PASS: install/update/backup/rollback and invalid packages leave previous release intact'
} finally {
    $resolved=(Resolve-Path -LiteralPath $root).Path
    $temp=(Resolve-Path -LiteralPath ([IO.Path]::GetTempPath())).Path.TrimEnd('\')
    if(-not $resolved.StartsWith($temp+'\',[StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolved -Leaf) -notlike 'zml-install-test-*'){throw 'Cleanup boundary'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
