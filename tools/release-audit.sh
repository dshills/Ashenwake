#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$AW_ROOT"
dotnet build game/Ashenwake.Tooling --no-restore --disable-build-servers -m:1
dotnet game/Ashenwake.Tooling/bin/Debug/net8.0/Ashenwake.Tooling.dll release audit "$AW_ROOT"
dotnet game/Ashenwake.Tooling/bin/Debug/net8.0/Ashenwake.Tooling.dll release soak "${AW_SOAK_SESSIONS:-10}" "${AW_SOAK_TICKS:-900}" artifacts/release/soak.json
dotnet game/Ashenwake.Tooling/bin/Debug/net8.0/Ashenwake.Tooling.dll release projectile-ceiling
if [[ $# -gt 0 ]]; then
    if [[ $# != 3 ]]; then
        echo 'Optional manifest arguments: <distribution-root> <runtime-identity> <platform-identity>' >&2
        exit 2
    fi
    build_id="$(git rev-parse HEAD)"
    dotnet game/Ashenwake.Tooling/bin/Debug/net8.0/Ashenwake.Tooling.dll release manifest "$1" "$build_id" endgame-runtime.1 "$2" "$3" artifacts/release/manifest.json
    dotnet game/Ashenwake.Tooling/bin/Debug/net8.0/Ashenwake.Tooling.dll release verify "$1" artifacts/release/manifest.json
fi
