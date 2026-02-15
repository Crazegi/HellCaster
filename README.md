<div align="center">

# 🔥 HellCaster: Neon Siege

### Retro Doom-style action shooter built with .NET 10 + WPF + custom raycasting

<p>
	<img src="https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 10" />
	<img src="https://img.shields.io/badge/UI-WPF-0C54C2?style=for-the-badge&logo=windows&logoColor=white" alt="WPF" />
	<img src="https://img.shields.io/badge/Platform-Windows-0078D6?style=for-the-badge&logo=windows11&logoColor=white" alt="Windows" />
	<img src="https://img.shields.io/badge/Renderer-2.5D%20Raycaster-EA4AAA?style=for-the-badge" alt="Raycaster" />
	<img src="https://img.shields.io/badge/Status-Playable-2EA043?style=for-the-badge" alt="Playable" />
</p>

<p>
	<a href="https://github.com/Crazegi/HellCaster/releases"><img src="https://img.shields.io/github/v/release/Crazegi/HellCaster?style=for-the-badge&label=Latest%20Release" alt="Latest Release" /></a>
	<a href="https://github.com/Crazegi/HellCaster/releases"><img src="https://img.shields.io/github/downloads/Crazegi/HellCaster/total?style=for-the-badge&label=Downloads" alt="Downloads" /></a>
	<a href="https://github.com/Crazegi/HellCaster"><img src="https://img.shields.io/github/last-commit/Crazegi/HellCaster?style=for-the-badge&label=Last%20Commit" alt="Last Commit" /></a>
	<img src="https://img.shields.io/badge/License-GPLv3-blue?style=for-the-badge" alt="GPLv3" />
</p>

</div>

---

## ✨ Why HellCaster?

HellCaster is a custom-built first-person shooter that delivers classic corridor combat vibes with modern convenience systems.

- ⚡ **Fast runtime** with optimized bitmap scene rendering
- 🧱 **Real wall textures** from external JPG/PNG assets
- 🧠 **Seeded procedural levels** with rooms, corridors, checkpoints, and objective flow
- 💾 **Persistent saves, settings, leaderboard, achievements/challenges**
- 🛠️ **One-command release pipeline** producing clean latest builds

---

## 🚀 Quick Start

### 1) Build + run (development)

```powershell
dotnet build HellCaster.slnx
dotnet run --project src/HellCaster.App/HellCaster.App.csproj
```

### 2) Build distributable release

```powershell
.\release.ps1
```

### 3) Launch the game

- ✅ Use: `release/HellCaster-latest.exe`
- 📦 Raw publish output: `release/latest/`

---

## 🎮 Controls

| Action | Input |
|---|---|
| Move forward/backward | `W / S` |
| Strafe left/right | `A / D` |
| Turn | `Q / E` or `← / →` |
| Mouse look | `Mouse X` |
| Shoot | `Left Click` or `Space` |
| Interact/advance exit | `F` |
| Restart after death | `R` |
| Open menu | `Esc` |

> 🛡️ Menu safety: when menu is open, world update/fire input is paused.

---

## 🧩 Feature Matrix

| Category | Included |
|---|---|
| Rendering | DDA raycasting, side-aware UVs, bilinear wall sampling, projection-plane scaling |
| Gameplay | Enemy variants, projectile combat, objective+exit progression |
| World Gen | Seeded maze+rooms, corridor widening, checkpoint pathing |
| Progression | Difficulty tiers, level advancement, score tracking |
| Persistence | Save/load, autosave, settings, leaderboard, achievements/challenges |
| UX | Fullscreen/windowed, resize+maximize, quality presets, POV/FOV setting |

---

## 🧰 Tech Stack Cards

<table>
	<tr>
		<td align="center" width="25%"><strong>🟣 .NET 10</strong><br/>Runtime and tooling foundation</td>
		<td align="center" width="25%"><strong>🪟 WPF</strong><br/>Desktop UI, menu flow, overlays</td>
		<td align="center" width="25%"><strong>🎯 Custom Raycaster</strong><br/>DDA walls + UV mapping + projection</td>
		<td align="center" width="25%"><strong>⚙️ PowerShell Release</strong><br/>Single-command clean packaging</td>
	</tr>
	<tr>
		<td align="center"><strong>🧠 Engine Core</strong><br/>Simulation loop, AI, combat, progression</td>
		<td align="center"><strong>🧱 Texture Loader</strong><br/>External JPG/PNG wall materials</td>
		<td align="center"><strong>💾 JSON Persistence</strong><br/>Save/settings/leaderboard storage</td>
		<td align="center"><strong>🗺️ ProcGen</strong><br/>Seeded rooms, corridors, checkpoints</td>
	</tr>
</table>

---

## 🧱 Texture Pipeline (External Assets)

Drop texture files using these names:

- `wall_brick.jpg` (or `.png` / `.jpeg`)
- `wall_concrete.jpg` (or `.png` / `.jpeg`)
- `wall_metal.jpg` (or `.png` / `.jpeg`)

### Search order

1. `AppBase/Textures/`
2. `AppBase/latest/Textures/`
3. `CurrentWorkingDirectory/Textures/`

### Recommended texture format

- Square and tileable textures (best: `128x128` or `256x256`)
- Strong contrast for retro look
- Avoid perspective photos (flat wall scans are ideal)

If an image is missing or invalid, built-in fallback textures are used.

---

## 🛠️ Settings

From menu:

- 👤 Player Name
- 🖥️ Resolution
- 🎚️ Quality (`Low`, `Medium`, `High`, `Ultra`)
- 👁️ POV/FOV
- 🪟 Fullscreen toggle
- 💀 Difficulty (`Easy`, `Medium`, `Hard`, `Hell`)
- 🔢 Optional campaign seed

---

## 🧠 Architecture (Advanced)

### Project structure

- `src/HellCaster.App` → WPF app, input loop, UI, render composition
- `src/HellCaster.Runtime` → engine simulation, raycasting, level generation, models/persistence services

### Frame flow

1. Read input and update simulation
2. Runtime raycasts walls and emits `GameSnapshot`
3. App draws scene bitmap (sky/floor/walls)
4. App overlays sprites/projectiles/HUD/effects

### Key engine techniques

- DDA grid traversal for stable wall intersections
- Side-aware texture coordinate mapping
- Proper projection-plane distance from FOV
- Snapshot-based decoupling between engine and renderer

---

## 💾 Data Persistence

Stored under:

- `%LOCALAPPDATA%/HellCaster/settings.json`
- `%LOCALAPPDATA%/HellCaster/savegame.json`
- `%LOCALAPPDATA%/HellCaster/leaderboard.json`

---

## 📦 Release Workflow

Run:

```powershell
.\release.ps1
```

What it does:

1. Cleans previous release outputs
2. Publishes fresh self-contained build
3. Outputs launcher: `release/HellCaster-latest.exe`
4. Recreates clean raw folder: `release/latest/`
5. Writes build metadata: `release/latest.json`

Optional variants:

```powershell
.\release.ps1 -Configuration Release -Runtime win-x64
.\release.ps1 -Configuration Debug -Runtime win-x64
```

---

## ⚡ Performance Tips

- Lower quality first, then resolution
- Lower POV/FOV if scene feels too heavy
- Keep textures tileable and moderate resolution

---

## 🧯 Troubleshooting

<details>
<summary><strong>Textures not visible</strong></summary>

- Confirm filenames exactly (`wall_brick.*`, `wall_concrete.*`, `wall_metal.*`)
- Verify files exist in `release/latest/Textures/` after release build
- Ensure image files open normally (not corrupted)

</details>

<details>
<summary><strong>Visual distortion near edges</strong></summary>

- Reduce POV/FOV in settings
- Ensure latest executable is used: `release/HellCaster-latest.exe`

</details>

<details>
<summary><strong>Menu clicks triggering gameplay input</strong></summary>

- Current build blocks world updates and fire while menu is visible

</details>

---

## 🗺️ Roadmap

- 🎨 Texture pack selector in settings
- 🔊 Audio pipeline (SFX + ambience)
- 🧍 Better sprite animation / enemy feedback
- 🧪 Optional renderer backend for fully polygonal 3D

---

## 📄 License

This project is licensed under the **GNU General Public License v3.0 (GPL-3.0)**.
