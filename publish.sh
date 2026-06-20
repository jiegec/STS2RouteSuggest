#!/bin/sh
python3 -c "
import json
with open('README.md') as f:
    desc = f.read()
    changelog = desc[desc.find('## Changelog'):]
    desc = desc[:desc.find('## Building from Source')]
with open('README_zh.md') as f:
    desc_zh = f.read()
    changelog_zh = desc_zh[desc_zh.find('## 更新日志'):]
    desc_zh = desc_zh[:desc_zh.find('## 手动安装方法')]
with open('SteamWorkshop/workshop.json') as f:
    ws = json.load(f)
ws['description'] = desc + desc_zh
ws['changeNote'] = changelog + changelog_zh
with open('SteamWorkshop/workshop.json', 'w') as f:
    json.dump(ws, f, indent=2)
"
cp dist/* SteamWorkshop/content
cp RouteSuggest/mod_image.png SteamWorkshop/image.png
./ModUploader-linux-x64/ModUploader upload -w $PWD/SteamWorkshop
