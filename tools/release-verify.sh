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
interaction_ui="$release_soak/interaction-client"
mkdir -p "$interaction_ui"
"$GODOT" --headless --path game/Ashenwake.Client res://InteractionSmoke.tscn --quit-after 600 --log-file "$interaction_ui/smoke.log" -- --interaction-smoke --output="$interaction_ui"
python3 tools/check-godot-log.py "$interaction_ui/smoke.log"
rg -q 'InteractionClientSmokePassed' "$interaction_ui/smoke.log"
journey_ui="$release_soak/journey-client"
mkdir -p "$journey_ui"
"$GODOT" --headless --path game/Ashenwake.Client res://JourneySmoke.tscn --quit-after 2400 --log-file "$journey_ui/smoke.log" -- --journey-smoke --output="$journey_ui"
python3 tools/check-godot-log.py "$journey_ui/smoke.log"
rg -q 'JourneyClientSmokePassed' "$journey_ui/smoke.log"
local_map_output="$release_soak/local-map-client"
mkdir -p "$local_map_output"
"$GODOT" --headless --path game/Ashenwake.Client res://LocalMapSmoke.tscn --quit-after 7200 --log-file "$local_map_output/smoke.log" -- --local-map-smoke --discipline=Vanguard --output="$local_map_output"
python3 tools/check-godot-log.py "$local_map_output/smoke.log"
rg -q 'LocalMapClientSmokePassed' "$local_map_output/smoke.log"

service_ui="$release_soak/service-client"
mkdir -p "$service_ui"
"$GODOT" --headless --path game/Ashenwake.Client res://ServiceInteractionSmoke.tscn --quit-after 600 --log-file "$service_ui/smoke.log" -- --service-interaction-smoke --output="$service_ui"
python3 tools/check-godot-log.py "$service_ui/smoke.log"
rg -q 'ServiceInteractionClientSmokePassed' "$service_ui/smoke.log"
