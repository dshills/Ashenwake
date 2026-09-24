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

death_recap_output="$package_output/death-recap-client"
mkdir -p "$death_recap_output"
"$binary" --headless --quit-after 18000 --log-file "$death_recap_output/smoke.log" -- --death-recap-smoke --discipline=Vanguard --output="$death_recap_output"
python3 tools/check-godot-log.py "$death_recap_output/smoke.log"
python3 -c 'import json,sys; assert json.load(open(sys.argv[1]))["passed"]' "$death_recap_output/death-recap-review.json"

training_output="$package_output/training-client"
mkdir -p "$training_output"
"$binary" --headless --quit-after 16000 --log-file "$training_output/smoke.log" -- --training-smoke --discipline=Vanguard --output="$training_output"
python3 tools/check-godot-log.py "$training_output/smoke.log"
python3 -c 'import json,sys; assert json.load(open(sys.argv[1]))["passed"]' "$training_output/training-review.json"

defensive_training_output="$package_output/defensive-training-client"
mkdir -p "$defensive_training_output"
"$binary" --headless --quit-after 18000 --log-file "$defensive_training_output/smoke.log" -- --defensive-training-smoke --discipline=Vanguard --output="$defensive_training_output"
python3 tools/check-godot-log.py "$defensive_training_output/smoke.log"
python3 -c 'import json,sys; assert json.load(open(sys.argv[1]))["passed"]' "$defensive_training_output/defensive-training-review.json"

loot_management_output="$package_output/loot-management-client"
mkdir -p "$loot_management_output"
"$binary" --headless --quit-after 16000 --log-file "$loot_management_output/smoke.log" -- --loot-management-smoke --discipline=Vanguard --output="$loot_management_output"
python3 tools/check-godot-log.py "$loot_management_output/smoke.log"
python3 -c 'import json,sys; assert json.load(open(sys.argv[1]))["passed"]' "$loot_management_output/loot-management-review.json"

mouse_output="$package_output/mouse-client"
mkdir -p "$mouse_output"
"$binary" --headless --quit-after 2400 --log-file "$mouse_output/smoke.log" -- --mouse-movement-smoke --output="$mouse_output"
python3 tools/check-godot-log.py "$mouse_output/smoke.log"
rg -q 'MouseMovementClientSmokePassed' "$mouse_output/smoke.log"

mouse_actions_output="$package_output/mouse-actions-client"
mkdir -p "$mouse_actions_output"
"$binary" --headless --quit-after 7200 --log-file "$mouse_actions_output/smoke.log" -- --mouse-actions-smoke --discipline=Vanguard --output="$mouse_actions_output"
python3 tools/check-godot-log.py "$mouse_actions_output/smoke.log"
rg -q 'MouseActionsClientSmokePassed' "$mouse_actions_output/smoke.log"

anatomy_output="$package_output/anatomy-client"
mkdir -p "$anatomy_output"
"$binary" --headless --quit-after 12000 --log-file "$anatomy_output/smoke.log" -- --anatomy-smoke --discipline=Vanguard --output="$anatomy_output"
python3 tools/check-godot-log.py "$anatomy_output/smoke.log"
rg -q 'AnatomyClientSmokePassed' "$anatomy_output/smoke.log"

interaction_output="$package_output/interaction-client"
mkdir -p "$interaction_output"
"$binary" --headless --quit-after 1200 --log-file "$interaction_output/smoke.log" -- --interaction-smoke --output="$interaction_output"
python3 tools/check-godot-log.py "$interaction_output/smoke.log"
rg -q 'InteractionClientSmokePassed' "$interaction_output/smoke.log"

journey_output="$package_output/journey-client"
mkdir -p "$journey_output"
"$binary" --headless --quit-after 2400 --log-file "$journey_output/smoke.log" -- --journey-smoke --output="$journey_output"
python3 tools/check-godot-log.py "$journey_output/smoke.log"
rg -q 'JourneyClientSmokePassed' "$journey_output/smoke.log"

opening_combat_output="$package_output/opening-combat-client"
mkdir -p "$opening_combat_output"
"$binary" --headless --quit-after 18000 --log-file "$opening_combat_output/smoke.log" -- --opening-combat-smoke --output="$opening_combat_output"
python3 tools/check-godot-log.py "$opening_combat_output/smoke.log"
rg -q 'OpeningCombatClientSmokePassed' "$opening_combat_output/smoke.log"

local_map_output="$package_output/local-map-client"
mkdir -p "$local_map_output"
"$binary" --headless --quit-after 7200 --log-file "$local_map_output/smoke.log" -- --local-map-smoke --discipline=Vanguard --output="$local_map_output"
python3 tools/check-godot-log.py "$local_map_output/smoke.log"
rg -q 'LocalMapClientSmokePassed' "$local_map_output/smoke.log"

verdant_output="$package_output/verdant-client"
mkdir -p "$verdant_output"
"$binary" --headless --quit-after 2400 --log-file "$verdant_output/smoke.log" -- --verdant-smoke --output="$verdant_output"
python3 tools/check-godot-log.py "$verdant_output/smoke.log"
rg -q 'VerdantClientSmokePassed' "$verdant_output/smoke.log"

verdant_exploration_output="$package_output/verdant-exploration-client"
mkdir -p "$verdant_exploration_output"
"$binary" --headless --quit-after 18000 --log-file "$verdant_exploration_output/smoke.log" -- --verdant-exploration-smoke --discipline=Vanguard --output="$verdant_exploration_output"
python3 tools/check-godot-log.py "$verdant_exploration_output/smoke.log"
rg -q 'VerdantExplorationClientSmokePassed' "$verdant_exploration_output/smoke.log"

cinder_exploration_output="$package_output/cinder-exploration-client"
mkdir -p "$cinder_exploration_output"
"$binary" --headless --quit-after 24000 --log-file "$cinder_exploration_output/smoke.log" -- --cinder-exploration-smoke --discipline=Vanguard --output="$cinder_exploration_output"
python3 tools/check-godot-log.py "$cinder_exploration_output/smoke.log"
rg -q 'CinderExplorationClientSmokePassed' "$cinder_exploration_output/smoke.log"

spine_exploration_output="$package_output/spine-exploration-client"
mkdir -p "$spine_exploration_output"
"$binary" --headless --quit-after 30000 --log-file "$spine_exploration_output/smoke.log" -- --spine-exploration-smoke --discipline=Vanguard --output="$spine_exploration_output"
python3 tools/check-godot-log.py "$spine_exploration_output/smoke.log"
rg -q 'SpineExplorationClientSmokePassed' "$spine_exploration_output/smoke.log"

hollow_exploration_output="$package_output/hollow-exploration-client"
mkdir -p "$hollow_exploration_output"
"$binary" --headless --quit-after 36000 --log-file "$hollow_exploration_output/smoke.log" -- --hollow-exploration-smoke --discipline=Vanguard --output="$hollow_exploration_output"
python3 tools/check-godot-log.py "$hollow_exploration_output/smoke.log"
rg -q 'HollowExplorationClientSmokePassed' "$hollow_exploration_output/smoke.log"

cinder_output="$package_output/cinder-client"
mkdir -p "$cinder_output"
"$binary" --headless --quit-after 3600 --log-file "$cinder_output/smoke.log" -- --cinder-smoke --output="$cinder_output"
python3 tools/check-godot-log.py "$cinder_output/smoke.log"
rg -q 'CinderClientSmokePassed' "$cinder_output/smoke.log"

spine_output="$package_output/spine-client"
mkdir -p "$spine_output"
"$binary" --headless --quit-after 4800 --log-file "$spine_output/smoke.log" -- --spine-smoke --output="$spine_output"
python3 tools/check-godot-log.py "$spine_output/smoke.log"
rg -q 'SpineClientSmokePassed' "$spine_output/smoke.log"

hollow_output="$package_output/hollow-client"
mkdir -p "$hollow_output"
"$binary" --headless --quit-after 7200 --log-file "$hollow_output/smoke.log" -- --hollow-smoke --output="$hollow_output"
python3 tools/check-godot-log.py "$hollow_output/smoke.log"
rg -q 'HollowClientSmokePassed' "$hollow_output/smoke.log"

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

opening_audio_output="$package_output/opening-audio-client"
mkdir -p "$opening_audio_output"
"$binary" --headless --quit-after 24000 --log-file "$opening_audio_output/smoke.log" -- --opening-audio-smoke --output="$opening_audio_output"
python3 tools/check-godot-log.py "$opening_audio_output/smoke.log"
rg -q 'OpeningAudioClientSmokePassed' "$opening_audio_output/smoke.log"
aw campaign replay "$opening_audio_output/opening-audio.awcampaign"

verdant_audio_output="$package_output/verdant-audio-client"
mkdir -p "$verdant_audio_output"
"$binary" --headless --quit-after 30000 --log-file "$verdant_audio_output/smoke.log" -- --verdant-audio-smoke --output="$verdant_audio_output"
python3 tools/check-godot-log.py "$verdant_audio_output/smoke.log"
rg -q 'VerdantAudioClientSmokePassed' "$verdant_audio_output/smoke.log"
aw campaign replay "$verdant_audio_output/verdant-audio.awcampaign"

cinder_audio_output="$package_output/cinder-audio-client"
mkdir -p "$cinder_audio_output"
"$binary" --headless --quit-after 36000 --log-file "$cinder_audio_output/smoke.log" -- --cinder-audio-smoke --output="$cinder_audio_output"
python3 tools/check-godot-log.py "$cinder_audio_output/smoke.log"
rg -q 'CinderAudioClientSmokePassed' "$cinder_audio_output/smoke.log"
aw campaign replay "$cinder_audio_output/cinder-audio.awcampaign"

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

crafting_output="$package_output/crafting-client"
mkdir -p "$crafting_output"
"$binary" --headless --quit-after 9000 --log-file "$crafting_output/smoke.log" -- --crafting-smoke --output="$crafting_output"
python3 tools/check-godot-log.py "$crafting_output/smoke.log"
rg -q 'CraftingClientSmokePassed' "$crafting_output/smoke.log"

skills_output="$package_output/skills-client"
mkdir -p "$skills_output"
"$binary" --headless --quit-after 18000 --log-file "$skills_output/smoke.log" -- --skills-smoke --output="$skills_output"
python3 tools/check-godot-log.py "$skills_output/smoke.log"
rg -q 'SkillsClientSmokePassed' "$skills_output/smoke.log"

expedition_output="$package_output/expedition-client"
mkdir -p "$expedition_output"
"$binary" --headless --quit-after 18000 --log-file "$expedition_output/smoke.log" -- --expedition-smoke --output="$expedition_output"
python3 tools/check-godot-log.py "$expedition_output/smoke.log"
rg -q 'ExpeditionClientSmokePassed' "$expedition_output/smoke.log"

hud_output="$package_output/hud-client"
mkdir -p "$hud_output"
"$binary" --headless --quit-after 18000 --log-file "$hud_output/smoke.log" -- --hud-smoke --output="$hud_output"
python3 tools/check-godot-log.py "$hud_output/smoke.log"
rg -q 'HudClientSmokePassed' "$hud_output/smoke.log"

experiment_output="$package_output/experiment-client"
mkdir -p "$experiment_output"
"$binary" --headless --quit-after 12000 --log-file "$experiment_output/smoke.log" -- --echoes-smoke --output="$experiment_output"
python3 tools/check-godot-log.py "$experiment_output/smoke.log"
rg -q 'ExperimentClientSmokePassed' "$experiment_output/smoke.log"
aw experiment replay "$experiment_output/echoes.awexperiment"

echoes_screen_output="$package_output/echoes-screen-client"
mkdir -p "$echoes_screen_output"
"$binary" --headless --quit-after 18000 --log-file "$echoes_screen_output/smoke.log" -- --echoes-screen-smoke --discipline=Vanguard --output="$echoes_screen_output"
python3 tools/check-godot-log.py "$echoes_screen_output/smoke.log"
rg -q 'EchoesScreenClientSmokePassed' "$echoes_screen_output/smoke.log"

front_menu_output="$package_output/front-menu-client"
mkdir -p "$front_menu_output"
"$binary" --headless --quit-after 18000 --log-file "$front_menu_output/smoke.log" -- --front-menu-smoke --output="$front_menu_output"
python3 tools/check-godot-log.py "$front_menu_output/smoke.log"
rg -q 'FrontMenuClientSmokePassed' "$front_menu_output/smoke.log"

settings_output="$package_output/settings-client"
mkdir -p "$settings_output"
"$binary" --headless --quit-after 18000 --log-file "$settings_output/smoke.log" -- --settings-smoke --output="$settings_output"
python3 tools/check-godot-log.py "$settings_output/smoke.log"
rg -q 'SettingsClientSmokePassed' "$settings_output/smoke.log"
