# HellCaster: Neon Siege

A Doom-style 2.5D action mini game built with **WPF + .NET 10**.

## Features

- First-person 2.5D raycast renderer
- Procedural level generation from random campaign seeds
- Wider corridors + carved rooms for better combat arenas
- Guaranteed playable levels (start→exit path and checkpoints)
- Main menu with:
	- New campaign
	- Load saved game
	- Manual save
	- Resolution settings
	- Quality settings (`Low`, `Medium`, `High`, `Ultra`)
	- Fullscreen toggle
	- Difficulty settings
	- Leaderboard + achievements view
- Difficulty modes: `Easy`, `Medium`, `Hard`, `Hell`
- Objective system: kill target + reach exit to advance
- Autosave checkpoints and autosave on level transitions
- Leaderboard persistence and challenge/achievement tracking
- Projectile trail + muzzle flash effects
- Enemy variants (`enemy`, `enemy-scout`, `enemy-brute`)


## Save Data

Local data is stored in:

- `%LOCALAPPDATA%/HellCaster/settings.json`
- `%LOCALAPPDATA%/HellCaster/savegame.json`
- `%LOCALAPPDATA%/HellCaster/leaderboard.json`

## Run

From the repository root:

```powershell
dotnet build HellCaster.slnx
dotnet run --project src/HellCaster.App/HellCaster.App.csproj
```

## Controls

- `W/S` move forward/backward
- `A/D` strafe left/right
- `Q/E` or `Left/Right` turn view
- `Mouse` horizontal look/aim
- `Left Click` or `Space` shoot
- `F` interact/force level transition when objective is complete at exit
- `R` restart after game over
- `ESC` open menu

## Window controls

- Standard Windows controls enabled: minimize, maximize, resize
- Cursor hides during gameplay and returns in menu (`ESC`)

## Release (always newest EXE, old versions cleared)

From repository root:

```powershell
.\release.ps1
```

What this does every run:

1. Deletes previous release outputs
2. Publishes a fresh self-contained single-file Windows build
3. Copies newest executable to:
	- `release/HellCaster-latest.exe`
4. Rebuilds clean output folder:
	- `release/latest/`
5. Writes release metadata:
	- `release/latest.json`

Run this file for normal play:

- `release/HellCaster-latest.exe` (the intended final launcher)

`release/latest/` is the raw publish output used to produce the latest launcher and includes extra binaries/debug symbols.

You always get only the latest release in the special release directory; stale outputs are removed automatically.

### Optional release variants

```powershell
.\release.ps1 -Configuration Release -Runtime win-x64
.\release.ps1 -Configuration Debug -Runtime win-x64
```

## Project structure

- `src/HellCaster.App` – WPF GUI and rendering loop
- `src/HellCaster.Runtime` – game engine, entities, procedural generation, save/load persistence

## Gameplay

- Survive enemy rushes
- Shoot enemies for score
- Complete level objective and reach exit portal
- Chain checkpoints and progress through increasing level difficulty
