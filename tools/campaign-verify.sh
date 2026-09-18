#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$AW_ROOT"
aw campaign validate
aw campaign compile
aw campaign demo
aw campaign benchmark
campaign_key=$(python3 -c 'import hashlib; print(hashlib.sha256(b"".join(open(p, "rb").read() for p in ("content/combat.json", "content/adventure.json", "content/progression.json", "content/campaign.json", "content/campaign-combat.json"))).hexdigest()[:12])')
campaign_output="$AW_ROOT/artifacts/campaign-client/$campaign_key"
mkdir -p "$campaign_output"
"$GODOT" --headless --path game/Ashenwake.Client --editor --import --log-file "$campaign_output/import.log"
python3 tools/check-godot-log.py "$campaign_output/import.log"
"$GODOT" --headless --path game/Ashenwake.Client res://Campaign.tscn --quit-after 45000 --log-file "$campaign_output/smoke.log" -- --campaign-smoke --output="$campaign_output"
python3 tools/check-godot-log.py "$campaign_output/smoke.log"
rg -q 'CampaignClientSmokePassed' "$campaign_output/smoke.log"
aw campaign replay "$campaign_output/campaign.awcampaign"
