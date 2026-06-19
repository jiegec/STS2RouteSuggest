#!/bin/sh
cp dist/* SteamWorkshop/content
cp RouteSuggest/mod_image.png SteamWorkshop/image.png
./ModUploader-linux-x64/ModUploader upload -w $PWD/SteamWorkshop
