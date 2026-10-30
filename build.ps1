param([switch]$Test, [switch]$UiTest, [switch]$MotionTest, [switch]$ManualUiTest, [switch]$CompanionTest, [string]$OutputDirectory, [string]$VerificationDirectory, [ValidateSet('anycpu','x86','x64')][string]$Platform = 'x86')
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
$sources = @('State.cs','Monitor.cs','NativeWindow.cs','LayeredDragonRig.cs','LayeredPet.cs','PetActions.cs','Companion.cs','LocalCompanion.cs','App.cs') | ForEach-Object { Join-Path (Join-Path $petRoot 'src') $_ }
& $compiler /nologo /target:winexe /optimize+ ('/platform:' + $Platform) ('/win32icon:' + $petIcon) '/codepage:65001' ('/out:' + (Join-Path $petBin 'CodexDragonPet.exe')) @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
& $compiler /nologo /target:winexe /optimize+ ('/platform:' + $Platform) ('/win32icon:' + $petIcon) ('/out:' + (Join-Path $petBin 'CodexPetWatcher.exe')) /r:System.dll /r:System.Core.dll (Join-Path $petRoot 'src/CodexWatcher.cs')
if ($LASTEXITCODE -ne 0) { throw 'Codex link build failed.' }
Copy-Item -LiteralPath (Join-Path $petRoot 'assets') -Destination $petBin -Recurse -Force
Copy-Item -LiteralPath (Join-Path $petRoot 'CodexDragonPet.exe.config') -Destination $petBin -Force
Copy-Item -LiteralPath (Join-Path $petRoot 'CodexDragonPet.exe.config') -Destination (Join-Path $petBin 'CodexPetWatcher.exe.config') -Force
if ($Test) {
    $tests = @($sources) + (Join-Path $petRoot 'tests/Tests.cs')
    & $compiler /nologo /target:exe /main:Tests /optimize+ ('/platform:' + $Platform) '/codepage:65001' ('/out:' + (Join-Path $petBin 'PetTests.exe')) @references @tests
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    & (Join-Path $petBin 'PetTests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
if ($UiTest) {
    $layeredTests = @($sources) + (Join-Path $petRoot 'tests/LayeredUiIntegration.cs')
    & $compiler /nologo /target:exe /main:LayeredUiIntegration /optimize+ ('/platform:' + $Platform) '/codepage:65001' ('/out:' + (Join-Path $petBin 'PetLayeredUiTests.exe')) @references @layeredTests
    if ($LASTEXITCODE -ne 0) { throw 'Layered UI test build failed.' }
    $uiEvidence = if ($VerificationDirectory) { [IO.Path]::GetFullPath($VerificationDirectory) } else { Join-Path $petRoot 'verification/ui-integration' }
    & (Join-Path $petBin 'PetLayeredUiTests.exe') (Join-Path $uiEvidence 'layered')
    if ($LASTEXITCODE -ne 0) { throw 'Layered UI integration failed.' }
    $uiTests = @($sources) + (Join-Path $petRoot 'tests/UiIntegration.cs')
    & $compiler /nologo /target:exe /main:UiIntegration /optimize+ ('/platform:' + $Platform) '/codepage:65001' ('/out:' + (Join-Path $petBin 'PetUiTests.exe')) @references @uiTests
    if ($LASTEXITCODE -ne 0) { throw 'UI test build failed.' }
    $uiEvidence = if ($VerificationDirectory) { [IO.Path]::GetFullPath($VerificationDirectory) } else { Join-Path $petRoot 'verification/ui-integration' }
    & (Join-Path $petBin 'PetUiTests.exe') $uiEvidence
    if ($LASTEXITCODE -ne 0) { throw 'UI integration failed.' }
    $actionTests = @($sources) + (Join-Path $petRoot 'tests/ActionUiIntegration.cs')
    & $compiler /nologo /target:exe /main:ActionUiIntegration /optimize+ ('/platform:' + $Platform) '/codepage:65001' ('/out:' + (Join-Path $petBin 'PetActionUiTests.exe')) @references @actionTests
    if ($LASTEXITCODE -ne 0) { throw 'Action UI test build failed.' }
    & (Join-Path $petBin 'PetActionUiTests.exe') (Join-Path $uiEvidence 'actions')
    if ($LASTEXITCODE -ne 0) { throw 'Action UI integration failed.' }
}
if ($MotionTest) {
    & $compiler /nologo /target:exe /main:MotionRenderTests /optimize+ ('/platform:' + $Platform) '/codepage:65001' ('/out:' + (Join-Path $petBin 'MotionRenderTests.exe')) @references (Join-Path $petRoot 'src/LayeredDragonRig.cs') (Join-Path $petRoot 'tests/MotionRenderTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Motion test build failed.' }
    $motionEvidence = if ($VerificationDirectory) { [IO.Path]::GetFullPath($VerificationDirectory) } else { Join-Path $petRoot 'verification/ui-integration' }
    & (Join-Path $petBin 'MotionRenderTests.exe') (Join-Path $motionEvidence 'motion')
    if ($LASTEXITCODE -ne 0) { throw 'Motion continuity checks failed.' }
}
if ($CompanionTest) {
    & $compiler /nologo /target:exe /main:LocalCompanionIntegration /optimize+ ('/platform:' + $Platform) '/codepage:65001' ('/out:' + (Join-Path $petBin 'PetCompanionTests.exe')) @references @sources (Join-Path $petRoot 'tests/LocalCompanionIntegration.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Companion test build failed.' }
    $companionEvidence = if ($VerificationDirectory) { [IO.Path]::GetFullPath($VerificationDirectory) } else { Join-Path $petRoot 'verification/companion' }
    & (Join-Path $petBin 'PetCompanionTests.exe') (Join-Path $companionEvidence 'companion')
    if ($LASTEXITCODE -ne 0) { throw 'Companion integration failed.' }
}
if ($ManualUiTest) {
    $manualTests = @($sources) + (Join-Path $petRoot 'tests/ManualActionUi.cs')
    & $compiler /nologo /target:winexe /main:ManualActionUi /optimize+ ('/platform:' + $Platform) '/codepage:65001' ('/out:' + (Join-Path $petBin 'PetManualUiTests.exe')) @references @manualTests
    if ($LASTEXITCODE -ne 0) { throw 'Manual UI test build failed.' }
}
Write-Output ('Built: ' + (Join-Path $petBin 'CodexDragonPet.exe'))
