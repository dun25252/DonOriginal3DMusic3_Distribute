<#
 DonOriginal3DMusic3 Ver.1.0 - DEVELOPERS ONLY.
 Not bundled with the customer ZIP.
 Windows x64 host requirements: .NET 8 SDK and Python 3.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)] [string]$V148Zip,
  [string]$Destination = ""
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if (-not $Destination) { $Destination=Join-Path $root 'dist' }
if (-not (Test-Path -LiteralPath $V148Zip -PathType Leaf)) {
  throw "元のv1.48音響素材ZIPが見つかりません: $V148Zip"
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw '.NET 8 SDKが必要です。'
}
$python = if (Get-Command py -ErrorAction SilentlyContinue) { @('py','-3') }
          elseif (Get-Command python -ErrorAction SilentlyContinue) { @('python') }
          else { throw 'Python 3が必要です。' }
$project=Join-Path $PSScriptRoot 'distribution_app\DonOriginal3DMusic3.csproj'
$publish=Join-Path $root '_v1.0_temp_publish'
New-Item -ItemType Directory -Path $publish -Force | Out-Null
Write-Host 'Ver.1.0 Windows GUI をビルド...'
& dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $publish
if ($LASTEXITCODE -ne 0) { throw 'EXEビルドに失敗しました。' }
$exe=Join-Path $publish 'DonOriginal3DMusic3.exe'
if (-not (Test-Path -LiteralPath $exe)) {
  throw 'EXEが生成されませんでした。'
}
$v=[System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
if ($v.FileVersion -ne '1.0.0.0' -or $v.ProductVersion -ne '1.0.0.0') {
  throw "不正なバージョン：FileVersion=$($v.FileVersion), ProductVersion=$($v.ProductVersion)"
}
Write-Host 'EXE自身の保存処理・H3室内反射保持・Stem整列テストを実行...'
$testReport=Join-Path $env:TEMP 'DonOriginal3DMusic3_v1.0_selftest_failure.txt'
Remove-Item -LiteralPath $testReport -ErrorAction SilentlyContinue
$selfTest=Start-Process -FilePath $exe -ArgumentList '--self-test' -PassThru -Wait -WindowStyle Hidden
if ($selfTest.ExitCode -ne 0) {
  $details=if (Test-Path -LiteralPath $testReport) {
    Get-Content -LiteralPath $testReport -Raw
  } else { "不明なセルフテスト失敗（ExitCode=$($selfTest.ExitCode)）" }
  throw "配布EXE自身の必須テストが失敗しました。ZIPを生成しません。`n$details"
}
Write-Host 'VER_1_0_EXE_SELF_TEST_OK'
Write-Host 'v1.48 ZIPを検証して必要データのみで配布ZIPを生成...'
$pack=Join-Path $PSScriptRoot 'package_v1_0.py'
$verify=Join-Path $PSScriptRoot 'verify_v1_0.py'
$pkgArgs=@($pack,'--v148',$V148Zip,'--exe',$exe,'--out',$Destination)
if ($python.Length -eq 2) { & $python[0] $python[1] @pkgArgs }
else { & $python[0] @pkgArgs }
if ($LASTEXITCODE -ne 0) { throw '素材パッケージ作成に失敗しました。' }
$zip=Join-Path $Destination 'DonOriginal3DMusic3_v1.0_Windows.zip'
if ($python.Length -eq 2) { & $python[0] $python[1] $verify --zip $zip }
else { & $python[0] $verify --zip $zip }
if ($LASTEXITCODE -ne 0) { throw '配布ZIPの検証に失敗しました。' }
Write-Host "パッケージ生成: $zip"
Write-Host "ZIP SHA256: $((Get-FileHash $zip -Algorithm SHA256).Hash)"
Write-Host '公開前に4曲一括変換・音質・3D変換先への直接出力を必ず実機検証してください。'
