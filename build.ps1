param([switch]$Test, [switch]$UiTest, [string]$OutputDirectory, [ValidateSet('anycpu','x86','x64')][string]$Platform = 'x86')
$ErrorActionPreference = 'Stop'
$petRoot = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework 4.x compiler was not found.' }
$petBin = if ($OutputDirectory) { [System.IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $petRoot 'bin' }
New-Item -ItemType Directory -Path $petBin -Force | Out-Null
$petIcon = Join-Path $petRoot 'assets/tray.ico'
if (-not (Test-Path -LiteralPath $petIcon) -or (Get-Item -LiteralPath (Join-Path $petRoot 'assets/tray-icon.png')).LastWriteTimeUtc -gt (Get-Item -LiteralPath $petIcon).LastWriteTimeUtc) {
    & (Join-Path $petRoot 'scripts/Export-TrayIcon.ps1') | Out-Null
}
$references = @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xaml.dll') | ForEach-Object { '/r:' + (Join-Path $framework $_) }
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/r:' + (Join-Path (Join-Path $framework 'WPF') $_) }
$sources = @('State.cs','Monitor.cs','NativeWindow.cs','Companion.cs','App.cs') | ForEach-Object { Join-Path (Join-Path $petRoot 'src') $_ }
& $compiler /nologo /target:winexe /optimize+ ('/platform:' + $Platform) ('/win32icon:' + $petIcon) '/codepage:65001' ('/out:' + (Join-Path $petBin 'CodexDragonPet.exe')) @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $petRoot 'assets') -Destination $petBin -Recurse -Force
Copy-Item -LiteralPath (Join-Path $petRoot 'CodexDragonPet.exe.config') -Destination $petBin -Force
if ($Test) {
    $tests = @((Join-Path $petRoot 'src/State.cs'),(Join-Path $petRoot 'src/Monitor.cs'),(Join-Path $petRoot 'tests/Tests.cs'))
    & $compiler /nologo /target:exe /optimize+ ('/platform:' + $Platform) '/codepage:65001' ('/out:' + (Join-Path $petBin 'PetTests.exe')) @references @tests
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    & (Join-Path $petBin 'PetTests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
if ($UiTest) {
    $uiTests = @($sources) + (Join-Path $petRoot 'tests/UiIntegration.cs')
    & $compiler /nologo /target:exe /main:UiIntegration /optimize+ ('/platform:' + $Platform) '/codepage:65001' ('/out:' + (Join-Path $petBin 'PetUiTests.exe')) @references @uiTests
    if ($LASTEXITCODE -ne 0) { throw 'UI test build failed.' }
    & (Join-Path $petBin 'PetUiTests.exe') (Join-Path $petRoot 'verification/ui-integration')
    if ($LASTEXITCODE -ne 0) { throw 'UI integration failed.' }
}
Write-Output ('Built: ' + (Join-Path $petBin 'CodexDragonPet.exe'))
