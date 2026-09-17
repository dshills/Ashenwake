#!/usr/bin/env bash
# Source from a shell or from another repository script.
AW_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export AW_ROOT
export DOTNET_ROOT="$AW_ROOT/.tools/dotnet"
export DOTNET_CLI_HOME="$AW_ROOT/.tools/dotnet-home"
export NUGET_PACKAGES="$AW_ROOT/.tools/nuget"
export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_CLI_USE_MSBUILD_SERVER=0
export GOCACHE="$AW_ROOT/.tools/go-cache"
export PATH="$DOTNET_ROOT:$AW_ROOT/.tools/bin:$PATH"
if [[ "$(uname -s)" == Darwin ]]; then
    export GODOT="$AW_ROOT/.tools/godot/Godot_mono.app/Contents/MacOS/Godot"
else
    export GODOT="$AW_ROOT/.tools/godot/Godot_v4.6.2-stable_mono_linux_x86_64/Godot_v4.6.2-stable_mono_linux.x86_64"
fi
