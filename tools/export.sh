#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$AW_ROOT"
mkdir -p artifacts/export
aw content compile
aw sandbox compile
aw adventure compile
content_key=$(python3 -c 'import hashlib; print(hashlib.sha256(b"".join(open(p, "rb").read() for p in ("content/combat.json", "content/adventure.json"))).hexdigest()[:12])')
package_output="$AW_ROOT/artifacts/package/$content_key"
if [[ "$(uname -s)" == Darwin ]]; then
    "$GODOT" --headless --path game/Ashenwake.Client --export-debug macOS "$AW_ROOT/artifacts/export/Ashenwake.zip" --log-file "$AW_ROOT/artifacts/export/export.log"
    if rg -n 'ERROR:|SCRIPT ERROR:' artifacts/export/export.log; then exit 1; fi
    unzip -oq artifacts/export/Ashenwake.zip -d artifacts/export/macos
    app="$AW_ROOT/artifacts/export/macos/Ashenwake.app"
    binary=$(find "$app/Contents/MacOS" -maxdepth 1 -type f -print -quit)
    "$binary" --headless --quit-after 3000 --log-file "$AW_ROOT/artifacts/export/package.log" -- --adventure-smoke --output="$package_output"
else
    "$GODOT" --headless --path game/Ashenwake.Client --export-debug Linux "$AW_ROOT/artifacts/export/Ashenwake.x86_64" --log-file "$AW_ROOT/artifacts/export/export.log"
    if rg -n 'ERROR:|SCRIPT ERROR:' artifacts/export/export.log; then exit 1; fi
    chmod +x artifacts/export/Ashenwake.x86_64
    artifacts/export/Ashenwake.x86_64 --headless --quit-after 3000 --log-file "$AW_ROOT/artifacts/export/package.log" -- --adventure-smoke --output="$package_output"
fi
if rg -n 'ERROR:|SCRIPT ERROR:' artifacts/export/export.log artifacts/export/package.log; then
    echo 'Export or package emitted an error.' >&2
    exit 1
fi
rg -q 'AdventureClientSmokePassed' artifacts/export/package.log
aw adventure replay "$package_output/expedition.awx"
