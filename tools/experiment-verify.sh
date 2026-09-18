#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$AW_ROOT"
dotnet build game/Ashenwake.Tooling --no-restore --disable-build-servers -m:1
dotnet build game/Ashenwake.Client --no-restore --disable-build-servers -m:1
experiment_validation="$(aw experiment validate)"
experiment_key="$(python3 -c 'import json,sys; print(json.load(sys.stdin)["key"])' <<< "$experiment_validation")"
experiment_output="$AW_ROOT/artifacts/experiment-verification/$experiment_key"
mkdir -p "$experiment_output"
printf '%s\n' "$experiment_validation" > "$experiment_output/content-validation.json"
aw experiment compile
aw experiment demo "$experiment_output/cli"
aw experiment replay "$experiment_output/cli/echoes.awexperiment"
"$GODOT" --headless --path game/Ashenwake.Client --editor --import --log-file "$experiment_output/import.log"
python3 tools/check-godot-log.py "$experiment_output/import.log"
"$GODOT" --headless --path game/Ashenwake.Client res://Endgame.tscn --quit-after 12000 --log-file "$experiment_output/smoke.log" -- --echoes-smoke --output="$experiment_output/client"
python3 tools/check-godot-log.py "$experiment_output/smoke.log"
rg -q 'ExperimentClientSmokePassed' "$experiment_output/smoke.log"
aw experiment replay "$experiment_output/client/echoes.awexperiment"
aw experiment replay "$experiment_output/client/keep-mind.awexperiment"
aw experiment replay "$experiment_output/client/release-memory.awexperiment"
