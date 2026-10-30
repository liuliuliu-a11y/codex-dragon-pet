param([Parameter(Mandatory=$true)][string]$Destination, [Parameter(Mandatory=$true)][string]$DotnetRoot)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$assets=Get-Content -LiteralPath (Join-Path $project 'src/VPetHost/obj/project.assets.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$cache=$assets.packageFolders.PSObject.Properties.Name | Select-Object -First 1
$licenses=Join-Path $Destination 'licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
$notice=@('# 第三方组件声明','','本运行包使用以下组件。许可正文位于 licenses 目录，组件版权按各自声明保留。','')
$apache=Join-Path $cache 'vpet-simulator.core/1.1.0.72/LICENSE'
foreach($entry in $assets.libraries.PSObject.Properties){
    if($entry.Value.type -ne 'package'){continue}
    $folder=Join-Path $cache $entry.Value.path
    $spec=Get-ChildItem -LiteralPath $folder -Filter '*.nuspec' | Select-Object -First 1
    if(-not $spec){throw ('Missing package metadata: '+$entry.Name)}
    [xml]$xml=Get-Content -LiteralPath $spec.FullName -Raw -Encoding UTF8
    $metadata=$xml.package.metadata
    $id=[string]$metadata.id; $version=[string]$metadata.version
    $target=Join-Path $licenses ($id+'-'+$version)
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    $legal=@(Get-ChildItem -LiteralPath $folder -File -Recurse | Where-Object Name -Match '(?i)license|notice')
    $licenseText = if($metadata.license -is [System.Xml.XmlElement]) { $metadata.license.InnerText } else { [string]$metadata.license }
    if($legal.Count -eq 0 -and $licenseText -eq 'Apache-2.0'){
        Copy-Item -LiteralPath $apache -Destination (Join-Path $target 'LICENSE')
    }elseif($legal.Count -eq 0){throw ('Missing license text: '+$entry.Name)}
    else{foreach($file in $legal){Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $target $file.Name) -Force}}
    $notice+=@(('## '+$id+' '+$version),'',('作者：'+[string]$metadata.authors),('版权：'+[string]$metadata.copyright),('项目：'+[string]$metadata.projectUrl),'')
}
foreach($name in @('LICENSE.txt','ThirdPartyNotices.txt')){
    $file=Join-Path $DotnetRoot $name
    if(-not(Test-Path -LiteralPath $file)){throw ('Missing .NET runtime notice: '+$name)}
    Copy-Item -LiteralPath $file -Destination (Join-Path $licenses ('dotnet-'+$name))
}
$notice+=@('## .NET 8 Windows Desktop Runtime','','Microsoft 及其贡献者；许可见 licenses/dotnet-LICENSE.txt 与 licenses/dotnet-ThirdPartyNotices.txt。','','## 角色素材','','白龙图形由本项目以既有白龙形象为参考生成，原始图集随源码保留。VPet 提供动画内核。')
$notice | Set-Content -LiteralPath (Join-Path $Destination 'THIRD-PARTY-NOTICES.md') -Encoding UTF8
