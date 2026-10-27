# DonOriginal3DMusic3 Ver.1.0 distribution ZIP GUI, Windows PowerShell 5.1.
# Developer utility only: never included inside the public ZIP.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$script:gui = New-Object System.Windows.Forms.Form
$script:gui.Text = 'DonOriginal3DMusic3 Ver.1.0 | 配布ZIP作成'
$script:gui.StartPosition = 'CenterScreen'
$script:gui.Size = [System.Drawing.Size]::new(970,530)
$script:gui.MinimumSize = [System.Drawing.Size]::new(780,440)
$script:gui.Font = [System.Drawing.Font]::new('Yu Gothic UI',10.0)
$script:gui.Padding = [System.Windows.Forms.Padding]::new(12)

$panel = New-Object System.Windows.Forms.TableLayoutPanel
$panel.Dock = 'Fill'
$panel.ColumnCount = 3
$panel.RowCount = 7
$panel.ColumnStyles.Add([System.Windows.Forms.ColumnStyle]::new([System.Windows.Forms.SizeType]::Absolute,210))
$panel.ColumnStyles.Add([System.Windows.Forms.ColumnStyle]::new([System.Windows.Forms.SizeType]::Percent,100))
$panel.ColumnStyles.Add([System.Windows.Forms.ColumnStyle]::new([System.Windows.Forms.SizeType]::Absolute,120))
foreach($h in @(55,51,51,51,53,37)) {
  $panel.RowStyles.Add([System.Windows.Forms.RowStyle]::new([System.Windows.Forms.SizeType]::Absolute,$h))
}
$panel.RowStyles.Add([System.Windows.Forms.RowStyle]::new([System.Windows.Forms.SizeType]::Percent,100))
$script:gui.Controls.Add($panel)

$head = New-Object System.Windows.Forms.Label
$head.Text = 'Ver.1.0のEXEとv1.48素材をまとめる（追加のGitHub Actions不要）'
$head.Dock = 'Fill'
$head.TextAlign = 'MiddleLeft'
$head.Font = [System.Drawing.Font]::new('Yu Gothic UI',12.0,[System.Drawing.FontStyle]::Bold)
$panel.Controls.Add($head,0,0)
$panel.SetColumnSpan($head,3)

function Add-PathPicker([int]$row,[string]$labelText,[bool]$isFolder,[string]$fileFilter) {
  $label=New-Object System.Windows.Forms.Label
  $label.Text=$labelText
  $label.TextAlign='MiddleLeft'
  $label.Dock='Fill'
  $panel.Controls.Add($label,0,$row)

  $field=New-Object System.Windows.Forms.TextBox
  $field.Dock='Fill'
  $field.Margin=[System.Windows.Forms.Padding]::new(0,12,0,0)
  $panel.Controls.Add($field,1,$row)

  $browse=New-Object System.Windows.Forms.Button
  $browse.Text='参照...'
  $browse.Dock='Fill'
  $browse.Margin=[System.Windows.Forms.Padding]::new(7,5,0,4)
  $browse.Tag=@{field=$field;folder=$isFolder;filter=$fileFilter;description=$labelText}
  $browse.Add_Click({
    param($sender,$eventArgs)
    $pick=$sender.Tag
    if($pick.folder) {
      $dlg=New-Object System.Windows.Forms.FolderBrowserDialog
      $dlg.Description=$pick.description
      if(Test-Path -LiteralPath $pick.field.Text -PathType Container) {
        $dlg.SelectedPath=$pick.field.Text
      }
      if($dlg.ShowDialog($script:gui) -eq 'OK') {
        $pick.field.Text=$dlg.SelectedPath
      }
      $dlg.Dispose()
    } else {
      $dlg=New-Object System.Windows.Forms.OpenFileDialog
      $dlg.Title=$pick.description
      $dlg.Filter=$pick.filter
      if($dlg.ShowDialog($script:gui) -eq 'OK') {
        $pick.field.Text=$dlg.FileName
      }
      $dlg.Dispose()
    }
  })
  $panel.Controls.Add($browse,2,$row)
  return $field
}
$script:source=Add-PathPicker 1 'v1.48素材ZIP（513MB）' $false 'ZIP (*.zip)|*.zip'
$script:artifact=Add-PathPicker 2 'Ver.1.0 EXE成果物ZIP / EXE' $false 'ZIPまたはEXE (*.zip;*.exe)|*.zip;*.exe'
$script:destination=Add-PathPicker 3 '完成ZIPの保存フォルダー' $true ''
$script:destination.Text=[Environment]::GetFolderPath('Desktop')

$script:build = New-Object System.Windows.Forms.Button
$script:build.Text = '配布用 Ver.1.0 ZIPを作成'
$script:build.Dock = 'Fill'
$script:build.Margin = [System.Windows.Forms.Padding]::new(0,3,0,4)
$script:build.Font = [System.Drawing.Font]::new('Yu Gothic UI',11.0,[System.Drawing.FontStyle]::Bold)
$panel.Controls.Add($script:build,0,4)
$panel.SetColumnSpan($script:build,3)

$script:bar=New-Object System.Windows.Forms.ProgressBar
$script:bar.Dock='Fill'
$script:bar.Style='Continuous'
$script:bar.Margin=[System.Windows.Forms.Padding]::new(0,4,0,4)
$panel.Controls.Add($script:bar,0,5)
$panel.SetColumnSpan($script:bar,3)

$script:output=New-Object System.Windows.Forms.TextBox
$script:output.Multiline=$true
$script:output.ReadOnly=$true
$script:output.ScrollBars='Vertical'
$script:output.Dock='Fill'
$script:output.Font=[System.Drawing.Font]::new('Consolas',9.0)
$panel.Controls.Add($script:output,0,6)
$panel.SetColumnSpan($script:output,3)

function Log([string]$text) {
  $script:output.AppendText($text+[Environment]::NewLine)
}
$script:job=$null
$script:jobHadError=$false
$script:jobSucceeded=$false
$script:tick=New-Object System.Windows.Forms.Timer
$script:tick.Interval=500
$script:tick.Add_Tick({
  if(!$script:job) { return }
  $receiveErrors=@()
  $messages=@(Receive-Job -Job $script:job -ErrorAction SilentlyContinue -ErrorVariable +receiveErrors)
  foreach($line in $messages) {
    if($null -ne $line) {
      $msg=[string]$line
      Log $msg
      if($msg -eq 'BUILD_SUCCESS') { $script:jobSucceeded=$true }
      if($msg.StartsWith('PACKAGING_ERROR:')) { $script:jobHadError=$true }
    }
  }
  foreach($problem in $receiveErrors) {
    $script:jobHadError=$true
    Log ('エラー詳細: '+ $problem.ToString())
  }
  if($script:job.State -in @('Running','NotStarted')) { return }
  $hasError = $script:jobHadError -or
    ($script:job.State -ne 'Completed') -or (-not $script:jobSucceeded)
  $errors = @($script:job.ChildJobs[0].Error | ForEach-Object { $_.ToString() })
  if($errors.Count -gt 0) { $hasError=$true; foreach($line in $errors){Log ('ジョブ例外: '+$line)} }
  if(-not $script:jobSucceeded) { Log '完了判定BUILD_SUCCESSがありません。作成成功として扱いません。' }
  Remove-Job -Job $script:job -Force -ErrorAction SilentlyContinue
  $script:job=$null
  $script:tick.Stop()
  $script:bar.Style='Continuous'
  $script:bar.MarqueeAnimationSpeed=0
  $script:build.Enabled=$true
  if($hasError) {
    Log '作成失敗。上のエラーを確認してください。'
    [System.Windows.Forms.MessageBox]::Show($script:gui,
       '配布ZIPの作成に失敗しました。下の詳細をご確認ください。',
       '失敗','OK','Error')|Out-Null
  } else {
    $script:bar.Value=100
    [System.Windows.Forms.MessageBox]::Show($script:gui,
       'ZIPの生成と素材検証に成功しました。公開前に実曲の変換・音質を確認してください。',
       '完了','OK','Information')|Out-Null
  }
})
$script:build.Add_Click({
  if($script:job) { return }
  $src=$script:source.Text.Trim()
  $art=$script:artifact.Text.Trim()
  $out=$script:destination.Text.Trim()
  if(!(Test-Path -LiteralPath $src -PathType Leaf) -or
     !(Test-Path -LiteralPath $art -PathType Leaf) -or
     !(Test-Path -LiteralPath $out -PathType Container)) {
    [System.Windows.Forms.MessageBox]::Show($script:gui,
       'v1.48素材ZIP、EXE成果物ZIP、保存先の3つを指定してください。',
       '入力不足','OK','Warning')|Out-Null
    return
  }
  $runner=Join-Path $PSScriptRoot 'package_v1_0_offline.ps1'
  if(!(Test-Path -LiteralPath $runner -PathType Leaf)) {
    [System.Windows.Forms.MessageBox]::Show($script:gui,
       'package_v1_0_offline.ps1がGUIと同じフォルダーに必要です。',
       'ファイル不足','OK','Error')|Out-Null
    return
  }
  $script:output.Clear()
  $script:jobHadError=$false
  $script:jobSucceeded=$false
  Log 'Ver.1.0の配布ZIPを作成中です。終了するまでこの画面を閉じないでください。'
  $script:bar.Style='Marquee'
  $script:bar.MarqueeAnimationSpeed=28
  $script:build.Enabled=$false
  try {
    $script:job=Start-Job -FilePath $runner -ArgumentList @($src,$art,$out)
    $script:tick.Start()
  } catch {
    Log ('開始できません: '+$_.Exception.Message)
    $script:build.Enabled=$true
    $script:bar.Style='Continuous'
  }
})
$script:gui.Add_FormClosing({
  param($sender,$eventArgs)
  if($script:job) {
    [System.Windows.Forms.MessageBox]::Show($script:gui,
       'ZIP作成中です。終了後に閉じてください。','処理中','OK','Warning')|Out-Null
    $eventArgs.Cancel=$true
  }
})
Log '1. v1.48の素材ZIPを指定'
Log '2. GitHub Actionsでビルド成功したEXE成果物ZIPを指定'
Log '3. 保存先を指定して「配布用 Ver.1.0 ZIPを作成」を押す'
Log 'GUIとpackage_v1_0_offline.ps1は同じフォルダーに置いてください。'
[void]$script:gui.ShowDialog()
