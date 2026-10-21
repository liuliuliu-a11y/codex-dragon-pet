param([string]$OutputDirectory, [switch]$Check)
$ErrorActionPreference = 'Stop'
$layerSampleRoot = $PSScriptRoot
$sharedRig = Join-Path $layerSampleRoot '../../src/LayeredDragonRig.cs'
$layerOutput = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $layerSampleRoot 'output' }
New-Item -ItemType Directory -Path $layerOutput -Force | Out-Null
$layerFramework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$layerRefs = @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Xaml.dll') | ForEach-Object { '/r:' + (Join-Path $layerFramework $_) }
$layerRefs += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/r:' + (Join-Path (Join-Path $layerFramework 'WPF') $_) }
& (Join-Path $layerFramework 'csc.exe') /nologo /target:winexe /optimize+ /platform:x64 /codepage:65001 ('/out:' + (Join-Path $layerOutput 'LayeredDragonPreview.exe')) @layerRefs $sharedRig (Join-Path $layerSampleRoot 'Preview.cs')
if ($LASTEXITCODE -ne 0) { throw 'Layered sample build failed.' }
New-Item -ItemType Directory -Path (Join-Path $layerOutput 'assets') -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $layerSampleRoot '../../assets/layered') -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $layerOutput 'assets') -Force }
if ($Check) {
    & (Join-Path $layerFramework 'csc.exe') /nologo /target:exe /optimize+ /platform:x64 /codepage:65001 ('/out:' + (Join-Path $layerOutput 'RigChecks.exe')) @layerRefs $sharedRig (Join-Path $layerSampleRoot 'RigChecks.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Rig check build failed.' }
    & (Join-Path $layerOutput 'RigChecks.exe') $layerOutput
    if ($LASTEXITCODE -ne 0) { throw 'Rig checks failed.' }
}
Write-Output (Join-Path $layerOutput 'LayeredDragonPreview.exe')
