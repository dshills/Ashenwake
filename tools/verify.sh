#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$AW_ROOT"
dotnet restore Ashenwake.sln --locked-mode --source "$AW_ROOT/.tools/feed" --disable-parallel --disable-build-servers -p:NuGetAudit=false
dotnet build Ashenwake.sln --no-restore --disable-build-servers -m:1
dotnet format Ashenwake.sln --verify-no-changes --no-restore
dotnet test game/Ashenwake.Tests --no-build --no-restore
test -z "$(gofmt -l tools/aw/*.go)"
(cd tools/aw && go vet ./... && go test ./... && go build -o ../../.tools/bin/aw .)
aw content validate
aw content compile
aw demo run
aw replay run artifacts/phase0/demo.awr
aw benchmark run
mkdir -p artifacts/client
"$GODOT" --headless --path game/Ashenwake.Client --editor --import --log-file "$AW_ROOT/artifacts/client/import.log"
"$GODOT" --headless --path game/Ashenwake.Client --quit-after 600 --log-file "$AW_ROOT/artifacts/client/smoke.log" -- --smoke --output="$AW_ROOT/artifacts/client"
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
