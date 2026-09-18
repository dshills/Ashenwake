#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$AW_ROOT"
dotnet restore Ashenwake.sln --locked-mode --source "$AW_ROOT/.tools/feed" --disable-parallel --disable-build-servers -p:NuGetAudit=false
dotnet build Ashenwake.sln --no-restore --disable-build-servers -m:1
dotnet format Ashenwake.sln --verify-no-changes --no-restore
dotnet test game/Ashenwake.Tests --no-build --no-restore
python3 tools/test-godot-log.py
test -z "$(gofmt -l tools/aw/*.go)"
(cd tools/aw && go vet ./... && go test ./... && go build -o ../../.tools/bin/aw .)
aw content validate
aw content compile
aw demo run
aw replay run artifacts/phase0/demo.awr
aw benchmark run
aw sandbox validate
aw sandbox compile
aw sandbox demo
aw sandbox builds
aw sandbox replay artifacts/combat/session.awc
aw sandbox benchmark
aw adventure validate
aw adventure compile
aw adventure demo
aw adventure benchmark
aw production validate
aw production compile
aw authoring validate
aw production demo
aw production benchmark
aw balance run
aw balance loot
mkdir -p artifacts/client
"$GODOT" --headless --path game/Ashenwake.Client --editor --import --log-file "$AW_ROOT/artifacts/client/import.log"
"$GODOT" --headless --path game/Ashenwake.Client res://Main.tscn --quit-after 600 --log-file "$AW_ROOT/artifacts/client/smoke.log" -- --smoke --output="$AW_ROOT/artifacts/client"
if rg -n 'ERROR:|SCRIPT ERROR:' artifacts/client/import.log artifacts/client/smoke.log; then
    echo 'Godot import or smoke emitted an error.' >&2
    exit 1
fi
rg -q 'ClientSmokePassed' artifacts/client/smoke.log
aw replay run artifacts/client/session.awr
python3 - <<'PY'
from pathlib import Path
import json
root = Path('artifacts')
client = json.loads((root/'client/client-report.json').read_text())
demo = json.loads((root/'phase0/demo.save.json').read_text())
assert client['stateHash'] == demo['stateHash'], 'Client/headless state differs'
print('Client and headless demo have identical state hashes.')
PY
content_key=$(python3 -c 'import hashlib; print(hashlib.sha256(b"".join(open(p, "rb").read() for p in ("content/combat.json", "content/adventure.json"))).hexdigest()[:12])')
sandbox_output="$AW_ROOT/artifacts/sandbox-client/$content_key"
mkdir -p "$sandbox_output"
"$GODOT" --headless --path game/Ashenwake.Client res://Sandbox.tscn --quit-after 600 --log-file "$sandbox_output/smoke.log" -- --sandbox-smoke --output="$sandbox_output"
if rg -n 'ERROR:|SCRIPT ERROR:' "$sandbox_output/smoke.log"; then exit 1; fi
rg -q 'SandboxSmokePassed' "$sandbox_output/smoke.log"
aw sandbox replay "$sandbox_output/session.awc"
adventure_output="$AW_ROOT/artifacts/adventure-client/$content_key"
mkdir -p "$adventure_output"
"$GODOT" --headless --path game/Ashenwake.Client res://Adventure.tscn --quit-after 3000 --log-file "$adventure_output/smoke.log" -- --adventure-smoke --output="$adventure_output"
if rg -n 'ERROR:|SCRIPT ERROR:' "$adventure_output/smoke.log"; then exit 1; fi
rg -q 'AdventureClientSmokePassed' "$adventure_output/smoke.log"
aw adventure replay "$adventure_output/expedition.awx"
production_key=$(python3 -c 'import hashlib; print(hashlib.sha256(b"".join(open(p, "rb").read() for p in ("content/combat.json", "content/adventure.json", "content/progression.json", "content/text.en.json"))).hexdigest()[:12])')
production_output="$AW_ROOT/artifacts/production-client/$production_key"
mkdir -p "$production_output"
"$GODOT" --headless --path game/Ashenwake.Client res://Production.tscn --quit-after 6000 --log-file "$production_output/smoke.log" -- --production-smoke --output="$production_output"
if rg -n 'ERROR:|SCRIPT ERROR:' "$production_output/smoke.log"; then exit 1; fi
rg -q 'ProductionClientSmokePassed' "$production_output/smoke.log"
aw production replay "$production_output/production.awp"
