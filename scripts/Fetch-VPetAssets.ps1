param([Parameter(Mandatory=$true)][string]$OutputDirectory,
      [Parameter(Mandatory=$true)][string]$TreeFile,
      [string]$Commit = '70263b91e3af9d68006b33c95ad63f59858ecbbe')
$ErrorActionPreference = 'Stop'
$destination = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$prefix = 'VPet-Simulator.Windows/mod/0000_core/pet/'
$entries = Get-Content -LiteralPath $TreeFile -Raw -Encoding UTF8 | ConvertFrom-Json
$selected = @($entries | Where-Object {
    $_.size -and (
      $_.path -eq ($prefix + 'vup.lps') -or
      ($_.path -match '/vup/(Default|Raise|Touch_Head|Touch_Body|Sleep|Say|StartUP|IDEL)/' -and $_.path -match 'Nomal|nomal|\.lps$') -or
      ($_.path -match '/vup/MOVE/(walk\.left|walk\.right|crawl\.left|crawl\.right)/' -and $_.path -match 'Nomal|nomal') -or
      ($_.path -match '/vup/WORK/(WorkONE|Study|PlayONE)/' -and $_.path -match 'Nomal|nomal') -or
      ($_.path -match '/vup/Eat/Nomal/' -or $_.path -match '/vup/Drink/(Nomal/|front_lay/|info\.lps$)')
    )
})
Write-Output ('Downloading ' + $selected.Count + ' verified VPet files (' + [Math]::Round(($selected | Measure-Object size -Sum).Sum / 1MB,1) + ' MiB).')
$failures = @($selected | ForEach-Object -ThrottleLimit 10 -Parallel {
    $entry = $_
    $relative = $entry.path.Substring($using:prefix.Length)
    $local = Join-Path $using:destination $relative
    function Test-Blob([string]$path, [string]$expected) {
        if (-not [IO.File]::Exists($path)) { return $false }
        $data = [IO.File]::ReadAllBytes($path)
        $header = [Text.Encoding]::ASCII.GetBytes('blob ' + $data.Length + [char]0)
        $sha = [Security.Cryptography.SHA1]::Create()
        try { $sha.TransformBlock($header,0,$header.Length,$null,0) | Out-Null; $sha.TransformFinalBlock($data,0,$data.Length) | Out-Null; return ([BitConverter]::ToString($sha.Hash).Replace('-','').ToLowerInvariant() -eq $expected) }
        finally { $sha.Dispose() }
    }
    if (Test-Blob $local $entry.sha) { return }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($local)) | Out-Null
    $encoded = (($entry.path -split '/') | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/'
    $uri = 'https://raw.githubusercontent.com/LorisYounger/VPet/' + $using:Commit + '/' + $encoded
    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        try {
            Invoke-WebRequest -Uri $uri -OutFile $local -TimeoutSec 45 -ErrorAction Stop
            if (-not (Test-Blob $local $entry.sha)) { throw 'Git blob verification mismatch' }
            return
        } catch { if ($attempt -eq 2) { Write-Output ($relative + ': ' + $_.Exception.Message) } }
    }
})
if ($failures.Count) { $failures | Write-Output; throw ('Failed VPet files: ' + $failures.Count) }
# Keep complete animation segments; remove movement/work references outside this curated pack.
$configuration = Join-Path $destination 'vup.lps'
$lines = Get-Content -LiteralPath $configuration -Encoding UTF8
$lines = $lines | Where-Object {
    if ($_ -match '^move:') { return $_ -match 'graph#(walk\.left|walk\.right|crawl\.left|crawl\.right):' }
    if ($_ -match '^work:') { return $_ -match 'Graph#(workone|study|playone):' }
    return $true
}
$lines | Set-Content -LiteralPath $configuration -Encoding UTF8
@{ repository='https://github.com/LorisYounger/VPet'; commit=$Commit; files=$selected.Count; bytes=($selected | Measure-Object size -Sum).Sum; assets=$selected; configurationAdapted=$true } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $destination 'provenance.json') -Encoding UTF8
@'
VPet character and animation assets: LorisYounger/VPet, VUP-Simulator team.
Source: https://github.com/LorisYounger/VPet
Animation license: https://github.com/LorisYounger/VPet#动画版权声明与授权
This non-commercial build preserves the source attribution. Animation files must not be sold.
The upstream code is licensed separately under Apache-2.0.
The curated vup.lps excludes absent movements and work animations; original frame names and timing are retained.
'@ | Set-Content -LiteralPath (Join-Path $destination 'ATTRIBUTION.txt') -Encoding UTF8
Write-Output ('VPet assets ready: ' + $destination)
