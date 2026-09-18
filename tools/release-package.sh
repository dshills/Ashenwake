#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$AW_ROOT"
if [[ $# != 1 || ! "$1" =~ ^[a-fA-F0-9]{40,64}$ ]]; then
    echo 'Usage: tools/release-package.sh <reviewed-source-commit-or-tree-hash>' >&2
    exit 2
fi
source_id="$1"
python3 tools/check-release-source.py "$source_id"
bash tools/export.sh
python3 tools/check-release-source.py "$source_id"
mkdir -p artifacts/release
candidate="$(mktemp -d "$AW_ROOT/artifacts/release/candidate.XXXXXX")"
package="$candidate/package"
mkdir -p "$package"
if [[ "$(uname -s)" == Darwin ]]; then
    cp artifacts/export/Ashenwake.zip "$package/Ashenwake.zip"
    platform_id=macos-universal
else
    # The Linux runtime is emitted alongside the executable by Godot/.NET.
    # Select this preset's executable and data directory, never another host's stale export.
    cp artifacts/export/Ashenwake.x86_64 "$package/Ashenwake.x86_64"
    runtime_dir="$(find artifacts/export -maxdepth 1 -type d -name 'data_Ashenwake_linuxbsd_x86_64' -print)"
    if [[ -z "$runtime_dir" ]]; then
        echo 'Linux export runtime directory is missing.' >&2
        exit 1
    fi
    cp -R "$runtime_dir" "$package/"
    platform_id=linux-x86_64
fi
bash tools/collect-notices.sh "$package/Notices"
python3 - "$package" "$source_id" "$platform_id" <<'PY'
import hashlib, json, pathlib, sys
package = pathlib.Path(sys.argv[1])
identity = {'schemaVersion':1, 'sourceId':sys.argv[2], 'platform':sys.argv[3],
            'runtime':'godot-4.6.2-mono-dotnet-8.0.31', 'rules':'endgame-runtime.1',
            'contentFiles':{}, 'assetSources':{}, 'publicReleaseAccepted':False}
for path in sorted(pathlib.Path('content').glob('*.json')):
    identity['contentFiles'][str(path)] = hashlib.sha256(path.read_bytes()).hexdigest().upper()
credits = json.loads(pathlib.Path('assets/credits.json').read_text())
for asset in credits['assets']:
    for name in asset['sourceFiles']:
        identity['assetSources'][name] = hashlib.sha256(pathlib.Path(name).read_bytes()).hexdigest().upper()
(package / 'build-identity.json').write_text(json.dumps(identity,indent=2)+'\n')
PY
cp docs/release_readiness.md "$package/RELEASE-READINESS.md"
cp artifacts/export/package.log "$candidate/package-smoke.log"
aw release manifest "$package" "$source_id" endgame-runtime.1 godot-4.6.2-mono-dotnet-8.0.31 "$platform_id" "$candidate/manifest.json"
aw release verify "$package" "$candidate/manifest.json"
python3 - "$candidate" <<'PY'
import hashlib, pathlib, sys
root=pathlib.Path(sys.argv[1])
path=root/'manifest.json'
(root/'manifest.sha256').write_text(hashlib.sha256(path.read_bytes()).hexdigest().upper()+'  manifest.json\n')
print('Verified local candidate: '+str(root))
PY
