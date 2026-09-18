#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$AW_ROOT"
bash tools/release-audit.sh
release_soak="$(mktemp -d "$AW_ROOT/artifacts/release/soak.XXXXXX")"
# The soak creates its own fresh run directory and never overwrites an earlier archive.
aw release endgame-soak "$release_soak/persistent" 1
release_ui="$release_soak/client"
mkdir -p "$release_ui"
"$GODOT" --headless --path game/Ashenwake.Client res://ReleaseSmoke.tscn --quit-after 600 --log-file "$release_ui/smoke.log" -- --release-smoke --output="$release_ui"
python3 tools/check-godot-log.py "$release_ui/smoke.log"
rg -q 'ReleaseClientSmokePassed' "$release_ui/smoke.log"
