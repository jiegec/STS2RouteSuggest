# RouteSuggest - Slay the Spire 2 Mod

![](screenshot.png)

A mod for Slay the Spire 2 that suggests the optimal path through the map and highlights it in the UI.

## Features

- **Automatic Path Calculation**: Computes the best route from your current position to the Boss
- **Visual Highlighting**: Highlights the suggested path in gold on the map screen
- **Smart Scoring**: Weighs different room types to find the optimal balance of rewards vs risks

## Installation

1. Download the latest release
2. Extract the mod files to your Slay the Spire 2 mods folder (`mods` folder should reside in the same folder as the game executable):
   - **macOS**: `~/Library/Application\ Support/Steam/steamapps/common/Slay\ the\ Spire\ 2/SlayTheSpire2.app/Contents/MacOS/mods/`
   - **Linux**: `~/.steam/steam/steamapps/common/Slay\ the\ Spire\ 2/mods`
3. Launch Slay the Spire 2 - the mod will load automatically

## Building from Source

### Prerequisites

- .NET 9.0 SDK or later
- Godot 4.5.1 with Mono support
- Slay the Spire 2 installed (for sts2.dll reference)

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

The mod uses a scoring system to evaluate each possible path from your current position to the Boss:

| Room Type     | Score | Reason                              |
|---------------|-------|-------------------------------------|
| **Rest Site** | +1    | Heal and upgrade cards              |
| **Treasure**  | +1    | Free relic                          |
| **Shop**      | +1    | Buy cards, relics, and potions      |
| **Monster**   | -1    | Standard combat encounter           |
| **Elite**     | -2    | Hard combat with better rewards     |
| **Boss**      | 0     | Final destination (no score impact) |
