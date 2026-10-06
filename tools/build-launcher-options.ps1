param([string]$QtSource, [string]$NativeQtDir='D:\Game\Hypergryph Launcher\1.6.0', [switch]$SkipTests)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(-not $QtSource){$QtSource=Join-Path $root 'artifacts/qtbase'}
if(-not(Test-Path (Join-Path $QtSource 'bin/syncqt.pl'))){
    & gh repo clone qt/qtbase $QtSource -- --branch v5.15.8-lts-lgpl --depth 1 --filter=blob:none --sparse
    if($LASTEXITCODE -ne 0){throw 'Qt header checkout failed'}
    & git -C $QtSource sparse-checkout set src/corelib src/gui src/widgets mkspecs bin
    if($LASTEXITCODE -ne 0){throw 'Qt public header sparse checkout failed'}
}
$sdk=Join-Path $root 'build/qt-sdk'
& python (Join-Path $PSScriptRoot 'prepare-qt-headers.py') --source $QtSource --output $sdk --native $NativeQtDir --perl 'C:/Program Files/Git/usr/bin/perl.exe'
if($LASTEXITCODE -ne 0){throw 'Qt public header setup failed'}
$vs=& "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe" -latest -property installationPath
$vc=Join-Path $vs 'VC/Auxiliary/Build/vcvars64.bat'
$output=Join-Path $root 'build/package/Release';New-Item -ItemType Directory -Force $output | Out-Null
$commands=@('call "'+$vc+'" >nul')
foreach($dll in @('Qt5Core','Qt5Gui','Qt5Widgets')){$commands+='lib /nologo /machine:x64 /def:"'+(Join-Path $sdk ($dll+'.def'))+'" /out:"'+(Join-Path $sdk ($dll+'.lib'))+'"'}
$commands+='cl /nologo /O2 /std:c++17 /EHsc /MT /utf-8 /W4 /LD /DUNICODE /D_UNICODE /DNOMINMAX /DWIN32_LEAN_AND_MEAN /DQT_NO_DEBUG /D_HAS_DEPRECATED_ADAPTOR_TYPEDEFS=1 /D_SILENCE_STDEXT_ARR_ITERS_DEPRECATION_WARNING /I"'+(Join-Path $sdk 'include')+'" /I"'+(Join-Path $sdk 'include/QtCore')+'" /I"'+(Join-Path $sdk 'include/QtGui')+'" /I"'+(Join-Path $sdk 'include/QtWidgets')+'" "'+(Join-Path $root 'src/launcher_options.cpp')+'" /Fo"'+(Join-Path $sdk 'launcher_options.obj')+'" /link /OUT:"'+(Join-Path $output 'ZMLLauncherOptions.dll')+'" /IMPLIB:"'+(Join-Path $sdk 'ZMLLauncherOptions.lib')+'" /LIBPATH:"'+$sdk+'" Qt5Core.lib Qt5Gui.lib Qt5Widgets.lib user32.lib'
& cmd /d /s /c ($commands -join ' && ')
if($LASTEXITCODE -ne 0){throw 'Native Qt options build failed'}
$license="This adapter uses unmodified public Qt 5.15.8 headers (qt/qtbase commit 4ee4fc18b4067b90efa46ca9baba74f53b54d9ec) under LGPLv3. Qt DLLs are not bundled. Source: https://github.com/qt/qtbase/tree/v5.15.8-lts-lgpl . Rebuild/relink instructions: tools/build-launcher-options.ps1 .\r\n\r\n"+[IO.File]::ReadAllText((Join-Path $QtSource 'LICENSE.LGPL3'))+"`r`n"+[IO.File]::ReadAllText((Join-Path $QtSource 'LICENSE.GPL3'))
[IO.File]::WriteAllText((Join-Path $output 'Qt-LICENSE.txt'),$license,[Text.UTF8Encoding]::new($false))
if(-not $SkipTests){
    $test=Join-Path $sdk 'LauncherOptionsTests.exe'
    $compile=$commands[-1].Replace(' /LD ',' ').Replace((Join-Path $root 'src/launcher_options.cpp'),(Join-Path $root 'tests/launcher_options_tests.cpp')).Replace('launcher_options.obj','launcher_options_tests.obj').Replace((Join-Path $output 'ZMLLauncherOptions.dll'),$test).Replace('ZMLLauncherOptions.lib','LauncherOptionsTests.lib')
    & cmd /d /s /c ($commands[0]+' && '+$compile)
    if($LASTEXITCODE -ne 0){throw 'Qt hidden-widget fixture compilation failed'}
    $info=[Diagnostics.ProcessStartInfo]::new($test);$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WindowStyle='Hidden';$info.ErrorDialog=$false;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $info.EnvironmentVariables['PATH']=$NativeQtDir+';'+$env:PATH
    $info.EnvironmentVariables['QT_QPA_PLATFORM_PLUGIN_PATH']=Join-Path $NativeQtDir 'plugins/platforms'
    $info.EnvironmentVariables['QT_FORCE_STDERR_LOGGING']='1'
    $process=[Diagnostics.Process]::Start($info)
    try {if(-not $process.WaitForExit(30000)){$process.Kill();$process.WaitForExit();throw 'Owned Qt test fixture timed out'};Write-Output $process.StandardOutput.ReadToEnd();Write-Output $process.StandardError.ReadToEnd();if($process.ExitCode -ne 0){throw ('Qt hidden-widget checks failed: '+$process.ExitCode)}}finally{$process.Dispose()}
}
