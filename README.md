[Leer en Español](README_ES.md)

# TF8Player

A modern port of **TF-8-Player** for **FortRise**. Enables up to 8-player matches in TowerFall using Harmony runtime memory patching, with no need for `Patcher.exe` or modifying the original `TowerFall.exe` binaries.

## Features

- **Up to 8 Players:** Supports 8-player Versus Free-for-All (FFA) and Team Deathmatch (P1 through P8).
- **Controller Support:** Supports up to 8 physical gamepads through the included launcher, or keyboard input for remaining slots.
- **16 Adjusted and Fixed Towers:** Based on Jonesey13's original mod levels (12 classic towers + 4 *Dark World* expansion towers), retouched and fixed to resolve broken or cramped player spawn points for a fair, balanced start.
- **Clean and Non-Invasive:** Installs as a standard FortRise mod without modifying original game files.

---

## Requirements

- TowerFall installed on PC.
- [FortRise](https://github.com/FortRise/FortRise) installed and working (v5.5.0 or higher).
- .NET SDK `10.x` *(only required if compiling from source)*.

---

## Installation

1. Ensure TowerFall is closed.
2. Copy the `TF8Player` folder into your TowerFall `Mods/` directory:
   ```text
   <TowerFall-Directory>\Mods\TF8Player
   ```
   *(Typical Steam path: `C:\Program Files (x86)\Steam\steamapps\common\TowerFall - FortRise\Mods\TF8Player`)*
3. The installed mod folder must contain:
   * `TF8PlayerFortRise.dll`
   * `meta.json`
   * `Launch-8Players.cmd`
   * `Content/` folder with the 8-player levels.

> [!IMPORTANT]
> Do not install the mod before installing FortRise in your game.

---

## How to Play (Running the Mod)

- **For 1 to 4 players:** Launch TowerFall normally via Steam or `FortRise.exe`.
- **For 5 to 8 players:** Start `Mods\TF8Player\Launch-8Players.cmd`. It defines `FNA_GAMEPAD_NUM_GAMEPADS=8` and the SDL DirectInput/RawInput switches before the process starts. If fewer than 8 gamepads are connected, remaining players can join using the keyboard.

---

## Building from Source

If you want to compile the DLL yourself:

1. Ensure the **.NET SDK 10.x** is installed.
2. Run `Compile-DLL.cmd`. The script uses the Roslyn compiler to build `TF8PlayerFortRise.dll`.
3. By default, the script looks for dependencies in:
   ```text
   C:\Program Files (x86)\Steam\steamapps\common\TowerFall - FortRise
   ```
4. If your game is installed in a different directory, define `TF8_GAME_DIR` before compiling:
   ```cmd
   set "TF8_GAME_DIR=D:\Games\TowerFall - FortRise"
   Compile-DLL.cmd
   ```

---

## Project Structure

```text
TF8Player/
├─ src/
│  ├─ TF8PlayerFortRiseModule.cs   # FortRise module entry & input hooks
│  └─ GameplayPatches.cs           # Harmony patches (menus, HUD, spawns, results)
├─ Content/
│  └─ Levels/
│     └─ Versus/                   # 16 towers with maps adapted for 8 players
├─ tools/                          # Validation tools
│  ├─ Validate-Harmony.cs
│  └─ Validate-Harmony.runtimeconfig.json
├─ Launch-8Players.cmd             # Sets eight-gamepad environment before launch
├─ Compile-DLL.cmd                 # .NET 10 compilation script
├─ TF8PlayerFortRise.csproj        # C# project file
├─ meta.json                       # FortRise mod metadata manifest
├─ README.md                       # English documentation
└─ README_ES.md                    # Spanish documentation
```

---

## Technical Details

Unlike the original mod which required a static patcher with Mono.Cecil, this port operates in memory:
- **Harmony Interception:** Dynamically hooks `MainMenu.CreateRollcall`, `MainMenu.CreateTeamSelect`, `HUD`, `VersusRoundResults`, `SessionStats`, and `TreasureSpawner` to expand native 4-player limits to 8 players.
- **Spawn Management:** When a map does not contain 8 team spawns (`TeamSpawn`), the mod automatically falls back to unassigned `PlayerSpawn` points to ensure all 8 archers spawn safely into the arena.

---

## Credits and Acknowledgments

- **[Jonesey13 / TF-8-Player](https://github.com/Jonesey13/TF-8-Player):** Author of the original 8-player TowerFall mod, whose map design and UI research served as the direct foundation and inspiration for this port. The included level maps are based on his original work, retouched and adjusted to fix specific spawn issues.
- **[FortRise](https://github.com/FortRise/FortRise):** The official community mod loader and framework for TowerFall.
