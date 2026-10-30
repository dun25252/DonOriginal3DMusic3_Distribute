#!/usr/bin/env python3
"""Ver.1.0 packaging and post-conversion checks. NOT a sound-quality test."""
import argparse
from pathlib import Path, PurePosixPath
import re
import sys
import zipfile

NAME = "DonOriginal3DMusic3_v1.0/"
ROOT_ALLOW = {"DonOriginal3DMusic3.exe", "README.txt", "THIRD_PARTY_NOTICES.txt"}
NEEDED = {
    "tools/ffmpeg/bin/ffmpeg.exe",
    "tools/ffmpeg/bin/ffprobe.exe",
    "tools/stem_separator/stem_separator.exe",
    "assets/hrtf/hutubs_pp3.sofa",
    "licenses/ht-demucs_license.txt",
    "licenses/demucs-onnx_license.txt",
}
for d in ["F", "B", "UF", "UB", "DF"]:
    for e in ["L", "R"]:
        NEEDED.add(f"assets/brir/iosr_{d}_room_{e}.wav")
for side in ("sideL", "sideR"):
    for ear in ("L", "R"):
        NEEDED.add(f"assets/brir/iosr_F_{side}_room_{ear}.wav")


def check_zip(p: Path) -> None:
    with zipfile.ZipFile(p) as z:
        corrupt = z.testzip()
        if corrupt:
            raise ValueError("ZIP CRC invalid: " + corrupt)
        names = z.namelist()
        expected = {NAME + x for x in ROOT_ALLOW}
        missing = expected - set(names)
        if missing:
            raise ValueError("Required root user files missing: " + str(missing))
        rels = set()
        for name in names:
            if not name.startswith(NAME):
                raise ValueError("Unexpected outer folder: " + name)
            if name.endswith("/"):
                continue
            rel = name[len(NAME):]
            rels.add(rel.lower())
            if "/" not in rel and rel not in ROOT_ALLOW:
                raise ValueError("Extra root-file forbidden: " + rel)
            low = rel.lower()
            if low.endswith((".cmd", ".bat", ".ps1", ".log", ".pdb", ".cs", ".csproj")):
                raise ValueError("Debug/development file forbidden: " + rel)
            if "管理用" in rel or re.search(r"(debug|positiontrace|_audit)", rel, re.I):
                raise ValueError("Management/debug file forbidden: " + rel)
        for asset in NEEDED:
            if asset.lower() not in rels:
                raise ValueError("Missing runtime: " + asset)
        if not any(x.startswith("assets/models/demucs_onnx_cache/") and x.endswith(".onnx")
                   for x in rels):
            raise ValueError("Missing offline Demucs ONNX model")
    print("ZIP CONTENTS OK", p)


def check_conversion(root: Path, expected_count: int | None = None) -> None:
    folder = root if root.name == "3D変換先" else root / "3D変換先"
    if not folder.is_dir():
        raise ValueError("Conversion folder must be named 3D変換先: " + str(folder))
    bad = []
    outputs = list(folder.iterdir())
    for path in outputs:
        if path.is_dir():
            bad.append("Song/date/research subfolder is forbidden: " + str(path))
        elif path.suffix.lower() != ".flac":
            bad.append("Extra file in final output folder: " + str(path))
    flacs = [x for x in outputs if x.is_file() and x.suffix.lower() == ".flac"]
    if not flacs:
        raise ValueError("No FLAC output was generated")
    if expected_count is not None and len(flacs) != expected_count:
        bad.append(f"Expected {expected_count} FLAC files, found {len(flacs)}")
    for p in flacs:
        if not p.name.endswith("_v1.0_Original3D.flac"):
            bad.append("Invalid FLAC filename: " + str(p))
        if p.name.startswith(("B_", "A_", "00_", "Stage")):
            bad.append("Research prefix leaked: " + str(p))
        if not p.is_relative_to(folder):
            bad.append("Output outside 3D変換先: " + str(p))
    if bad:
        raise ValueError("Invalid user-visible conversion results:\n"+"\n".join(bad))
    print("OUTPUT NAME AND DEBUG TXT CHECK OK:", len(flacs), "files")


if __name__ == "__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("--zip", type=Path, required=True)
    parser.add_argument("--converted-output-dir", type=Path)
    parser.add_argument("--expect-count", type=int, help="Number of songs converted in a clean QA output directory")
    args=parser.parse_args()
    try:
        check_zip(args.zip)
        if args.converted_output_dir:
            check_conversion(args.converted_output_dir, args.expect_count)
        else:
            print("NOTICE: Real Windows conversion naming/debug TXT test was not run.")
    except (OSError, ValueError, zipfile.BadZipFile) as exc:
        sys.exit("VER.1.0 VALIDATION FAILED: "+str(exc))
