#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$AW_ROOT"
if [[ $# != 1 ]]; then
    echo 'Usage: tools/collect-notices.sh <new-notice-directory>' >&2
    exit 2
fi
notice_output="$1"
if [[ -e "$notice_output" ]]; then
    echo 'Notice destination must be new; existing distribution data is preserved.' >&2
    exit 2
fi
mkdir -p "$notice_output"
notice_output="$(cd "$notice_output" && pwd)"
"$GODOT" --headless --path "$AW_ROOT/game/Ashenwake.Client" --script "$AW_ROOT/tools/collect-godot-notices.gd" --log-file "$notice_output/collection.log" -- "$notice_output"
python3 tools/check-godot-log.py "$notice_output/collection.log"
python3 - "$notice_output" <<'PY'
import hashlib, json, os, pathlib, platform, shutil, sys
root = pathlib.Path(os.environ['AW_ROOT'])
destination = pathlib.Path(sys.argv[1])
targets = ['osx-arm64', 'osx-x64'] if platform.system() == 'Darwin' else ['linux-x64']
records = []
for pack in json.loads((root / 'tools/runtime-packs.json').read_text()):
    if not any(pack['id'] == 'microsoft.netcore.app.runtime.' + target for target in targets):
        continue
    package = pathlib.Path(os.environ['NUGET_PACKAGES']) / pack['id'] / pack['version']
    for name in ['LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT']:
        source = package / name
        output_name = pack['id'] + '-' + pack['version'] + '-' + name
        shutil.copyfile(source, destination / output_name)
        records.append({'package':pack['id'], 'version':pack['version'], 'file':output_name,
                        'sha256':hashlib.sha256(source.read_bytes()).hexdigest().upper()})
godot = json.loads((destination / 'Godot-notices.json').read_text())
if any(godot['version'].get(k) != v for k, v in {'major':4, 'minor':6, 'patch':2,
        'status':'stable', 'build':'official', 'hash':'71f334935c000924d403448e698df4441130df18'}.items()):
    raise SystemExit('Engine version differs from pinned notice source')
(destination / 'notice-sources.json').write_text(json.dumps({'schemaVersion':1,
    'godotVersion':godot['version'], 'runtimeNotices':records,
    'scope':'Engine-provided notices and pinned redistributed .NET runtime packs; project rights are recorded separately.'}, indent=2) + '\n')
shutil.copyfile(root / 'assets/credits.json', destination / 'project-credits.json')
print('RuntimeNoticesCollected')
PY
