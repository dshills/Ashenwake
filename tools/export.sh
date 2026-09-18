#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$AW_ROOT"
mkdir -p artifacts/export
aw content compile
aw sandbox compile
aw adventure compile
aw production compile
aw campaign compile
aw endgame compile
aw experiment compile
content_key=$(python3 -c 'import hashlib; print(hashlib.sha256(b"".join(open(p, "rb").read() for p in ("content/combat.json", "content/adventure.json", "content/progression.json", "content/text.en.json", "content/campaign.json", "content/campaign-combat.json", "content/endgame.json", "content/endgame-combat.json", "content/experiments.json"))).hexdigest()[:12])')
mkdir -p "$AW_ROOT/artifacts/package"
package_output="$(mktemp -d "$AW_ROOT/artifacts/package/$content_key.XXXXXX")"
if [[ "$(uname -s)" == Darwin ]]; then
    "$GODOT" --headless --path game/Ashenwake.Client --export-debug macOS "$AW_ROOT/artifacts/export/Ashenwake.zip" --log-file "$AW_ROOT/artifacts/export/export.log"
    python3 tools/check-godot-log.py --export artifacts/export/export.log
    unzip -oq artifacts/export/Ashenwake.zip -d artifacts/export/macos
    app="$AW_ROOT/artifacts/export/macos/Ashenwake.app"
    binary=$(find "$app/Contents/MacOS" -maxdepth 1 -type f -print -quit)
    "$binary" --headless --quit-after 180000 --log-file "$AW_ROOT/artifacts/export/package.log" -- --endgame-smoke --output="$package_output"
else
    "$GODOT" --headless --path game/Ashenwake.Client --export-debug Linux "$AW_ROOT/artifacts/export/Ashenwake.x86_64" --log-file "$AW_ROOT/artifacts/export/export.log"
    python3 tools/check-godot-log.py --export artifacts/export/export.log
    chmod +x artifacts/export/Ashenwake.x86_64
    binary="$AW_ROOT/artifacts/export/Ashenwake.x86_64"
    "$binary" --headless --quit-after 180000 --log-file "$AW_ROOT/artifacts/export/package.log" -- --endgame-smoke --output="$package_output"
fi
python3 tools/check-godot-log.py artifacts/export/package.log
rg -q 'EndgameClientSmokePassed' artifacts/export/package.log
aw endgame replay "$package_output/endgame.awendgame"

release_output="$package_output/release-client"
mkdir -p "$release_output"
"$binary" --headless --quit-after 600 --log-file "$release_output/smoke.log" -- --release-smoke --output="$release_output"
python3 tools/check-godot-log.py "$release_output/smoke.log"
rg -q 'ReleaseClientSmokePassed' "$release_output/smoke.log"

experiment_output="$package_output/experiment-client"
mkdir -p "$experiment_output"
"$binary" --headless --quit-after 12000 --log-file "$experiment_output/smoke.log" -- --echoes-smoke --output="$experiment_output"
python3 tools/check-godot-log.py "$experiment_output/smoke.log"
rg -q 'ExperimentClientSmokePassed' "$experiment_output/smoke.log"
aw experiment replay "$experiment_output/echoes.awexperiment"
