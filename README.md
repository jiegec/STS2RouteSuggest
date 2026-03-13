# RouteSuggest - Slay the Spire 2 Mod

![](screenshot.png)

A mod for Slay the Spire 2 that suggests the optimal path through the map and highlights it in gold/red on the map screen.

**Supported game versions:** v0.98.3 and v0.99 (public beta)

## Features

- **Dual path suggestions**: Shows two optimal routes - a safe path and an aggressive path
- **Visual highlighting**: 
  - **Gold**: Safe path (minimizes risk)
  - **Red**: Aggressive path (prioritizes combat for rewards)
- **Smart scoring**: Different weights for safe vs aggressive playstyles

## Installation

1. Download the latest release from [GitHub releases](https://github.com/jiegec/STS2RouteSuggest/releases) or [Nexus mods](https://www.nexusmods.com/slaythespire2/mods/54)
2. Extract the mod files to your Slay the Spire 2 mods folder (`mods` folder should reside in the same folder as the game executable):
   - **macOS**: `~/Library/Application\ Support/Steam/steamapps/common/Slay\ the\ Spire\ 2/SlayTheSpire2.app/Contents/MacOS/mods/`
   - **Linux**: `~/.steam/steam/steamapps/common/Slay\ the\ Spire\ 2/mods`
3. Launch Slay the Spire 2 - the mod will load automatically

## Building from Source

### Prerequisites

- .NET 9.0 SDK or later
- Godot 4.5.1 with Mono support
- Slay the Spire 2 (for the sts2.dll reference)

### Build Steps

```bash
# Clone the repository
git clone https://github.com/jiegec/STS2RouteSuggest
cd RouteSuggest

# Build the mod
./build.sh

# Install the mod
./install.sh
```

## How It Works

The mod calculates two optimal paths using different scoring systems:

### Safe Path (Gold)

Minimizes encounters and prioritizes safety:

| Room Type     | Score | Reason                         |
|---------------|-------|--------------------------------|
| **Rest Site** | +1    | Heal and upgrade cards         |
| **Treasure**  | +1    | Free relic                     |
| **Shop**      | +1    | Buy cards, relics, and potions |
| **Monster**   | -1    | Avoid combat                   |
| **Elite**     | -3    | Avoid hard encounters          |
| **Boss**      | 0     | Final destination              |

### Aggressive Path (Red)

Prioritizes combat rewards and unknown encounters:

| Room Type     | Score | Reason                         |
|---------------|-------|--------------------------------|
| **Rest Site** | +1    | Heal and upgrade cards         |
| **Treasure**  | +1    | Free relic                     |
| **Shop**      | +1    | Buy cards, relics, and potions |
| **Monster**   | +2    | Gold and card rewards          |
| **Elite**     | +3    | Relics and better rewards      |
| **Unknown**   | +2    | Potential for combat           |
| **Boss**      | 0     | Final destination              |

When both paths share an edge, it appears in gold.

## Changelog

### Unreleased

- Tuned the weights for safe/aggressive paths

### v1.2.0

- Fixed path highlighting when entering a new act

### v1.1.0

- Added dual path mode: safe path (gold) and aggressive path (red)
- Safe path minimizes combat encounters
- Aggressive path prioritizes Elite (+2), Monster (+1), and Unknown (+1) rooms
- Overlapping edges shown in gold

### v1.0.0

- Initial release
- Support for game versions v0.98.3 and v0.99
