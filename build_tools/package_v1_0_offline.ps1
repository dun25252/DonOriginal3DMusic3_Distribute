# Ver.1.0 developer-only packager, Windows PowerShell 5.1, no Python/.NET SDK needed.
# User-facing release contains EXE + assets + tools + licenses + 2 short TXT ONLY.
[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$V148Zip,
  [Parameter(Mandatory=$true)][string]$ExeArtifact,
  [Parameter(Mandatory=$true)][string]$OutDir
)
$ErrorActionPreference = 'Stop'
try {
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = 'DonOriginal3DMusic3_v1.0'
$final = Join-Path $OutDir 'DonOriginal3DMusic3_v1.0_Windows.zip'
$tempZip = $final + '.building'
$expectedSize = [long]513598015
$expectedHash = '9f2de7b19f1b82bf2d4fe14b4c44f6d821c83115feeee3614c12cc90ff04c5da'
$needed = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($x in @(
 'assets/hrtf/hutubs_pp3.sofa',
 'tools/ffmpeg/bin/ffmpeg.exe',
 'tools/ffmpeg/bin/ffprobe.exe',
 'tools/stem_separator/stem_separator.exe',
 'licenses/HT-Demucs_LICENSE.txt',
 'licenses/demucs-onnx_LICENSE.txt')) { [void]$needed.Add($x) }
foreach ($d in @('F','B')) {
  foreach ($e in @('L','R')) {
    [void]$needed.Add(('assets/brir/iosr_' + $d + '_room_' + $e + '.wav'))
  }
}
foreach ($side in @('sideL','sideR')) {
  foreach ($e in @('L','R')) {
    [void]$needed.Add(('assets/brir/iosr_F_' + $side + '_room_' + $e + '.wav'))
  }
}

function Resource-Path([string]$raw) {
  $raw = $raw.Replace('\','/')
  if (!$raw -or $raw.StartsWith('/') -or $raw.EndsWith('/')) { return $null }
  $parts = $raw.Split('/')
  foreach ($p in $parts) {
    if (!$p -or $p -in @('.','..') -or $p.Contains(':')) { return $null }
  }
  $start = -1
  for ($i=0; $i -lt $parts.Length; $i++) {
    if ($parts[$i] -in @('assets','tools','licenses')) { $start=$i; break }
  }
  if ($start -lt 0 -or $start -ge $parts.Length-1) { return $null }
  $rel = $parts[$start..($parts.Length-1)] -join '/'
  $lower = $rel.ToLowerInvariant()
  $leaf = $parts[-1].ToLowerInvariant()
  if ($lower.StartsWith('assets/brir/')) {
    if ($needed.Contains($rel)) { return $rel }
    return $null
  }
  if ($lower -eq 'assets/hrtf/hutubs_pp3.sofa') { return 'assets/hrtf/hutubs_pp3.sofa' }
  if ($lower.StartsWith('assets/models/demucs_onnx_cache/')) { return $rel }
  if ($lower.StartsWith('licenses/')) {
    if ($leaf -in @('ht-demucs_license.txt','demucs-onnx_license.txt',
      'license','license.txt','license.md','copying','copying.txt','copyright','notice','notice.txt')) {
      return $rel
    }
    return $null
  }
  if ($lower.StartsWith('tools/ffmpeg/') -or $lower.StartsWith('tools/stem_separator/')) {
    if ($leaf -in @('thumbs.db','desktop.ini','donoriginal3dmusic3.exe','readme.md')) { return $null }
    if ($leaf -match '\.(cmd|bat|ps1|log|pdb|bak|cs|csproj|pyc|py)$') { return $null }
    if ($leaf -match 'debug|positiontrace|stagec_|staged_|stagee_|音響検査|測定と出典') { return $null }
    return $rel
  }
  return $null
}

function Zip-CopyFile([string]$src, $dstArchive, [string]$name) {
  $entry = $dstArchive.CreateEntry($name,[IO.Compression.CompressionLevel]::Optimal)
  $input = [IO.File]::OpenRead($src)
  $output = $entry.Open()
  try { $input.CopyTo($output,1048576) }
  finally { $output.Dispose(); $input.Dispose() }
}
function Zip-CopyEntry($srcEntry, $dstArchive, [string]$name) {
  $entry = $dstArchive.CreateEntry($name,[IO.Compression.CompressionLevel]::Optimal)
  $input = $srcEntry.Open()
  $output = $entry.Open()
  try { $input.CopyTo($output,1048576) }
  finally { $output.Dispose(); $input.Dispose() }
}
function Zip-Text([string]$text, $dstArchive, [string]$name, [bool]$withBom) {
  $encoder = [Text.UTF8Encoding]::new($withBom)
  [byte[]]$bytes = @($encoder.GetPreamble()) + @($encoder.GetBytes($text))
  $entry = $dstArchive.CreateEntry($name,[IO.Compression.CompressionLevel]::Optimal)
  $out = $entry.Open()
  try { $out.Write($bytes,0,$bytes.Length) }
  finally { $out.Dispose() }
}

if (!(Test-Path -LiteralPath $V148Zip -PathType Leaf)) { throw 'v1.48の素材ZIPがありません。' }
if (!(Test-Path -LiteralPath $ExeArtifact -PathType Leaf)) { throw 'Ver.1.0 EXE成果物がありません。' }
if (!(Test-Path -LiteralPath $OutDir -PathType Container)) { throw '保存先フォルダーがありません。' }

Write-Output 'STEP 1/6: v1.48 ZIPのサイズとSHA-256を照合'
if ((Get-Item -LiteralPath $V148Zip).Length -ne $expectedSize) { throw 'v1.48 ZIPのサイズが一致しません。' }
if ((Get-FileHash -LiteralPath $V148Zip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) {
  throw 'v1.48 ZIPのSHA-256が一致しません。'
}
$work = Join-Path ([IO.Path]::GetTempPath()) ('DonOriginal3DMusic3_v1.0_pack_' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $work -Force
try {
  Write-Output 'STEP 2/6: Ver.1.0 EXEを検出して実行バージョンを確認'
  $exe = Join-Path $work 'DonOriginal3DMusic3.exe'
  if ([IO.Path]::GetExtension($ExeArtifact).Equals('.exe',[StringComparison]::OrdinalIgnoreCase)) {
    [IO.File]::Copy($ExeArtifact,$exe)
  }
  else {
    $artifact = [IO.Compression.ZipFile]::OpenRead($ExeArtifact)
    try {
      $matching = @($artifact.Entries | Where-Object {
        [IO.Path]::GetFileName($_.FullName) -ieq 'DonOriginal3DMusic3.exe'
      })
      if ($matching.Count -ne 1) { throw '成果物ZIPにはDonOriginal3DMusic3.exeが1件必要です。' }
      $input = $matching[0].Open()
      $output = [IO.File]::Create($exe)
      try { $input.CopyTo($output,1048576) }
      finally { $output.Dispose(); $input.Dispose() }
    }
    finally { $artifact.Dispose() }
  }
  if ((Get-Item -LiteralPath $exe).Length -lt 100000) { throw 'EXEが小さすぎます。' }
  $v = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
  if ($v.FileVersion -ne '1.0.0.0' -or $v.ProductVersion -ne '1.0.0.0') {
    throw ('Ver.1.0ではないEXEです: ' + $v.FileVersion + ' / ' + $v.ProductVersion)
  }

  Write-Output 'STEP 3/6: Windows EXE自身の自己検査'
  $errorReport = Join-Path $env:TEMP 'DonOriginal3DMusic3_v1.0_selftest_failure.txt'
  Remove-Item -LiteralPath $errorReport -ErrorAction SilentlyContinue
  $p = Start-Process -FilePath $exe -ArgumentList '--self-test' -Wait -PassThru -WindowStyle Hidden
  if ($p.ExitCode -ne 0) {
    $details = ''
    if (Test-Path -LiteralPath $errorReport) { $details = Get-Content -LiteralPath $errorReport -Raw }
    throw ('EXE自己検査に失敗: ' + $details)
  }

  Write-Output 'STEP 4/6: 必須のH3/IoSR/HTDemucs/FFmpegとライセンスを収集'
  $sourceZip = [IO.Compression.ZipFile]::OpenRead($V148Zip)
  try {
    $map = [System.Collections.Generic.List[object]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $modelPresent = $false
    $totalBytes = [long]0
    Write-Output ('ZIP全項目数: '+$sourceZip.Entries.Count)
    $exampleResources = @($sourceZip.Entries | Where-Object {
      $_.FullName -match '(^|/)(assets|tools|licenses)/'
    } | Select-Object -First 8 -ExpandProperty FullName)
    if ($exampleResources.Count -gt 0) {
      Write-Output ('ZIP内素材の先頭例: ' + ($exampleResources -join '; '))
    }
    foreach ($entry in $sourceZip.Entries) {
      $rel = Resource-Path $entry.FullName
      if (!$rel) { continue }
      if (!$seen.Add($rel)) { throw ('同名素材の重複: ' + $rel) }
      if ($entry.Length -le 0) { throw ('空の素材: ' + $rel) }
      $totalBytes += [long]$entry.Length
      if ($totalBytes -gt 8589934592) { throw '展開サイズが安全上限8GBを超えます。' }
      if ($rel.ToLowerInvariant().StartsWith('assets/models/demucs_onnx_cache/') -and
          $rel.ToLowerInvariant().EndsWith('.onnx')) { $modelPresent = $true }
      $map.Add(@{Name=$rel;Entry=$entry})
    }
    Write-Output ('抽出候補: ' + $map.Count + 'ファイル / 展開推定 '+ $totalBytes +' bytes')
    $missingAssets = @($needed | Where-Object { -not $seen.Contains($_) } | Sort-Object)
    if ($missingAssets.Count -gt 0) {
      Write-Output ('検出したassets/例: ' +
        ((@($map | Where-Object { $_.Name -like 'assets/*' } |
          Select-Object -First 12 | ForEach-Object { $_.Name })) -join '; '))
      throw ('必須素材が見つかりません: ' + ($missingAssets -join ', '))
    }
    if (!$modelPresent) {
      $modelPaths = @($sourceZip.Entries | Where-Object {
        $_.FullName -match '(?i)demucs|onnx'
      } | Select-Object -First 15 -ExpandProperty FullName)
      throw ('HTDemucsのONNXモデルを検出できません。ZIP内候補: '+($modelPaths -join '; '))
    }
    Write-Output ('素材数: ' + $map.Count)

    $readme = @'
DonOriginal3DMusic3 Ver.1.0

Windows用の3D音楽変換ソフトです。複数曲をまとめて変換できます。

使い方
1. DonOriginal3DMusic3.exeを起動
2. 音楽ファイルを追加して保存先を選択
3. 「選択した曲をまとめて3D変換」を押す

保存先: 3D変換先
ファイル名: 曲名_v1.0.flac（FLAC 48kHz/24bit）

※ assetsとtoolsは必要なので削除しないでください。
※ 元の曲は変更しません。
※ 使用する音源の権利を確認してください。
'@
    $notice = @'
Third-party materials / DonOriginal3DMusic3 Ver.1.0

HUTUBS measured HRIR/HRTF (pp3)
https://doi.org/10.14279/depositonce-8487   CC BY 4.0
IoSR Listening Room measured BRIR (University of Surrey)
https://doi.org/10.15126/surreydata.00813511   CC BY 4.0
FFmpeg: https://ffmpeg.org/legal.html (build-specific licensing requirements)
HT-Demucs/demucs-onnx: original MIT license texts included under licenses/.

Before public release, verify FFmpeg license/source-code provisions,
all model dependencies and upstream attribution requirements.
'@

    Write-Output 'STEP 5/6: 配布用ZIPを組み立て'
    if (Test-Path -LiteralPath $tempZip) { Remove-Item -LiteralPath $tempZip -Force }
    $stream = [IO.File]::Open($tempZip,[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    $destZip = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$false)
    try {
      Zip-CopyFile $exe $destZip ($root+'/DonOriginal3DMusic3.exe')
      Zip-Text $readme $destZip ($root+'/README.txt') $true
      Zip-Text $notice $destZip ($root+'/THIRD_PARTY_NOTICES.txt') $false
      foreach ($item in $map) { Zip-CopyEntry $item.Entry $destZip ($root+'/'+$item.Name) }
    }
    finally { $destZip.Dispose() }
  }
  finally { $sourceZip.Dispose() }

  Write-Output 'STEP 6/6: ZIPの内容・全ファイルの展開検査'
  $out = [IO.Compression.ZipFile]::OpenRead($tempZip)
  try {
    $names = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $out.Entries) {
      if (!$entry.FullName.StartsWith($root+'/',[StringComparison]::Ordinal) -or
          $entry.FullName -match '\.(cmd|bat|ps1|pdb|log|cs|csproj)$' -or
          $entry.FullName -match '管理用|positiontrace|_audit') {
        throw ('配布ZIPに不要なファイル: ' + $entry.FullName)
      }
      [void]$names.Add($entry.FullName.Substring($root.Length+1))
      $unzip = $entry.Open()
      try { $unzip.CopyTo([IO.Stream]::Null,1048576) }
      finally { $unzip.Dispose() }
    }
    foreach ($must in $needed) {
      if (!$names.Contains($must)) { throw ('生成ZIPに素材不足: ' + $must) }
    }
    foreach ($must in @('DonOriginal3DMusic3.exe','README.txt','THIRD_PARTY_NOTICES.txt')) {
      if (!$names.Contains($must)) { throw ('生成ZIPに必須ファイル不足: ' + $must) }
    }
  }
  finally { $out.Dispose() }
  if (Test-Path -LiteralPath $final) { [IO.File]::Replace($tempZip,$final,$null) }
  else { [IO.File]::Move($tempZip,$final) }
  Write-Output ('完成: ' + $final)
  Write-Output ('サイズ: ' + (Get-Item -LiteralPath $final).Length + ' bytes')
  Write-Output ('SHA256: ' + (Get-FileHash -LiteralPath $final -Algorithm SHA256).Hash)
  Write-Output '注意: 公開前に実曲4曲の変換・音質・ライセンス条件を別途確認してください。'
  Write-Output 'BUILD_SUCCESS'
}
finally {
  if (Test-Path -LiteralPath $tempZip) {
    Remove-Item -LiteralPath $tempZip -Force -ErrorAction SilentlyContinue
  }
  Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

catch {
  Write-Output ('PACKAGING_ERROR: ' + $_.Exception.Message)
  Write-Output ('ERROR_TYPE: ' + $_.Exception.GetType().FullName)
  if ($_.ScriptStackTrace) {
    Write-Output ('SCRIPT_STACK: ' + $_.ScriptStackTrace)
  }
  Write-Output ('ERROR_DETAILS: ' + $_.ToString())
  throw
}
