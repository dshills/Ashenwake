#!/usr/bin/env python3
"""Populate a platform-independent offline feed from pinned NuGet archives."""
import base64
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET


def required_packages(root):
    packages = set()
    for lock in (root / "game").glob("*/packages.lock.json"):
        for framework in json.loads(lock.read_text())["dependencies"].values():
            for name, dependency in framework.items():
                if dependency["type"] != "Project":
                    packages.add((name.lower(), dependency["resolved"]))
    for pack in json.loads((root / "tools/runtime-packs.json").read_text()):
        packages.add((pack["id"].lower(), pack["version"]))
    # Project SDK packages are resolved before restore and are absent from lockfiles.
    for project in (root / "game").glob("*/*.csproj"):
        sdk = ET.parse(project).getroot().get("Sdk", "")
        if "/" in sdk:
            name, version = sdk.rsplit("/", 1)
            packages.add((name.lower(), version))
    return packages


def archive_hash(path):
    checksum = hashlib.sha512()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            checksum.update(block)
    return base64.b64encode(checksum.digest()).decode()


def fetch_package(feed, name, version, expected):
    filename = f"{name}.{version}.nupkg"
    # Upgrade old feeds whose editor-bundled Godot archives used title-case names.
    matches = list(feed.glob("*.nupkg"))
    matches = [p for p in matches if p.name.lower() == filename]
    if len(matches) > 1:
        raise SystemExit(f"Duplicate package archives: {filename}")
    path = matches[0] if matches else feed / filename
    if path.exists() and archive_hash(path) == expected:
        return
    with tempfile.NamedTemporaryFile(dir=feed, prefix=filename + ".", suffix=".part", delete=False) as download:
        temporary = Path(download.name)
    try:
        subprocess.run([
            "curl", "--fail", "--silent", "--show-error", "--location", "--retry", "3", "--max-time", "120",
            f"https://api.nuget.org/v3-flatcontainer/{name}/{version}/{filename}", "--output", str(temporary),
        ], check=True)
        if archive_hash(temporary) != expected:
            raise SystemExit(f"Package checksum mismatch: {path}")
        # Never publish unverified bytes, including when refreshing an old cache.
        temporary.replace(path)
    finally:
        temporary.unlink(missing_ok=True)


def main():
    root = Path(__file__).resolve().parent.parent
    feed = root / ".tools/feed"
    feed.mkdir(parents=True, exist_ok=True)
    archive_hashes = json.loads((root / "tools/package-archives.json").read_text())
    packages = required_packages(root)
    for name, version in sorted(packages):
        # Archive signatures have separate hashes; locked restore also checks the
        # normalized NuGet content hashes recorded in packages.lock.json.
        fetch_package(feed, name, version, archive_hashes[f"{name}/{version}"])
    print(f"Verified {len(packages)} locked packages in {feed}")


if __name__ == "__main__":
    main()
