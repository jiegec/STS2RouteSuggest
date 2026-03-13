#!/bin/bash
set -x

OS="$(uname -s)"

case "$OS" in
    Linux*)
        cp ~/.steam/steam/steamapps/common/Slay\ the\ Spire\ 2/data_sts2_linuxbsd_x86_64/sts2.dll .
        ../Godot_v4.5.1-stable_mono_linux_x86_64/Godot_v4.5.1-stable_mono_linux.x86_64 --build-solutions --quit --headless
        rm -rf dist
        mkdir -p dist
        cp ./.godot/mono/temp/bin/Debug/RouteSuggest.dll dist/
        ../Godot_v4.5.1-stable_mono_linux_x86_64/Godot_v4.5.1-stable_mono_linux.x86_64 --export-pack "Windows Desktop" dist/RouteSuggest.pck --headless
        cp mod_manifest.json dist/RouteSuggest.json
        ;;
    Darwin*)
        cp ~/Library/Application\ Support/steam/steamapps/common/Slay\ the\ Spire\ 2/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64/sts2.dll .
        /Applications/Godot_mono.app/Contents/MacOS/Godot --build-solutions --quit --headless
        rm -rf dist
        mkdir -p dist
        cp ./.godot/mono/temp/bin/Debug/RouteSuggest.dll dist/
        /Applications/Godot_mono.app/Contents/MacOS/Godot --export-pack "Windows Desktop" dist/RouteSuggest.pck --headless
        cp mod_manifest.json dist/RouteSuggest.json
        ;;
    *)
        echo "Unknown operating system: $OS"
        exit 1
        ;;
esac

