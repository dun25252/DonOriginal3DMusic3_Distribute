## 最新Ver.1.0修正版EXE：3分割ダウンロード（2026-10-28）

**[GitHub ActionsからPart 1〜3をダウンロード](https://github.com/dun25252/DonOriginal3DMusic3_Distribute/actions/runs/40672326204)**

画面下部の **Artifacts** で `DonOriginal3DMusic3_v1.0_Update_Part01`、
`DonOriginal3DMusic3_v1.0_Update_Part02`、
`DonOriginal3DMusic3_v1.0_Update_Part03` をそれぞれダウンロードしてください。

各ZIPを**同じフォルダーへ解凍**し、Part 1 にある `restore.cmd` をダブルクリックします。
生成される `DonOriginal3DMusic3.exe` を既存の `DonOriginal3DMusic3_v1.0` フォルダーの同名EXEと置き換えてください。
`assets` と `tools` は削除しないでください。

- **再結合時のSHA-256**：`ce9867c66e0437ef4429f3d8313d8d77a478a5fc8f35e5134f6d69a44ec9831c`
- GitHub Windowsコンパイル・自己検査・3分割再結合照合：成功
- この3分割は**EXE更新用のみ**です。素材一式入りの完全版配布ZIPではありません。
- Actions成果物には保存期限があります。期限後はダウンロードできません。

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

## Ver.1.0 最新修正（明瞭度・ディスク番号）

2026-10-28 の修正版は以下の問題に対応しました。

- 室内反射を20〜100%で調整。初期値45%は直接音の明瞭さ重視、100%は以前と同等の反射量。
- ディスク番号を2倍にする処理を自動・手動指定で実施し、最終FLACで確認。元の1→2、2→4。
- 日本語タイトルのUTF-8読み込み修正も維持。

[最新Windows EXEのビルド成功記録とダウンロード](https://github.com/dun25252/DonOriginal3DMusic3_Distribute/actions/runs/40669819465)

GitHub Actions の **Artifacts** にある `DonOriginal3DMusic3_v1.0_Exe_ONLY_NOT_DISTRIBUTION` をダウンロードしてください。**これはEXEのみ**で、音響素材のない新規インストールには使えません。

既存の完全版を解凍した `DonOriginal3DMusic3_v1.0` フォルダー内で、同名の `DonOriginal3DMusic3.exe` を新しいEXEに上書きしてください。`assets`、`tools`、`licenses` はそのまま使用します。友人へ渡す場合は、更新したこのフォルダー全体をZIPにしてください。

※ この修正版はWindows EXEビルドと組込み自己検査に成功していますが、実際の楽曲を使った音質A/Bやディスク番号の端末別互換性検証は未完了です。第三者ソフトの再配布条件も守ってください。

