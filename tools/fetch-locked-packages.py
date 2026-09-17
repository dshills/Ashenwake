#!/usr/bin/env python3
"""Populate an offline feed from committed lockfiles and verified runtime pack hashes."""
import base64
import hashlib
import json
from pathlib import Path
import subprocess

root = Path(__file__).resolve().parent.parent
feed = root / ".tools" / "feed"
feed.mkdir(parents=True, exist_ok=True)
packages = set()
archive_hashes = json.loads((root / "tools/package-archives.json").read_text())
for lock in (root / "game").glob("*/packages.lock.json"):
    for framework in json.loads(lock.read_text())["dependencies"].values():
        for name, dependency in framework.items():
            if dependency["type"] != "Project":
                packages.add((name.lower(), dependency["resolved"]))
for pack in json.loads((root / "tools/runtime-packs.json").read_text()):
    packages.add((pack["id"], pack["version"]))
for name, version in sorted(packages):
    # NuGet lockfile content hashes normalize repository signatures; archive bytes
    # have separate pinned hashes. Locked restore verifies the content hashes too.
    expected = archive_hashes[f"{name}/{version}"]
    path = feed / f"{name}.{version}.nupkg"
    # Godot distributes title-cased package filenames inside its verified editor archive.
    existing = next((p for p in feed.glob("*.nupkg") if p.name.lower() == path.name), None)
    if existing is not None:
        path = existing
    if not path.exists():
        temporary = path.with_suffix(".part")
        subprocess.run([
            "curl", "--fail", "--silent", "--show-error", "--location", "--retry", "3", "--max-time", "120",
            f"https://api.nuget.org/v3-flatcontainer/{name}/{version}/{path.name}", "--output", str(temporary),
        ], check=True)
        temporary.replace(path)
    digest = base64.b64encode(hashlib.sha512(path.read_bytes()).digest()).decode()
    if digest != expected:
        raise SystemExit(f"Package checksum mismatch: {path}")
print(f"Verified {len(packages)} locked packages in {feed}")
