## 最新Ver.1.0 — 4Stem独立3D配置（2026-10-29）

**[Windowsビルド成功・3分割EXE更新ファイル](https://github.com/dun25252/DonOriginal3DMusic3_Distribute/actions/runs/40869032785)**

従来はHTDemucs分離後にドラム・その他の楽器をまとめ直し、音が同じ空間に密集していました。今回のEXEは**ボーカル、ドラム、ベース、その他の楽器の4Stem＋原音との差分**を保持し、以下へ別々に振り分けます。

- ボーカル：GUI指定の前方0°または後方180°
- ドラム：原曲の左・右を側方の独立した音源として処理
- ベース：後方180°
- その他の楽器：左右側方を主に、前方にも一部配置
- 分離の残差：前方へ配置し、モデルが取り出せなかった音を捨てない
- 側方：H3の直接音とIoSR実測BRIRを合成（初期反射・室内反射調整を維持）

**ダウンロード**：上のActionsページの `Artifacts` から `DonOriginal3DMusic3_v1.0_Update_Part01`〜`Part03` を取得し、すべて同じフォルダーに解凍して `restore.cmd` を実行。生成したEXEを**既存の完全版フォルダー**に上書きしてください。新規インストールに必要な `assets/`、`tools/` は含んでいません。

**結合後EXE SHA-256：** `6b30e72d62e0ed3a5cf3a1222dfdef497a730ad34634195aab50af6e36e4ce94`

Windowsビルド、自己検査、4Stemの残差検査、3分割再結合検査に成功。ただし、**実際の楽曲を用いた音声変換の最後までの検証と、ヘッドホンでの包囲感改善の聴感検証はまだ未実施**です。歌手個人の自動識別・個別配置は未実装。Ver.1.0維持。

---

## Ver.1.0 最新・音声フィルター配線修正版（2026-10-29）

[**Windowsビルド成功と3分割EXE更新ファイル（GitHub Actions）**](https://github.com/dun25252/DonOriginal3DMusic3_Distribute/actions/runs/40843731897)

- 前後ボーカル切替の実装時に残った `[irFLL]` / `[irFRL]` の未接続経路を削除（FFmpeg error -22）。
- 以降、FFmpegフィルターグラフで余った内部接続や接続数の異常を検出する自己検査を追加。
- ボーカル前後切替、後方低音、室内反射調整、ディスク番号2倍、日本語タイトル修正は維持。
- Windowsビルド・自己検査・3分割結合時のSHA-256一致：成功。
- 再結合EXE SHA-256：`db33f86575bad4e5c523520db02c9c5c2764ad4cc96766fb93f87993746f23a6`。

**ダウンロード**：GitHub Actionsページの `Artifacts` から `DonOriginal3DMusic3_v1.0_Update_Part01`・`Part02`・`Part03` の3つを取得し、すべてを同じフォルダーに解凍して `restore.cmd` を実行。出来上がった `DonOriginal3DMusic3.exe` を、既存の完全版フォルダーの同名EXEに上書きしてください。

**注意**：今回のダウンロードは実行ファイルの更新分のみ。音響素材の `assets`、`tools`、`licenses` は含みません。削除しないでください。実際の楽曲を使うFFmpegのフル変換検査および3D定位の官能検査はまだ未実施です。バージョンは1.0のままです。

---

# DonOriginal3DMusic3 Ver.1.0 — 3D定位修正EXE（最新）

**現在のEXE更新ファイル：** [Windowsビルド成功・3分割ダウンロード](https://github.com/dun25252/DonOriginal3DMusic3_Distribute/actions/runs/40841184720)

最新の実装では、低音を従来の「前72%／後28%」から「前10%／後90%」に変更し、GUIに「ボーカル定位：前方0°／後方180°」を追加しています。日本語タイトル、ディスク番号2倍、室内反射調整を維持しています。

このActionsの **Artifacts** から `DonOriginal3DMusic3_v1.0_Update_Part01`・`Part02`・`Part03` をダウンロードし、すべて同じフォルダーに解凍して `restore.cmd` を実行してください。

再結合したEXEのSHA-256：`2fda911095feb766edb03debb401495ba21f7bacbc7b1b534ab41d074734ce89`。Windowsビルドとソースコードの自己検査、3分割後の再結合検査は成功しています。

**注意：これはEXE更新用のみ**です。既存の完全版フォルダーにある `DonOriginal3DMusic3.exe` を上書きし、`assets/`、`tools/`、`licenses/` は残してください。ダウンロードした3ファイルだけで新規起動できる完全版ではありません。

音の前後定位や3D感が聴覚上改善したかは、実曲と実際のヘッドホンで未検証です。複数歌手の個人ごとの自動定位・ヘッドトラッキングは実装していません。

---

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

