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

interaction_output="$package_output/interaction-client"
mkdir -p "$interaction_output"
"$binary" --headless --quit-after 600 --log-file "$interaction_output/smoke.log" -- --interaction-smoke --output="$interaction_output"
python3 tools/check-godot-log.py "$interaction_output/smoke.log"
rg -q 'InteractionClientSmokePassed' "$interaction_output/smoke.log"

journey_output="$package_output/journey-client"
mkdir -p "$journey_output"
"$binary" --headless --quit-after 2400 --log-file "$journey_output/smoke.log" -- --journey-smoke --output="$journey_output"
python3 tools/check-godot-log.py "$journey_output/smoke.log"
rg -q 'JourneyClientSmokePassed' "$journey_output/smoke.log"

verdant_output="$package_output/verdant-client"
mkdir -p "$verdant_output"
"$binary" --headless --quit-after 2400 --log-file "$verdant_output/smoke.log" -- --verdant-smoke --output="$verdant_output"
python3 tools/check-godot-log.py "$verdant_output/smoke.log"
rg -q 'VerdantClientSmokePassed' "$verdant_output/smoke.log"

service_output="$package_output/service-client"
mkdir -p "$service_output"
"$binary" --headless --quit-after 600 --log-file "$service_output/smoke.log" -- --service-interaction-smoke --output="$service_output"
python3 tools/check-godot-log.py "$service_output/smoke.log"
rg -q 'ServiceInteractionClientSmokePassed' "$service_output/smoke.log"

visual_output="$package_output/visual-client"
mkdir -p "$visual_output"
"$binary" --headless --quit-after 1200 --log-file "$visual_output/smoke.log" -- --visual-smoke --output="$visual_output"
python3 tools/check-godot-log.py "$visual_output/smoke.log"
rg -q 'VisualClientSmokePassed' "$visual_output/smoke.log"

combat_feedback_output="$package_output/combat-feedback-client"
mkdir -p "$combat_feedback_output"
"$binary" --headless --quit-after 1200 --log-file "$combat_feedback_output/smoke.log" -- --combat-feedback-smoke --output="$combat_feedback_output"
python3 tools/check-godot-log.py "$combat_feedback_output/smoke.log"
rg -q 'CombatFeedbackClientSmokePassed' "$combat_feedback_output/smoke.log"

appearance_output="$package_output/appearance-client"
mkdir -p "$appearance_output"
"$binary" --headless --quit-after 2400 --log-file "$appearance_output/smoke.log" -- --appearance-smoke --output="$appearance_output"
python3 tools/check-godot-log.py "$appearance_output/smoke.log"
rg -q 'AppearanceClientSmokePassed' "$appearance_output/smoke.log"

experiment_output="$package_output/experiment-client"
mkdir -p "$experiment_output"
"$binary" --headless --quit-after 12000 --log-file "$experiment_output/smoke.log" -- --echoes-smoke --output="$experiment_output"
python3 tools/check-godot-log.py "$experiment_output/smoke.log"
rg -q 'ExperimentClientSmokePassed' "$experiment_output/smoke.log"
aw experiment replay "$experiment_output/echoes.awexperiment"
