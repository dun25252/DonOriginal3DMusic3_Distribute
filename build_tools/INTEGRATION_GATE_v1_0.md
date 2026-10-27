# 2026-10-27 追加：Ver.1.0専用 WinForms 全曲エンジンを実装

**既存のGo/PowerShell v1.62/v1.64アーカイブを再パッチする方針から変更。** 現時点ではv1.62の本来のGoソースバイト列を開けないため、完全に独立してコンパイルできる.NET 8 Windows Formsソースを配布準備ブランチに新設した。これは**同じRoomFirst Bの設計に沿った再実装**であり、ユーザーが試聴承認したv1.62の音響波形と一致する保証はない。音質比較なしに公開不可。

- `build_tools/distribution_app/Program.cs`：単一画面の曲選択・フォルダー追加・削除、出力先記憶、ゲイン、進捗、中止。管理・研究・A/Bタブなし。
- `build_tools/distribution_app/DistributionAudioEngine.cs`：H3 SOFAから前後実測HRIR、IoSR前後Room BRIR直接音0～10msだけ入替、10ms以降のIRはそのまま保持。HTDemucsでボーカル・ベース・伴奏を位相整列し、IoSR側方IRと組み合わせて全曲の独立空間バスへレンダリングする。入力原曲の情報から `3D変換先/曲名_v1.0.flac` のみ出力する。アルゴリズム同一性未確認の研究再実装。
- `build_tools/distribution_app/StemSeparatorService.cs`：本家GitHub研究ブランチの既存オフラインStem分離ソースをコピーして実体化。実分離モデルは添付されたv1.48 ZIPから配布時に抽出する。
- `build_tools/distribution_app/DonOriginal3DMusic3.csproj`：Windows x64自己完結単一EXE、FileVersion/ProductVersion=1.0.0.0、AssemblyInformationalVersionも同値。Git SHAの自動追記禁止。
- `build_tools/build_v1_0_windows.ps1`：Windows .NET 8 SDK＋Python 3でEXEをビルドし、v1.48素材ZIPを厳格なSHA-256で照合、必要なassets/tools/短いREADME＋外部ライセンス表記のみを収録する。
- `.github/workflows/v1_0_windows_exe_build.yml`：Windowsコンパイル、EXEバージョン検査、EXE-only CI Artifact。**配布用ZIPとは別**。GitHub Actionsジョブは現時点でもrunner_id=0/steps=0のため実行されていない。

**未完了必須ゲート**：C#ソース実コンパイルとエラー修正、実v1.48素材（アップロード済み）ZIP SHA/CRC抽出、Windows実機の音声処理と4曲一括出力、採用済みv1.62 B試作との耳による品質比較と直接音/後期IRの検証、第三者データの再配布ライセンス確認。この条件未達では一般公開用正式Ver.1.0と称さない。過去に記したGo `outputlayout` は設計参考・単体テスト用で、新C# GUIへそのままはリンクされていない。

---

# Ver.1.0 final EXE integration / release gate (not distributed)

**The public build remains Ver.1.0; do not increment to 1.1.** This file belongs ONLY to `build/v1.0-repackage`, not the end-user ZIP or main README.

## What has been implemented as source

- `outputlayout/outputlayout.go`: Titleタグ（欠落時は元ファイル名）→ Windows安全化 → `曲名_v1.0.flac`。同名時は `曲名(2)_v1.0.flac`、上書き禁止、`3D変換先`へFLAC1本ずつ保存。
- `outputlayout/hybrid.go`: 現行v1.62 RoomFirstの生成物にある `B_同じ部屋にH3直接音*.flac` だけを選択し、`A_*.flac` と `v1.0_3D音響検査.txt` は非配布の作業領域に隔離。1本に確定できない場合は変換失敗とし、A版を代用しない。
- `outputlayout/*_test.go`: 日本語・禁止文字・Titleタグ・同名連番・元のB版の選択・A/TXT非混入のテストコード。
- `package_v1_0.py`: ユーザー提供のv1.48公式ZIPをサイズ(513598015B)・SHA-256（`9f2de7b19f1b82bf2d4fe14b4c44f6d821c83115feeee3614c12cc90ff04c5da`）で検証し、必要な H3 SOFA・IoSR BRIR・HTDemucs ONNX・FFmpeg/ffprobe・Stem Separatorだけを整理して別の完成Ver.1.0 GUI EXEとまとめる。
- `verify_v1_0.py`: 完成ZIPから余計なCMD・TXT・管理用・研究ファイルを除外し、変換先に日時別・曲別サブフォルダーがないか、全曲 `_v1.0.flac` であるか、余計な監査TXTがないか検査。
- `test_package_v1_0.py`: 小さい模擬v1.48 ZIPでの配布ルール回帰テスト。

## Exact integration point in the current Go/PowerShell GUI source

1. `DonOriginal3DMusic3_v1.64_非公開GitHub対応_一括3D変換_GUI.zip` 内のGoソースへ `outputlayout` ヘルパーを取り込む（Goモジュール `replace` か、`package main` へコピー）。
2. GUIは単一画面のまま、複数曲選択・保存先・ボーカル/伴奏設定・変換開始/キャンセルのみ。Private GitHub取得やv1.48 ZIP選択のUIは**削除**。素材の位置は `filepath.Dir(os.Executable())/assets`、`/tools` のみ。
3. 各曲の変換ごとに `work, cleanup, err := outputlayout.WorkDirectory()`、成功/失敗/キャンセルのいずれも `defer cleanup()` 相当の破棄を行う。**元のRoomFirst B DSPは変更せず**、現行の研究用レンダラが出力する `DonOriginal3DMusic3_...RoomFirst_timestamp/B_...flac` と診断TXTを全部このテンポラリフォルダー内へ書く。
4. 研究用レンダリングが正常完了してから `outputlayout.StoreHybridB(saveBase, originalInput, ffprobeExe, work)` を1回実行する。これが **元曲名で保存された唯一のFLAC**。従来の研究用フォルダーを利用者の保存先にコピーしない。
5. オリジナル音源のメタデータ・FLACフォーマット・サンプルピークの実証検査後、GUIに成功を返す。未完了・一部失敗した曲は成功扱いしない。
6. EXEの `FileVersion`・`ProductVersion` は `1.0.0.0`。配布ZIPの名前は `DonOriginal3DMusic3_v1.0_Windows.zip`、中身は `DonOriginal3DMusic3_v1.0/DonOriginal3DMusic3.exe`, `assets`, `tools`, `README.txt`, `THIRD_PARTY_NOTICES.txt` のみ。

## QA and blockers as of 2026-10-27

- **Complete v1.0 EXE has NOT yet been compiled from the updated worker**. These helper files do not patch a compiled GUI executable.
- The user's original v1.48 513MB asset ZIP was attached in this conversation, but direct runtime file access repeatedly returned `ClientError`. Therefore its full content and license files have **NOT been verified by extraction**; the remote release metadata and hash do not substitute for local ZIP CRC checks.
- GitHub Actions run 40484628089 failed **before allocating a hosted runner** (`runner_id=0`, zero steps). CI unit tests not actually executed; do not represent them as passing.
- No complete user-facing ZIP or GitHub Release is available. **Do not upload the previous research EXE as Ver.1.0** because it would still create `B_` audio filenames and diagnostic TXT.
- To finish, run Go/Python tests, compile the corrected x64 EXE, package with the user-provided v1.48 ZIP, and verify a real Windows run with 3D FLAC (including one m4a, same-title collision, and a batch of at least 4 songs). Release only after output naming and sound quality check.

User-facing public README remains intentionally short and has no developer logs. The distribution repo is currently private until the owner decides to make it public.

## 2026-10-27 追加監査：配布用の本体ソースは既に存在

以前の記録の「Ver1.0 EXEに未統合」は現行ブランチについては不正確。現在の **C# .NET 8 WinForms 本体** は以下の4ファイルとして実在する：

- `build_tools/distribution_app/Program.cs`：1画面GUI、複数曲リスト、進捗/中止、出力先・音量設定の保存。
- `build_tools/distribution_app/DistributionAudioEngine.cs`：H3直接音でIoSR F/Bの実測ルームIRを一部差し替えて長い後続反射を残すレンダラ。ユーザー採用のRoomFirst B設計を元にし、A/B研究版の2本出力は行わず、1曲につき `3D変換先/曲名_v1.0.flac` だけを保存し、全中間PCM・ログは一時作業領域で処分。
- `build_tools/distribution_app/StemSeparatorService.cs`：HTDemucs offline分離モデルの実行。
- `build_tools/distribution_app/DonOriginal3DMusic3.csproj`：1.0.0.0のWindows x64単独GUI実行ファイルのビルド設定。

**今回追加した安全修正**：
1. `DistributionAudioEngine.ReconstructBuses` はHTDemucs Vocals/Bass と原音48kHz PCMの尺差が0.1秒を超える場合は変換を拒否する。従前の最大10%無音埋めによる声消失を防止。
2. `Sanitize` でWindows予約名COM1–COM9/LPT1–LPT9等を扱う。
3. `DistributionAudioEngine.SelfTestAsync` は実装内テストで、出力ルート/命名/同名曲連番/日本語曲名/Windows予約名/デバッグ非混入/Stem相補再構成/大きなStem尺差拒否/室内IR後段サンプル不変を確認する（本物のH3 SOFA/FFmpegによる音質テストとは異なる）。
4. `Program.Main("--self-test")` により、コンソールのないGUI EXEでもテストを非表示・終了コードで実行できる。
5. `build_tools/build_v1_0_windows.ps1` では、EXEの内部Version確認後にこの自己テストを **必ず実行**。失敗したらv1.48素材のパッケージ化に進まない。

**2026-10-27 実行状況**：コンテナ実行基盤が `ClientError` を返す状態。ユーザー添付v1.48素材ZIP 513,598,015バイトをFiles経由で復元できたが、CRC/実素材検証は未完了。GitHub Actions workflowも runner_id=0、steps=[]、実テスト不実行。**GUI本体はソース統合済みだが.NETコンパイル済みではなく、v1.0 ZIPは未生成。**

ビルド可能なWindows実行環境を復旧した後の唯一の次ゲート：
`powershell -ExecutionPolicy Bypass -File ./build_tools/build_v1_0_windows.ps1 -V148Zip "<attached v1.48 ZIP absolute path>"`
を実行してEXE実テスト/513MBソースSHA/ZIP CRCに成功すること。その後少なくとも4曲の実再生検査を行う。**バージョンは1.0固定**。
