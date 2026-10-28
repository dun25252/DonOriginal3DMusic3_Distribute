# DonOriginal3DMusic3 Ver.1.0

音楽を3D FLACへ変換するWindows用ソフトです。複数曲をまとめて変換できます。

## ダウンロードについて

**このGitHubの「Code → Download ZIP」はソフト本体ではありません。**

UTF-8曲名修正済みの配布用 `DonOriginal3DMusic3_v1.0_Windows_UTF8fix.zip`（441,875,652バイト）はまだGitHub Releaseに登録されていません。登録されるまではGitHubからEXE入りの配布ZIPをダウンロードできません。

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

配布ZIPのSHA-256：`06978d1997d696626c8497695162249a94c2758338aa761b260995a939a9cc49`

### 日本語曲名の修正（Ver.1.0）

FFprobeのUTF-8出力をWindowsのCP932として読み取っていたため、日本語タイトルが文字化けする問題を修正しました。
EXEのFileVersion / ProductVersionはどちらもVer.1.0のままです。実曲「さよならエンドロール」のFLAC TITLEは正常なUTF-8であることを確認しています。
現在、完成ZIP本体はこのGitHubのCodeダウンロードには含まれていません。直接共有した完全版ZIPをご利用ください。
