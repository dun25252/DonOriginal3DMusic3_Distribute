#!/usr/bin/env python3
"""Test distribution archive policy using tiny synthetic fixtures (not audio QA)."""
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import package_v1_0 as pack
import verify_v1_0 as verify


def necessary():
    return set(pack.REQUIRED) | {"assets/models/demucs_onnx_cache/demo.onnx",
        "licenses/HT-Demucs_LICENSE.txt", "licenses/demucs-onnx_LICENSE.txt"}


class PackagingTests(unittest.TestCase):
    def test_runtime_zip_filters_and_no_dev_tools(self):
        with tempfile.TemporaryDirectory() as td:
            folder = Path(td)
            raw = folder / "fixture.zip"
            with zipfile.ZipFile(raw, "w") as archive:
                for path in necessary():
                    archive.writestr("research-v1.48/" + path, b"X" * 100)
                for debug in [
                    "research-v1.48/DonOriginal3DMusic3.exe",
                    "research-v1.48/DonOriginal3DMusic3_管理用.txt",
                    "research-v1.48/01_BUILD_AND_RUN_v1.48.cmd",
                    "research-v1.48/assets/brir/StageC_debug.txt",
                    "research-v1.48/tools/ffmpeg/bin/trace.log",
                    "research-v1.48/README.md",
                ]:
                    archive.writestr(debug, b"development-only")
            exe = folder / "Ver1.0.exe"
            exe.write_bytes(b"MZ" + b"\0" * 100_200)
            out = folder / "output"
            with patch.object(pack, "check_v148_archive", return_value=None), \
                 patch.object(pack, "verify_exe_version", return_value=None):
                pack.pack(raw, exe, out)
            archive = out / pack.OUTFILE
            verify.check_zip(archive)
            with zipfile.ZipFile(archive) as z:
                names = "\n".join(z.namelist())
                self.assertNotIn("StageC_debug.txt", names)
                self.assertNotIn("管理用", names)
                self.assertNotIn("01_BUILD", names)
                self.assertIn("assets/hrtf/hutubs_pp3.sofa", names)
                self.assertIn("README.txt", names)

    def test_reject_missing_model(self):
        with tempfile.TemporaryDirectory() as td:
            zpath = Path(td) / "fixture.zip"
            with zipfile.ZipFile(zpath, "w") as w:
                for path in pack.REQUIRED:
                    w.writestr(path, b"X" * 100)
            with zipfile.ZipFile(zpath) as z:
                with self.assertRaisesRegex(ValueError, "ONNX"):
                    pack.gather(z)

    def test_reject_duplicate_path_case_insensitive(self):
        with tempfile.TemporaryDirectory() as td:
            zpath = Path(td) / "fixture.zip"
            with zipfile.ZipFile(zpath, "w") as w:
                for path in necessary():
                    w.writestr(path, b"X" * 100)
                w.writestr("tools/FFMPEG/bin/ffmpeg.exe", b"X")
            with zipfile.ZipFile(zpath) as z:
                with self.assertRaisesRegex(ValueError, "Duplicated"):
                    pack.gather(z)

    def test_post_conversion_names_and_no_debug(self):
        with tempfile.TemporaryDirectory() as td:
            folder = Path(td) / "3D変換先"
            folder.mkdir()
            (folder / "Cats are dangerous_v1.0.flac").write_bytes(b"flac data")
            verify.check_conversion(Path(td), expected_count=1)
            (folder / "v1.0_3D音響検査.txt").write_text("debug")
            with self.assertRaisesRegex(ValueError, "Extra file"):
                verify.check_conversion(Path(td), expected_count=1)

    def test_reject_research_subfolder(self):
        with tempfile.TemporaryDirectory() as td:
            folder = Path(td) / "3D変換先"
            folder.mkdir()
            (folder / "Cats are dangerous_v1.0.flac").write_bytes(b"flac data")
            (folder / "DonOriginal3DMusic3_v1.62_debug").mkdir()
            with self.assertRaisesRegex(ValueError, "subfolder"):
                verify.check_conversion(Path(td), expected_count=1)


if __name__ == "__main__":
    unittest.main()
