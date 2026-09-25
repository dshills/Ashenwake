"""Offline regressions for checksum-verified package feed creation and upgrades."""
import base64
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

TOOLS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("locked_packages", TOOLS / "fetch-locked-packages.py")
packages = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packages)


def digest(data):
    return base64.b64encode(hashlib.sha512(data).digest()).decode()


class PackageFeedTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="ashenwake-feed-")
        self.addCleanup(temporary.cleanup)
        self.feed = Path(temporary.name)
        self.expected = digest(b"official archive")

    def download(self, command, **kwargs):
        self.assertEqual(command[-3], "https://api.nuget.org/v3-flatcontainer/godotsharp/4.6.2/godotsharp.4.6.2.nupkg")
        Path(command[-1]).write_bytes(b"official archive")

    def fetch(self):
        packages.fetch_package(self.feed, "godotsharp", "4.6.2", self.expected)

    def test_valid_cached_package_requires_no_network(self):
        path = self.feed / "GodotSharp.4.6.2.nupkg"
        path.write_bytes(b"official archive")
        with patch.object(packages.subprocess, "run") as download:
            self.fetch()
            download.assert_not_called()

    def test_fresh_package_is_verified_and_published(self):
        with patch.object(packages.subprocess, "run", side_effect=self.download):
            self.fetch()
        self.assertEqual((self.feed / "godotsharp.4.6.2.nupkg").read_bytes(), b"official archive")
        self.assertFalse(list(self.feed.glob("*.part")))

    def test_platform_bundled_cache_is_replaced_without_case_duplicates(self):
        path = self.feed / "GodotSharp.4.6.2.nupkg"
        path.write_bytes(b"platform-specific archive")
        with patch.object(packages.subprocess, "run", side_effect=self.download):
            self.fetch()
        self.assertEqual(list(self.feed.iterdir()), [path])
        self.assertEqual(path.read_bytes(), b"official archive")

    def test_bad_download_never_replaces_previous_archive(self):
        path = self.feed / "GodotSharp.4.6.2.nupkg"
        path.write_bytes(b"previous archive")
        def corrupted(command, **kwargs):
            Path(command[-1]).write_bytes(b"corrupt")
        with patch.object(packages.subprocess, "run", side_effect=corrupted):
            with self.assertRaisesRegex(SystemExit, "checksum mismatch"):
                self.fetch()
        self.assertEqual(path.read_bytes(), b"previous archive")
        self.assertFalse(list(self.feed.glob("*.part")))

    def test_interrupted_download_does_not_publish_partial_archive(self):
        def interrupted(command, **kwargs):
            Path(command[-1]).write_bytes(b"partial")
            raise subprocess.CalledProcessError(22, command)
        with patch.object(packages.subprocess, "run", side_effect=interrupted):
            with self.assertRaises(subprocess.CalledProcessError):
                self.fetch()
        self.assertEqual(list(self.feed.iterdir()), [])

    def test_overlapping_fetches_do_not_share_or_remove_each_others_download(self):
        downloads = []
        def overlapping(command, **kwargs):
            temporary = Path(command[-1])
            downloads.append(temporary)
            temporary.write_bytes(b"official archive")
            if len(downloads) == 1:
                self.fetch()
        with patch.object(packages.subprocess, "run", side_effect=overlapping):
            self.fetch()
        self.assertEqual(len(set(downloads)), 2)
        self.assertEqual((self.feed / "godotsharp.4.6.2.nupkg").read_bytes(), b"official archive")
        self.assertFalse(list(self.feed.glob("*.part")))

    def test_requirements_include_project_sdk_and_all_have_pinned_hashes(self):
        required = packages.required_packages(TOOLS.parent)
        self.assertIn(("godot.net.sdk", "4.6.2"), required)
        pins = json.loads((TOOLS / "package-archives.json").read_text())
        for name, version in required:
            self.assertEqual(len(base64.b64decode(pins[f"{name}/{version}"])), 64)


if __name__ == "__main__":
    unittest.main()
