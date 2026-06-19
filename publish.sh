#!/bin/sh
python3 -c "
import json
with open('README.md') as f:
    desc = f.read()
    changelog = desc[desc.find('## Changelog'):]
    desc = desc[:desc.find('## Building from Source')]
with open('SteamWorkshop/workshop.json') as f:
    ws = json.load(f)
ws['description'] = desc
ws['changeNote'] = changelog
with open('SteamWorkshop/workshop.json', 'w') as f:
    json.dump(ws, f, indent=2)
"
cp dist/* SteamWorkshop/content
cp RouteSuggest/mod_image.png SteamWorkshop/image.png
./ModUploader-linux-x64/ModUploader upload -w $PWD/SteamWorkshop
