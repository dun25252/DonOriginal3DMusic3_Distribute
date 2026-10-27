#!/usr/bin/env python3
"""DonOriginal3DMusic3 Ver.1.0 public ZIP builder.

Only use a corrected Ver.1.0 GUI EXE (not Ver.1.63/1.64/experimental GUI).
The signed-off build must produce "3D変換先/<title>_v1.0.flac",
and must never leave user-visible diagnostic TXT.
The builder cannot patch an already compiled EXE; those behaviors must
be verified in the GUI source and real Windows runtime BEFORE publishing.
"""
from __future__ import annotations
import argparse
import hashlib
import os
from pathlib import Path, PurePosixPath
import shutil
import subprocess
import sys
import tempfile
import zipfile

V148_BYTES = 513_598_015
V148_SHA256 = "9f2de7b19f1b82bf2d4fe14b4c44f6d821c83115feeee3614c12cc90ff04c5da"
PACKAGE_ROOT = "DonOriginal3DMusic3_v1.0"
OUTFILE = "DonOriginal3DMusic3_v1.0_Windows.zip"

REQUIRED = {
    "assets/hrtf/hutubs_pp3.sofa",
    "tools/ffmpeg/bin/ffmpeg.exe",
    "tools/ffmpeg/bin/ffprobe.exe",
    "tools/stem_separator/stem_separator.exe",
}
for direction in ("F", "B"):
    for ear in ("L", "R"):
        REQUIRED.add(f"assets/brir/iosr_{direction}_room_{ear}.wav")
for side in ("sideL", "sideR"):
    for ear in ("L", "R"):
        REQUIRED.add(f"assets/brir/iosr_F_{side}_room_{ear}.wav")

README = """DonOriginal3DMusic3 Ver.1.0

複数の曲を3D FLACに変換するWindows用ソフトです。

使い方
1. DonOriginal3DMusic3.exe を起動
2. 曲を追加して保存先を選択
3. 「選択した曲をまとめて3D変換」を押す

出力先: 保存先の「3D変換先」フォルダー
ファイル名: 曲名_v1.0.flac （48kHz / 24bit）

・Windows 10/11 64bit向け
・assetsとtoolsフォルダーは削除しないでください
・元の音楽ファイルは変更しません
・環境によって音の聞こえ方は異なります
・権利上問題のない音源をご利用ください
"""

NOTICES = """Third-party materials bundled with DonOriginal3DMusic3 Ver.1.0

HUTUBS HRIR/HRTF (pp3), Brinkmann et al., 2019
  https://doi.org/10.14279/depositonce-8487
  CC BY 4.0
IoSR Listening Room BRIR, University of Surrey / Jon Francombe, 2017
  https://doi.org/10.15126/surreydata.00813511
  CC BY 4.0
FFmpeg: https://ffmpeg.org/legal.html (build-dependent license terms)
Demucs/HTDemucs: license terms apply to the particular distributed model.
Other binary components: follow their bundled upstream licenses.

The original measured responses are not re-measured by this application.
Please keep applicable upstream LICENSE/COPYING files in tools when supplied.
Before public release, review redistribution conditions of all packaged
binaries, model weights and source measurement datasets.
"""

# Strip ancestors such as "DonOriginal3DMusic3_v1.48/" from each member.
ANCHORS = ("assets", "tools")
EXCLUDED_NAMES = {
    "thumbs.db", "desktop.ini", "donoriginal3dmusic3.exe",
    "donoriginal3dmusic3_管理用.txt", "readme.md",
}
EXCLUDED_SUFFIXES = (".cmd", ".bat", ".ps1", ".log", ".pdb",
                     ".bak", ".cs", ".csproj", ".pyc", ".py")
DEBUG_NAMES = ("debug", "positiontrace", "stagec_", "staged_",
               "stagee_", "音響検査", "測定と出典")


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(8 * 1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def verify_exe_version(exe: Path) -> None:
    """On Windows do not package an older prototype under the v1.0 name.

    This is an EXE metadata check, not proof that output naming or audio QA passes.
    """
    if os.name != "nt":
        return  # Linux CI uses synthetic binary fixtures; real release build is Windows.
    env = dict(os.environ)
    env["DON_EXE_PATH"] = str(exe.resolve())
    proc = subprocess.run(
        ["powershell.exe", "-NoProfile", "-NonInteractive", "-Command",
         '$v=[Diagnostics.FileVersionInfo]::GetVersionInfo($env:DON_EXE_PATH);'
         'Write-Output ($v.FileVersion + "|" + $v.ProductVersion)'],
        capture_output=True, text=True, encoding="utf-8", errors="replace", env=env
    )
    if proc.returncode != 0:
        raise ValueError("Windows EXEのバージョン情報を確認できません: " + proc.stderr.strip())
    versions = proc.stdout.strip().split("|")
    if len(versions) != 2 or versions[0] != "1.0.0.0" or versions[1] != "1.0.0.0":
        raise ValueError(
            "配布EXEはFileVersionとProductVersionの両方が1.0.0.0である必要があります: "
            + proc.stdout.strip()
        )


def check_v148_archive(path: Path) -> None:
    if path.stat().st_size != V148_BYTES:
        raise ValueError(f"v1.48 ZIP size mismatch: {path.stat().st_size} bytes, expected {V148_BYTES}")
    actual = sha256_file(path)
    if actual.lower() != V148_SHA256:
        raise ValueError("v1.48 ZIP SHA-256 mismatch: refusing to package unrelated/unverified assets")


def relative_resource_path(member_name: str) -> str | None:
    member_name = member_name.replace("\\", "/")
    raw = member_name.split("/")
    if (not member_name or member_name.startswith("/")
            or any(p in ("", ".", "..") for p in raw)):
        return None
    # Detect roots only on complete directory components.
    k = next((i for i, v in enumerate(raw) if v.lower() in ANCHORS), None)
    if k is None:
        return None
    dst = "/".join(raw[k:])
    low = dst.lower()
    if low.startswith("assets/brir/"):
        return dst if low.endswith(".wav") else None
    if low == "assets/hrtf/hutubs_pp3.sofa":
        return "assets/hrtf/hutubs_pp3.sofa"
    if low.startswith("assets/models/demucs_onnx_cache/"):
        return dst
    if low.startswith("tools/ffmpeg/") or low.startswith("tools/stem_separator/"):
        name = PurePosixPath(dst).name.lower()
        if name in EXCLUDED_NAMES or name.endswith(EXCLUDED_SUFFIXES):
            # Preserve upstream license notices despite stripping debug TXT.
            if name not in ("license", "license.txt", "copying", "copying.txt",
                            "license.md", "copyright"):
                return None
        if any(word in name for word in DEBUG_NAMES):
            return None
        return dst
    return None


def gather(archive: zipfile.ZipFile) -> dict[str, zipfile.ZipInfo]:
    found: dict[str, zipfile.ZipInfo] = {}
    canonical: set[str] = set()
    for entry in archive.infolist():
        if entry.is_dir():
            continue
        dest = relative_resource_path(entry.filename)
        if dest is None:
            continue
        key = dest.lower()
        if key in canonical:
            raise ValueError("Duplicated asset path (case-insensitive): " + dest)
        if entry.file_size <= 0:
            raise ValueError("Unexpected empty asset: " + dest)
        canonical.add(key)
        found[dest] = entry
    missing = {p for p in REQUIRED if p.lower() not in canonical}
    if missing:
        raise ValueError("v1.48 ZIP is missing required runtime assets:\n" + "\n".join(sorted(missing)))
    model_files = [n for n in found if n.lower().startswith(
        "assets/models/demucs_onnx_cache/") and n.lower().endswith(".onnx")]
    if not model_files:
        raise ValueError("No offline HTDemucs ONNX model weights found")
    return found


def pack(v148: Path, exe: Path, target: Path) -> None:
    if not v148.is_file() or not exe.is_file():
        raise FileNotFoundError("v1.48素材ZIPと修正済みVer.1.0.exeの両方が必要です")
    if exe.suffix.lower() != ".exe" or exe.stat().st_size < 100_000:
        raise ValueError("Provided GUI EXE is missing or unusually small")
    verify_exe_version(exe)
    check_v148_archive(v148)
    target.mkdir(parents=True, exist_ok=True)
    final = target / OUTFILE
    work_fd, tmp_str = tempfile.mkstemp(prefix="._v1.0_build_", suffix=".zip", dir=target)
    os.close(work_fd)
    tmp = Path(tmp_str)
    try:
        with zipfile.ZipFile(v148, "r") as source:
            source_entries = gather(source)
            with zipfile.ZipFile(tmp, "w", compression=zipfile.ZIP_DEFLATED,
                                 compresslevel=3, allowZip64=True) as dst:
                def entry(name: str) -> str:
                    return PACKAGE_ROOT + "/" + name
                dst.write(exe, entry("DonOriginal3DMusic3.exe"))
                dst.writestr(entry("README.txt"), README.encode("utf-8-sig"))
                dst.writestr(entry("THIRD_PARTY_NOTICES.txt"), NOTICES.encode("utf-8"))
                for rel, info in sorted(source_entries.items()):
                    # Stream copies: archive can exceed 500MB.
                    with source.open(info, "r") as source_file, dst.open(entry(rel), "w",
                                                                        force_zip64=True) as output_file:
                        shutil.copyfileobj(source_file, output_file, length=8 * 1024 * 1024)
        with zipfile.ZipFile(tmp, "r") as z:
            corrupt = z.testzip()
            if corrupt is not None:
                raise ValueError("ZIP CRC error: " + corrupt)
            names = z.namelist()
            # Only end-user app, assets, tools, brief help and licenses.
            root = PACKAGE_ROOT + "/"
            allowed = ("assets/", "tools/")
            for file in names:
                if not file.startswith(root):
                    raise ValueError("Unexpected ZIP root: " + file)
                rel = file[len(root):]
                if rel in ("DonOriginal3DMusic3.exe", "README.txt", "THIRD_PARTY_NOTICES.txt"):
                    continue
                if not rel.startswith(allowed):
                    raise ValueError("Unexpected development artifact: " + rel)
                name = PurePosixPath(rel).name.lower()
                if name.endswith((".cmd", ".bat", ".ps1", ".log", ".pdb")):
                    raise ValueError("Unexpected debug/build artifact: " + rel)
        os.replace(tmp, final)
        print("PACKAGE:", final)
        print("SIZE:", final.stat().st_size)
        print("SHA256:", sha256_file(final))
        print("RUNTIME FILES:", len(source_entries))
        print("WARNING: Windows full-song conversion and filename normalization still need real EXE testing.")
    finally:
        if tmp.exists():
            tmp.unlink()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--v148", type=Path, required=True, help="Original 513598015-byte v1.48 ZIP")
    parser.add_argument("--exe", type=Path, required=True,
                        help="CORRECTED v1.0 GUI EXE, independently tested for output naming/no debug TXT")
    parser.add_argument("--out", type=Path, default=Path("."))
    args = parser.parse_args()
    try:
        pack(args.v148, args.exe, args.out)
    except (OSError, ValueError, zipfile.BadZipFile) as error:
        sys.exit("PACKAGING FAILED: " + str(error))
