# DonOriginal3DMusic3 Ver.1.0

音楽を3D FLACへ変換するWindows用ソフトです。複数曲をまとめて変換できます。

## ダウンロードについて

**このGitHubの「Code → Download ZIP」はソフト本体ではありません。**

UTF-8曲名修正済みの配布用 `DonOriginal3DMusic3_v1.0_DiscNumber修正版_Windows.zip`（460,512,221バイト）はまだGitHub Releaseに登録されていません。登録されるまではGitHubからEXE入りの配布ZIPをダウンロードできません。

配布ZIPを用意できたら、[Releases](https://github.com/dun25252/DonOriginal3DMusic3_Distribute/releases) に置きます。

## 使い方

1. 配布用ZIPを解凍する
2. `DonOriginal3DMusic3_v1.0/DonOriginal3DMusic3.exe` を起動する
3. 曲を追加して保存先を指定し、一括3D変換を開始する

変換結果は `3D変換先` に `曲名_v1.0.flac`（48kHz／24bit）として保存されます。

## 注意

- Windows 10 / 11（64bit）向けです。
- `assets` と `tools` は必要な素材なので削除しないでください。
- 元の音楽ファイルは変更しません。
- 実曲での最終動作確認および第三者ソフトウェアの再配布条件の確認は未完了です。

配布ZIPのSHA-256：`457fd8be0bc444f78227a0128c7c2886095aa8a2d54f0d143076e59e670a463d`

### 日本語曲名の修正（Ver.1.0）

FFprobeのUTF-8出力をWindowsのCP932として読み取っていたため、日本語タイトルが文字化けする問題を修正しました。
EXEのFileVersion / ProductVersionはどちらもVer.1.0のままです。実曲「さよならエンドロール」のFLAC TITLEは正常なUTF-8であることを確認しています。
現在、完成ZIP本体はこのGitHubのCodeダウンロードには含まれていません。直接共有した完全版ZIPをご利用ください。

### ディスク番号の復元（Ver.1.0）

旧版に存在した、変換時に元のディスク番号を2倍にする処理を復元しました。GUIでオン／オフでき、設定も保存されます。初期状態はオンです（例：1→2、2→4）。元音源にディスク番号がない場合は変更しません。変換完了前に出力FLACのタグを検査します。

新しいWindowsビルドは https://github.com/dun25252/DonOriginal3DMusic3_Distribute/actions/runs/40661531701 で成功しました。これはEXE単体のビルド検証であり、GitHubのCode ZIPには完成アプリ・音響素材は含まれません。実際の楽曲の一括変換試験は未実施です。
