param([Parameter(Mandatory=$true)][string]$Dotnet,
      [Parameter(Mandatory=$true)][string]$AssetDirectory,
      [Parameter(Mandatory=$true)][string]$OutputDirectory,
      [switch]$SelfContained, [switch]$Test)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$destination = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$sources = @('State.cs','Monitor.cs','StateBridge.cs') | ForEach-Object { Join-Path (Join-Path $project 'src') $_ }
$references = @('System.dll','System.Core.dll','System.Web.Extensions.dll') | ForEach-Object { '/r:' + (Join-Path $framework $_) }
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /main:CodexDragon.BridgeProgram /optimize+ /platform:x64 /codepage:65001 ('/out:' + (Join-Path $destination 'CodexStateBridge.exe')) @references @sources
if ($LASTEXITCODE -ne 0) { throw 'State bridge compilation failed' }
if ($Test) {
    $testExe = Join-Path $destination 'BridgeTests.exe'
    & (Join-Path $framework 'csc.exe') /nologo /target:exe /main:BridgeTests /optimize+ /platform:x64 /codepage:65001 ('/out:' + $testExe) @references @sources (Join-Path $project 'tests/BridgeTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Bridge test compilation failed' }
    & $testExe (Join-Path $destination 'test-evidence')
    if ($LASTEXITCODE -ne 0) { throw 'Bridge contract validation failed' }
}
& $Dotnet publish (Join-Path $project 'src/VPetHost/VPetHost.csproj') -c Release -r win-x64 --self-contained $SelfContained.ToString().ToLowerInvariant() -o $destination
if ($LASTEXITCODE -ne 0) { throw 'VPet host publish failed' }
New-Item -ItemType Directory -Path (Join-Path $destination 'assets') -Force | Out-Null
$assetTarget = Join-Path $destination 'assets/vpet'
New-Item -ItemType Directory -Path $assetTarget -Force | Out-Null
foreach ($child in Get-ChildItem -LiteralPath $AssetDirectory) { Copy-Item -LiteralPath $child.FullName -Destination $assetTarget -Recurse -Force }
Copy-Item -LiteralPath (Join-Path $project 'assets/tray.ico') -Destination (Join-Path $destination 'assets') -Force
Write-Output ('VPet runtime ready: ' + $destination)
