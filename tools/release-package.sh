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
# Source checks deliberately exclude build caches. Rebuild every helper used to
# compile, replay and attest this candidate from the reviewed source first.
dotnet build game/Ashenwake.Tooling --no-restore --disable-build-servers -m:1 -t:Rebuild
mkdir -p "$AW_ROOT/.tools/bin"
(cd tools/aw && go build -o "$AW_ROOT/.tools/bin/aw" .)
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
import configparser, hashlib, json, pathlib, sys
package = pathlib.Path(sys.argv[1])
project = configparser.ConfigParser(interpolation=None)
project.read_string('[godot_header]\n' + pathlib.Path('game/Ashenwake.Client/project.godot').read_text())
version = json.loads(project['application']['config/version'])
if not isinstance(version, str) or not version or len(version) > 64 or any(ord(c) < 32 for c in version):
    raise SystemExit('Project application version is missing or invalid')
identity = {'schemaVersion':1, 'sourceId':sys.argv[2], 'platform':sys.argv[3],
            'runtime':'godot-4.6.2-mono-dotnet-8.0.31', 'rules':'endgame-runtime.1',
            'applicationVersion':version,
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
python3 - "$candidate" <<'PYEVIDENCE'
import json, os, pathlib, shutil, sys
root = pathlib.Path.cwd().resolve()
record_path = root / 'artifacts/export/package-run.json'
record = json.loads(record_path.read_text())
if record.get('schemaVersion') != 1 or record.get('completed') is not True:
    raise SystemExit('Packaged diagnostics did not produce a completed run record')
relative = pathlib.Path(record['packageOutput'])
if relative.is_absolute() or '..' in relative.parts:
    raise SystemExit('Packaged diagnostic output path is invalid')
source = root / relative
resolved = source.resolve(strict=True)
package_root = root / 'artifacts/package'
if source != resolved or resolved == package_root or not resolved.is_relative_to(package_root) or not resolved.is_dir():
    raise SystemExit('Packaged diagnostic output must be a fresh local package run')
evidence = pathlib.Path(sys.argv[1]) / 'evidence'
evidence.mkdir()
for current, directories, files in os.walk(source, followlinks=False):
    # The tested app is intentionally retained only in the exported archive.
    directories[:] = [name for name in directories if name != 'macos' and not name.endswith('.app')]
    if any((pathlib.Path(current) / name).is_symlink() for name in directories):
        raise SystemExit('Unexpected symbolic link in diagnostic evidence')
    for name in files:
        if not (name.endswith('.log') or name.endswith('report.json') or name.endswith('review.json')):
            continue
        path = pathlib.Path(current) / name
        if path.is_symlink() or not path.is_file():
            raise SystemExit('Unexpected diagnostic evidence file')
        destination = evidence / path.relative_to(source)
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, destination)
shutil.copyfile(root / 'artifacts/export/export.log', evidence / 'export.log')
shutil.copyfile(root / 'artifacts/export/package.log', evidence / 'package-smoke.log')
shutil.copyfile(record_path, evidence / 'package-run.json')
PYEVIDENCE
aw release manifest "$package" "$source_id" endgame-runtime.1 godot-4.6.2-mono-dotnet-8.0.31 "$platform_id" "$candidate/manifest.json"
aw release verify "$package" "$candidate/manifest.json"
python3 - "$candidate" <<'PY'
import hashlib, pathlib, sys
root=pathlib.Path(sys.argv[1])
path=root/'manifest.json'
(root/'manifest.sha256').write_text(hashlib.sha256(path.read_bytes()).hexdigest().upper()+'  manifest.json\n')
print('Verified local candidate: '+str(root))
PY
