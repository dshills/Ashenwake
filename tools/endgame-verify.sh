#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$AW_ROOT"
aw endgame validate
aw endgame compile
aw endgame builds
aw endgame demo
aw endgame benchmark
aw endgame exhaustive
endgame_key=$(python3 -c 'import hashlib; print(hashlib.sha256(b"".join(open(p, "rb").read() for p in ("content/combat.json", "content/adventure.json", "content/progression.json", "content/campaign.json", "content/campaign-combat.json", "content/endgame.json", "content/endgame-combat.json"))).hexdigest()[:12])')
endgame_output="$AW_ROOT/artifacts/endgame-client/$endgame_key"
mkdir -p "$endgame_output"
"$GODOT" --headless --path game/Ashenwake.Client --editor --import --log-file "$endgame_output/import.log"
python3 tools/check-godot-log.py "$endgame_output/import.log"
"$GODOT" --headless --path game/Ashenwake.Client res://Endgame.tscn --quit-after 180000 --log-file "$endgame_output/smoke.log" -- --endgame-smoke --output="$endgame_output"
python3 tools/check-godot-log.py "$endgame_output/smoke.log"
rg -q 'EndgameClientSmokePassed' "$endgame_output/smoke.log"
aw endgame replay "$endgame_output/endgame.awendgame"
