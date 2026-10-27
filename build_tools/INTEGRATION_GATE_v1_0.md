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
