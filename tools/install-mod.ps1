param([Parameter(Mandatory=$true)][string]$ModPackage, [string]$PackageRoot='')
$ErrorActionPreference='Stop'
if(-not $PackageRoot){$PackageRoot=Join-Path (Split-Path $PSScriptRoot -Parent) 'build/package/Release'}
$source=(Resolve-Path -LiteralPath $ModPackage).Path
New-Item -ItemType Directory -Force -Path $PackageRoot | Out-Null
$root=(Resolve-Path -LiteralPath $PackageRoot).Path
function Assert-PlainPath([string]$path){
    for($p=Get-Item -LiteralPath $path; $p; $p=$p.Parent){
        if($p.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse points are not allowed'}
    }
}
Assert-PlainPath $source
Assert-PlainPath $root
$bundle=Get-Content -LiteralPath (Join-Path $source 'zml-package.json') -Encoding utf8 -Raw | ConvertFrom-Json
$id=[string]$bundle.id
if($id -notmatch '^[a-z0-9][a-z0-9._-]{0,63}$'){throw 'Invalid Mod package id'}
$files=@($bundle.files)
if($files.Count -lt 2 -or $files.Count -gt 256 -or -not($files -contains 'mod.ini')){throw 'Invalid file whitelist'}
$seen=@{}
foreach($file in $files){
    if($file -isnot [string] -or -not $file -or $file -eq 'zml-package.json' -or [IO.Path]::IsPathRooted($file) -or $file -match '(^|[\\/])\.\.([\\/]|$)|:|(^|[\\/])(\.git|artifacts|logs)([\\/]|$)' -or $seen.ContainsKey($file)){throw 'Unsafe/duplicate package file'}
    $seen[$file]=$true
    $absolute=[IO.Path]::GetFullPath((Join-Path $source $file))
    if(-not $absolute.StartsWith($source+'\',[StringComparison]::OrdinalIgnoreCase) -or -not(Test-Path -LiteralPath $absolute -PathType Leaf)){throw "Missing/outside package file: $file"}
    for($p=Get-Item -LiteralPath $absolute; $p -and $p.FullName.StartsWith($source,[StringComparison]::OrdinalIgnoreCase); $p=$p.Parent){
        if($p.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse points are not allowed'}
        if($p -is [IO.FileInfo]){$p=$p.Directory;if($p.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse points are not allowed'}}
    }
}
$ini=Get-Content -LiteralPath (Join-Path $source 'mod.ini') -Encoding utf8 -Raw
if($ini -notmatch '(?m)^id=([^\r\n]+)\r?$' -or $Matches[1] -ne $id){throw 'Mod id/manifest mismatch'}
if($ini -notmatch '(?m)^library=([^\r\n]+)\r?$' -or -not($files -contains $Matches[1])){throw 'Library is not in the package whitelist'}
$mods=Join-Path $root 'mods';New-Item -ItemType Directory -Force $mods | Out-Null
Assert-PlainPath $mods
$target=[IO.Path]::GetFullPath((Join-Path $mods $id))
if(-not $target.StartsWith($mods+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Target boundary check failed'}
$guid=[guid]::NewGuid().ToString('N')
$stage=Join-Path $root ('.mod-staging-'+$guid)
$backup=Join-Path $root ('.mod-backups/'+$id+'-'+$guid)
foreach($managed in @('.mod-backups')){
    $dir=Join-Path $root $managed
    if(Test-Path -LiteralPath $dir){Assert-PlainPath $dir}
}
New-Item -ItemType Directory -Force $stage | Out-Null
foreach($file in $files + @('zml-package.json')){
    $dest=Join-Path $stage $file
    New-Item -ItemType Directory -Force (Split-Path $dest -Parent) | Out-Null
    Copy-Item -LiteralPath (Join-Path $source $file) -Destination $dest
}
$previous=$false
try {
    if(Test-Path -LiteralPath $target){
        if(-not(Test-Path -LiteralPath $target -PathType Container)){throw 'Mod target is not a directory'}
        Assert-PlainPath $target
        New-Item -ItemType Directory -Force (Split-Path $backup -Parent) | Out-Null
        Move-Item -LiteralPath $target -Destination $backup
        $previous=$true
    }
    Move-Item -LiteralPath $stage -Destination $target
} catch {
    if($previous -and -not(Test-Path -LiteralPath $target)){Move-Item -LiteralPath $backup -Destination $target}
    throw
}
# Keep the previous directory as an explicit, reversible artifact backup.
Write-Output "Installed $id -> $target"
if($previous){Write-Output "Backup -> $backup"}
