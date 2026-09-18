#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$AW_ROOT"
umask 077
coop_existing_package=''
if [[ $# -gt 0 ]]; then
    if [[ $# != 2 || "$1" != '--package-directory' ]]; then
        printf '%s\n' 'usage: tools/coop-package-verify.sh [--package-directory <previous-verifier-output>]' >&2
        exit 2
    fi
    coop_existing_package="$(cd "$2" && pwd)"
fi
mkdir -p artifacts/coop-package
coop_package_output="$(mktemp -d "$AW_ROOT/artifacts/coop-package/run.XXXXXX")"
export COOP_PACKAGE_OUTPUT="$coop_package_output"
dotnet build game/Ashenwake.NetworkProbe --no-restore --disable-build-servers -m:1
if [[ -n "$coop_existing_package" ]]; then
    if [[ "$(uname -s)" == Darwin ]]; then
        COOP_PACKAGE_BINARY="$(find "$coop_existing_package/package/Ashenwake.app/Contents/MacOS" -maxdepth 1 -type f -print -quit)"
        COOP_PACKAGE_ARCHIVE="$coop_existing_package/Ashenwake.zip"
    else
        COOP_PACKAGE_BINARY="$coop_existing_package/Ashenwake.x86_64"
        COOP_PACKAGE_ARCHIVE="$COOP_PACKAGE_BINARY"
    fi
    test -x "$COOP_PACKAGE_BINARY"
    test -f "$COOP_PACKAGE_ARCHIVE"
else
    aw content compile
    aw sandbox compile
    aw adventure compile
    aw production compile
    aw campaign compile
    aw endgame compile
    dotnet build game/Ashenwake.Client --no-restore --disable-build-servers -m:1
    if [[ "$(uname -s)" == Darwin ]]; then
    "$GODOT" --headless --path game/Ashenwake.Client --export-debug macOS "$coop_package_output/Ashenwake.zip" --log-file "$coop_package_output/export.log"
    python3 tools/check-godot-log.py --export "$coop_package_output/export.log"
    unzip -oq "$coop_package_output/Ashenwake.zip" -d "$coop_package_output/package"
    COOP_PACKAGE_BINARY="$(find "$coop_package_output/package/Ashenwake.app/Contents/MacOS" -maxdepth 1 -type f -print -quit)"
    COOP_PACKAGE_ARCHIVE="$coop_package_output/Ashenwake.zip"
    else
    "$GODOT" --headless --path game/Ashenwake.Client --export-debug Linux "$coop_package_output/Ashenwake.x86_64" --log-file "$coop_package_output/export.log"
    python3 tools/check-godot-log.py --export "$coop_package_output/export.log"
    COOP_PACKAGE_BINARY="$coop_package_output/Ashenwake.x86_64"
    chmod +x "$COOP_PACKAGE_BINARY"
    COOP_PACKAGE_ARCHIVE="$COOP_PACKAGE_BINARY"
    fi
fi
export COOP_PACKAGE_BINARY COOP_PACKAGE_ARCHIVE
# The same default launcher must preserve the offline and release-check routes.
"$COOP_PACKAGE_BINARY" --headless --quit-after 30 --log-file "$coop_package_output/offline.log" -- --discipline=Vanguard --output="$coop_package_output/offline"
python3 tools/check-godot-log.py "$coop_package_output/offline.log"
"$COOP_PACKAGE_BINARY" --headless --quit-after 600 --log-file "$coop_package_output/release.log" -- --release-smoke --output="$coop_package_output/release"
python3 tools/check-godot-log.py "$coop_package_output/release.log"
rg -q 'ReleaseClientSmokePassed' "$coop_package_output/release.log"
# This verifier owns both services. Occupied ports are never stopped or reused.
python3 - <<'PY'
import socket
for port in (8088, 5180):
    with socket.socket() as s:
        try: s.bind(('127.0.0.1', port))
        except OSError: raise SystemExit(f'Local test port {port} is occupied; existing services were not touched.')
PY
export ASHENWAKE_SERVER_KEY="$(python3 -c 'import secrets; print(secrets.token_hex(32))')"
export ASHENWAKE_HTTP_ADDR='127.0.0.1:8088'
export ASHENWAKE_GAME_SERVER_URL='ws://127.0.0.1:5180/v1/matches/{allocationId}/socket'
export ASHENWAKE_CONTROL_URL='http://127.0.0.1:8088/'
export ASHENWAKE_LISTEN='http://127.0.0.1:5180'
export ASHENWAKE_SERVER_ARTIFACTS="$coop_package_output/server"
coop_control_pid=''
coop_server_pid=''
cleanup_coop_package() {
    for child in "$coop_server_pid" "$coop_control_pid"; do
        if [[ -n "$child" ]] && kill -0 "$child" 2>/dev/null; then
            kill -TERM "$child"
            wait "$child" || true
        fi
    done
}
trap cleanup_coop_package EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
bash tools/online-services-verify.sh serve > "$coop_package_output/control.log" 2>&1 &
coop_control_pid=$!
dotnet game/Ashenwake.Server/bin/Debug/net8.0/Ashenwake.Server.dll > "$coop_package_output/server.log" 2>&1 &
coop_server_pid=$!
python3 - <<'PY'
import time, urllib.request
for port in (8088, 5180):
    for _ in range(300):
        try:
            with urllib.request.urlopen(f'http://127.0.0.1:{port}/readyz', timeout=.5) as response:
                if response.status == 200: break
        except OSError: time.sleep(.1)
    else: raise SystemExit(f'Owned test service on {port} did not become ready.')
PY
# Obtain 30-second, single-use tickets only after export and service readiness.
dotnet game/Ashenwake.NetworkProbe/bin/Debug/net8.0/Ashenwake.NetworkProbe.dll setup "$coop_package_output/setup"
python3 - <<'PY'
import hashlib, json, os, pathlib, subprocess
output = pathlib.Path(os.environ['COOP_PACKAGE_OUTPUT'])
binary = pathlib.Path(os.environ['COOP_PACKAGE_BINARY'])
connection = json.loads((output/'setup/connection.json').read_text())
client = output/'client'
client.mkdir()
arguments = [str(binary), '--headless', '--max-fps', '60', '--quit-after', '36000', '--log-file', str(client/'smoke.log'), '--',
             '--coop-smoke', '--output='+str(client), '--coop-server='+connection['socketUrl'],
             '--allocation='+connection['allocationId'], '--ticket='+connection['ticket'], '--peer-ticket='+connection['peerTicket']]
try:
    result = subprocess.run(arguments, timeout=660)
except subprocess.TimeoutExpired:
    raise SystemExit('Packaged co-op exceeded its bounded run.')
if result.returncode: raise SystemExit(f'Packaged co-op exited with code {result.returncode}.')
report = json.loads((client/'coop-client-report.json').read_text())
if report.get('kind') != 'CoopClientSmokePassed' or report.get('passed') is not True or report.get('mismatchedSnapshots') != 0:
    raise SystemExit('The packaged co-op did not produce a passing authoritative report.')
summary = {'kind':'CoopPackageSmokePassed', 'binarySha256':hashlib.sha256(binary.read_bytes()).hexdigest(),
           'packageSha256':hashlib.sha256(pathlib.Path(os.environ['COOP_PACKAGE_ARCHIVE']).read_bytes()).hexdigest(),
           'serverAssemblySha256':hashlib.sha256(pathlib.Path('game/Ashenwake.Server/bin/Debug/net8.0/Ashenwake.Server.dll').read_bytes()).hexdigest(),
           'tick':report['tick'], 'matchedSnapshots':report['matchedSnapshots'], 'receipts':len(report['rewards']),
           'stateHash':report['serverStateHash'], 'contentHash':report['contentHash'],
           'route':'Exported default Launch scene -> --coop-smoke -> two authenticated WebSocket peers; no command-line scene override.'}
(output/'report.json').write_text(json.dumps(summary, indent=2)+'\n')
print(json.dumps(summary))
PY
python3 tools/check-godot-log.py "$coop_package_output/client/smoke.log"
rg -q 'CoopClientSmokePassed' "$coop_package_output/client/smoke.log"
