param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$destination = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $destination) { throw 'Use a new output directory to avoid stale character frames.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$compilerOutput = Join-Path ([IO.Path]::GetDirectoryName($destination)) ('PackWhiteDragon-' + [Guid]::NewGuid().ToString('N') + '.exe')
$references = @('System.dll','System.Core.dll','System.Xaml.dll') | ForEach-Object { '/r:' + (Join-Path $framework $_) }
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/r:' + (Join-Path (Join-Path $framework 'WPF') $_) }
& (Join-Path $framework 'csc.exe') /nologo /target:exe /main:PackWhiteDragon /codepage:65001 ('/out:' + $compilerOutput) @references (Join-Path $PSScriptRoot 'PackWhiteDragon.cs')
if ($LASTEXITCODE -ne 0) { throw 'Sprite registration compiler failed' }
& $compilerOutput (Join-Path $project 'assets/white-dragon-v2/sheets') $destination
if ($LASTEXITCODE -ne 0) { throw 'Sprite registration failed' }
