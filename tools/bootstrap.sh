#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"
mkdir -p "$AW_ROOT/.tools/downloads" "$DOTNET_ROOT" "$AW_ROOT/.tools/godot" "$AW_ROOT/.tools/templates" "$AW_ROOT/.tools/bin"
case "$(uname -s)-$(uname -m)" in
    Darwin-arm64)
        sdk_rid=osx-arm64
        sdk_hash=d42156be70b489236479a415536013a89a9fd6eaca57d5716a7017612fcc65bdb459068973d7cffd6fe710f2799af8bee95b2046ba032762d47df176f5c4a7d6
        editor=Godot_v4.6.2-stable_mono_macos.universal.zip
        editor_hash=76ed530180f8578ca9a5638b36304efc9c8c22d48c0abe21778d072464686b47
        ;;
    Linux-x86_64)
        sdk_rid=linux-x64
        sdk_hash=934b8060a7190e5909ad1fd0785db542f487b3bbf6cdd14826b02095fdd0d0394298b1634085eff302928fccc33f7c1a7253e9b87df555fc36fce819bcd2e798
        editor=Godot_v4.6.2-stable_mono_linux_x86_64.zip
        editor_hash=7d53302c31648ad98b620e8ca5b0c869c1066495770e62b2fa770cfeb004f167
        ;;
    *) echo 'Bootstrap supports macOS arm64 and Linux x86_64.' >&2; exit 1 ;;
esac
download() {
    local url="$1" dest="$2" algorithm="$3" expected="$4"
    if [[ ! -f "$dest" ]]; then
        curl --fail --location --retry 3 "$url" --output "$dest.part"
        mv "$dest.part" "$dest"
    fi
    python3 - "$dest" "$algorithm" "$expected" <<'PY'
import hashlib, sys
with open(sys.argv[1], 'rb') as f:
    h = hashlib.new(sys.argv[2])
    for block in iter(lambda: f.read(1024 * 1024), b''):
        h.update(block)
if h.hexdigest() != sys.argv[3]:
    raise SystemExit('Checksum mismatch: ' + sys.argv[1])
PY
}
sdk_archive="$AW_ROOT/.tools/downloads/dotnet-sdk-8.0.425-$sdk_rid.tar.gz"
download "https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.425/dotnet-sdk-8.0.425-$sdk_rid.tar.gz" "$sdk_archive" sha512 "$sdk_hash"
if [[ ! -x "$DOTNET_ROOT/dotnet" ]]; then tar -xzf "$sdk_archive" -C "$DOTNET_ROOT"; fi
download "https://github.com/godotengine/godot/releases/download/4.6.2-stable/$editor" "$AW_ROOT/.tools/downloads/$editor" sha256 "$editor_hash"
if [[ ! -x "$GODOT" ]]; then unzip -q "$AW_ROOT/.tools/downloads/$editor" -d "$AW_ROOT/.tools/godot"; fi
mkdir -p "$AW_ROOT/.tools/feed"
if [[ "$(uname -s)" == Darwin ]]; then
    cp "$AW_ROOT/.tools/godot/Godot_mono.app/Contents/Resources/GodotSharp/Tools/nupkgs/"*.nupkg "$AW_ROOT/.tools/feed/"
else
    cp "$AW_ROOT/.tools/godot/Godot_v4.6.2-stable_mono_linux_x86_64/GodotSharp/Tools/nupkgs/"*.nupkg "$AW_ROOT/.tools/feed/"
fi
if [[ "${1:-}" == --templates ]]; then
    templates=Godot_v4.6.2-stable_mono_export_templates.tpz
    download "https://github.com/godotengine/godot/releases/download/4.6.2-stable/$templates" "$AW_ROOT/.tools/downloads/$templates" sha256 4ecf72faf76f96e010d166ddbbe3f0fb8e7df9633282666a3a9afd4ee3e00e7d
    unzip -oq "$AW_ROOT/.tools/downloads/$templates" 'templates/macos.zip' 'templates/linux_debug.x86_64' 'templates/linux_release.x86_64' 'templates/version.txt' -d "$AW_ROOT/.tools/templates"
fi
python3 "$AW_ROOT/tools/fetch-locked-packages.py"
dotnet --version
"$GODOT" --version
