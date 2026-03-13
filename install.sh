#!/bin/bash
set -x

OS="$(uname -s)"

case "$OS" in
    Linux*)
        mkdir -p ~/.steam/steam/steamapps/common/Slay\ the\ Spire\ 2/mods/RouteSuggest
        cp -r dist/* ~/.steam/steam/steamapps/common/Slay\ the\ Spire\ 2/mods/RouteSuggest
        ;;
    Darwin*)
        mkdir -p ~/Library/Application\ Support/Steam/steamapps/common/Slay\ the\ Spire\ 2/SlayTheSpire2.app/Contents/MacOS/mods/RouteSuggest
        cp -r dist/* ~/Library/Application\ Support/Steam/steamapps/common/Slay\ the\ Spire\ 2/SlayTheSpire2.app/Contents/MacOS/mods/RouteSuggest
        ;;
    *)
        echo "Unknown operating system: $OS"
        exit 1
        ;;
esac

