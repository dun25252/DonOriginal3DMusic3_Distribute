# DonOriginal3DMusic3 Ver.1.0 配布前ゲート（開発者用）

一般配布ZIPには入れません。バージョンは **Ver.1.0固定**。

## 本体

- `distribution_app/Program.cs`：WinForms GUI、複数曲、保存先記憶、進捗、中止。
- `distribution_app/DistributionAudioEngine.cs`：48kHzのIoSR実測室内BRIRの直接音をHUTUBS H3で一部置換し、後段の室内反射を残すRoomFirst B方式。これは以前のv1.62と**音質同等と実測確認できたわけではない**。
- `distribution_app/StemSeparatorService.cs`：同梱済みHTDemucs/ONNXのオフラインStem分離。
- `distribution_app/DonOriginal3DMusic3.csproj`：Windows x64 .NET 8 WinForms、FileVersion/ProductVersion=1.0.0.0。

現在の本体は **C#のみ**。2026-10-27に使用されていないGo製outputlayout試作品は削除済み。C#で `3D変換先/曲名_v1.0.flac`、重複連番、Titleタグ（format/stream両方）、Windows禁止文字/予約名対応、デバッグTXTの一時領域隔離を直接実装。

## 安全ゲート

1. `DistributionAudioEngine.ReconstructBuses`：原音とVocals/BassのPCM尺差が0.1秒超なら停止。大量の無音パディングで声が欠落することを防止。
2. `Program.Main("--self-test")`：実際のビルドEXE自身で出力名・連番・日本語・Titleタグ・デバッグ非混入・Stem整列・Room BRIR後段保持を擬似データで検証。**実際の楽曲/実測H3の聴感評価ではない**。
3. `build_v1_0_windows.ps1`：.NET publish / 1.0.0.0検査 / EXE自身のself-test / v1.48 ZIPの原本ハッシュ検査 / 素材同梱 / ZIP検査を順に実施し、失敗時は未完成として停止。
4. `package_v1_0.py`：v1.48原本513,598,015 bytes、SHA256 `9f2de7b19f1b82bf2d4fe14b4c44f6d821c83115feeee3614c12cc90ff04c5da` で照合。assets/brir, H3 SOFA, ONNX, ffmpeg, ffprobe, stem_separator、HT-Demucsとdemucs-onnxのMITライセンス原文を同梱。開発資料・旧EXE・過去CMD・管理TXTを除外。
5. `verify_v1_0.py`：ZIP内に必要素材・ライセンスがあるか検査。Windowsで実際に曲を変換した後 `--converted-output-dir` を指定して `3D変換先` のFLACのみ・曲名_v1.0.flac・余計な日時/研究フォルダーなしを検証。
6. `test_package_v1_0.py`：実際の514MB音源でなく小型模擬ZIPを使ったパッケージ構造テスト。

## 未完了とブロッカー（2026-10-27）

- **修正C#本体のコンパイル成功を確認できていない**。Python/コンテナを用いる実行環境は `ClientError` を返す。
- ユーザーの約514MB v1.48素材ZIPは会話中に添付済みで、Files経由で作業領域へ復元できた。しかし実ZIPのCRC/SHA-256と内容検査はコンテナ実行不能で未完了。再アップロードは不要。
- GitHub Actionsの `windows-latest` と `ubuntu-latest` 両方が `runner_id=0, steps=[]` でジョブ開始前に失敗。Actions再実行でも同じ。Billing/Actions制限またはGitHub側のrunner生成エラー等が考えられるが**原因はまだ特定できていない**。
- 本物のv1.48素材同梱ZIP、Ver1.0.exe、GitHub Releaseはまだ存在しない。完成したふりで配布しない。

## 復旧後の実行（Windows開発環境、.NET 8 SDK + Python 3）

```powershell
powershell -ExecutionPolicy Bypass -File .\build_tools\build_v1_0_windows.ps1 -V148Zip "C:\path\DonOriginal3DMusic3_v1.48_StereoBRIR_GUI_Research.zip"
```

成功後も、原音の変換テスト（4曲以上、FLACとM4A、同名衝突）・変換後の再生/音質比較・ffmpeg/HTDemucs/IR素材の再配布ライセンス確認が必要。配布ZIPを検証してから公開すること。
