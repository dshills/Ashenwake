#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$AW_ROOT"
mkdir -p artifacts/export
aw content compile
aw sandbox compile
if [[ "$(uname -s)" == Darwin ]]; then
    "$GODOT" --headless --path game/Ashenwake.Client --export-debug macOS "$AW_ROOT/artifacts/export/Ashenwake.zip" --log-file "$AW_ROOT/artifacts/export/export.log"
    if rg -n 'ERROR:|SCRIPT ERROR:' artifacts/export/export.log; then exit 1; fi
    unzip -oq artifacts/export/Ashenwake.zip -d artifacts/export/macos
    app="$AW_ROOT/artifacts/export/macos/Ashenwake.app"
    binary=$(find "$app/Contents/MacOS" -maxdepth 1 -type f -print -quit)
    "$binary" --headless --quit-after 600 --log-file "$AW_ROOT/artifacts/export/package.log" -- --sandbox-smoke --output="$AW_ROOT/artifacts/package"
else
    "$GODOT" --headless --path game/Ashenwake.Client --export-debug Linux "$AW_ROOT/artifacts/export/Ashenwake.x86_64" --log-file "$AW_ROOT/artifacts/export/export.log"
    if rg -n 'ERROR:|SCRIPT ERROR:' artifacts/export/export.log; then exit 1; fi
    chmod +x artifacts/export/Ashenwake.x86_64
    artifacts/export/Ashenwake.x86_64 --headless --quit-after 600 --log-file "$AW_ROOT/artifacts/export/package.log" -- --sandbox-smoke --output="$AW_ROOT/artifacts/package"
fi
if rg -n 'ERROR:|SCRIPT ERROR:' artifacts/export/export.log artifacts/export/package.log; then
    echo 'Export or package emitted an error.' >&2
    exit 1
fi
rg -q 'SandboxSmokePassed' artifacts/export/package.log
aw sandbox replay artifacts/package/session.awc
