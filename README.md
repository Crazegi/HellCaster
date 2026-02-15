# HellCaster: Neon Siege

HellCaster is a Doom-inspired first-person 2.5D shooter built with .NET 10, WPF, and a custom raycasting runtime.

This project focuses on:

- Fast iteration in pure C# (no external game engine)
- Deterministic seeded campaign generation
- Classic corridor/room combat loop
- Lightweight release packaging for Windows

---

## 1) Feature Overview

### Core gameplay

- First-person 2.5D raycast rendering with textured walls
- Procedural level generation from campaign seed + level index
- Objective loop per level:
	- Reach kill target
	- Activate/approach exit
	- Advance to next level
- Difficulty tiers: `Easy`, `Medium`, `Hard`, `Hell`
- Enemy variants:
	- `enemy` (standard)
	- `enemy-scout` (faster, lighter)
	- `enemy-brute` (slower, heavier)

### Progression and persistence

- Autosave on checkpoints and level transitions
- Manual save/load from menu
- Leaderboard tracking by score and highest level
- Challenge + achievement state persistence

### Rendering and UX

- External wall texture support (`jpg/png/jpeg`)
- Quality presets (`Low`, `Medium`, `High`, `Ultra`)
- POV/FOV selection from settings
- Fullscreen toggle + windowed resizing/maximize support
- Mouse-look and keyboard fallback turning

---

## 2) Technology Stack

- **Runtime/UI**: .NET 10 + WPF (`net10.0-windows`)
- **Language**: C#
- **Rendering model**:
	- CPU raycasting in runtime
	- WPF canvas + bitmap compositing for scene rendering
- **Storage**: JSON files in `%LOCALAPPDATA%/HellCaster`
- **Packaging**: PowerShell release pipeline (`release.ps1`)

---

## 3) Repository Layout

- `HellCaster.slnx` — solution root
- `src/HellCaster.App` — WPF app (menu/input/render orchestration)
- `src/HellCaster.Runtime` — game engine, world simulation, generation, persistence models
- `release.ps1` — clean publish/release script
- `release/` — latest packaged output (generated)

Important app files:

- `src/HellCaster.App/MainWindow.xaml` — UI layout and menu panels
- `src/HellCaster.App/MainWindow.xaml.cs` — frame loop, rendering, menu flow, input handling
- `src/HellCaster.App/Textures/` — texture assets + texture usage README

Important runtime files:

- `src/HellCaster.Runtime/GameEngine.cs` — simulation + raycasting + snapshots
- `src/HellCaster.Runtime/LevelGenerator.cs` — procedural level generation
- `src/HellCaster.Runtime/Models.cs` — shared records/enums/settings/snapshot models
- `src/HellCaster.Runtime/PersistenceService.cs` — save/settings/leaderboard JSON I/O

---

## 4) Run Locally (Dev)

From repository root:

```powershell
dotnet build HellCaster.slnx
dotnet run --project src/HellCaster.App/HellCaster.App.csproj
```

Requirements:

- Windows with .NET SDK 10 installed

---

## 5) Controls

- `W/S` — move forward/backward
- `A/D` — strafe left/right
- `Q/E` or `Left/Right` — turn
- `Mouse` — horizontal look
- `Left Click` or `Space` — fire
- `F` — interact (e.g. advance at exit once objective complete)
- `R` — restart after game over
- `Esc` — open menu

Input behavior note:

- While menu is open, gameplay simulation/fire input is paused (UI click does not shoot).

---

## 6) Settings and Configuration

Menu settings currently include:

- Player name
- Resolution
- Quality preset
- POV/FOV value
- Fullscreen toggle
- Difficulty
- Optional campaign seed

Settings are persisted and reloaded automatically.

---

## 7) Save/State Files

Data location:

- `%LOCALAPPDATA%/HellCaster/settings.json`
- `%LOCALAPPDATA%/HellCaster/savegame.json`
- `%LOCALAPPDATA%/HellCaster/leaderboard.json`

Data categories:

- **Settings**: display, controls-related options, difficulty/profile
- **Savegame**: run snapshot (seed, level, player state, score, objectives)
- **Leaderboard**: top runs and timestamps

---

## 8) Texture System (External Images)

HellCaster supports external wall textures loaded at runtime.

Supported filenames:

- `wall_brick.jpg|png|jpeg`
- `wall_concrete.jpg|png|jpeg`
- `wall_metal.jpg|png|jpeg`

Search paths:

1. `AppBase/Textures/`
2. `AppBase/latest/Textures/`
3. `CurrentWorkingDirectory/Textures/`

Development placement:

- `src/HellCaster.App/Textures/`

Packaged release placement:

- `release/latest/Textures/`

If files are missing or invalid, built-in fallback textures are used.

Recommended texture specs:

- Square images (`128x128` or `256x256`)
- Seamless/tileable patterns for best visual continuity

---

## 9) Rendering Pipeline (Advanced)

At a high level each frame:

1. Runtime updates simulation (input, movement, bullets, enemies, objectives)
2. Runtime raycasts walls and emits a `GameSnapshot`
3. App renders:
	 - Scene base (sky/floor/walls) into a reusable bitmap layer
	 - Sprites/projectiles/UI overlays on top

Key rendering properties:

- DDA-based wall ray traversal for stable wall intersection
- Side-aware UV mapping for consistent texture attachment
- Bilinear texture sampling for smoother wall surfaces
- Proper FOV-based projection-plane scaling to avoid fake lens effects

---

## 10) Procedural Generation (Advanced)

Generation strategy combines:

- Maze carving for navigable backbone
- Corridor widening for combat flow
- Room carving with overlap constraints
- Start/exit path validation and checkpoint placement

Level determinism:

- Level seed derived from campaign seed + level index
- Same seed + difficulty combination reproduces the same level layout

---

## 11) Build and Release Workflow

### Standard release

```powershell
.\release.ps1
```

Per run, script behavior:

1. Cleans old release outputs
2. Publishes fresh self-contained Windows build
3. Produces launcher:
	 - `release/HellCaster-latest.exe`
4. Recreates clean publish folder:
	 - `release/latest/`
5. Writes metadata:
	 - `release/latest.json`

Use this executable for play:

- `release/HellCaster-latest.exe`

### Optional variants

```powershell
.\release.ps1 -Configuration Release -Runtime win-x64
.\release.ps1 -Configuration Debug -Runtime win-x64
```

---

## 12) Performance Notes

Performance-critical paths:

- Raycast count (`RayCount`) scales by quality/resolution
- Scene rendering uses a reusable bitmap to reduce WPF shape overhead
- Texture sampling uses cached downscaled arrays (`WallTextureSize` grid)

If FPS is low:

1. Lower quality preset
2. Lower resolution
3. Reduce POV/FOV (wide FOV increases visible wall complexity)

---

## 13) Troubleshooting

### Textures do not appear

- Verify filenames exactly:
	- `wall_brick.*`, `wall_concrete.*`, `wall_metal.*`
- Check files exist in `release/latest/Textures/` after `release.ps1`
- Ensure files are valid images (non-corrupt jpg/png)

### Distorted/warping wall visuals

- Confirm you are running latest executable from `release/HellCaster-latest.exe`
- Check POV/FOV is not set excessively high

### Menu click fires weapon

- Fixed in current build: gameplay input is paused while menu is visible

---

## 14) Development Workflow

Typical iteration loop:

```powershell
dotnet build HellCaster.slnx
dotnet run --project src/HellCaster.App/HellCaster.App.csproj
```

For distributable build:

```powershell
.\release.ps1
```

---

## 15) Roadmap Ideas

- Texture pack selector in settings
- Normal/specular shading for walls
- Better sprite animation system
- Audio pipeline (ambient + weapon + enemy events)
- Save-slot management instead of single save file

---

## 16) License and Usage

No explicit license file is currently included in this repository. If you plan to distribute or accept external contributions, add a license file and contribution policy.
