# CLAUDE.md — sdmay26-02 Project Guide

## Project Overview

This is a **turn-based tactical RPG/dungeon crawler** built in Unity (C#) as a senior design capstone project (SDMAY26-02). Players control characters on a 3D grid in turn-based combat, similar in structure to XCOM or Fire Emblem.

---

## Repository Structure

```
Assets/
├── DataFiles/          # JSON data for characters, classes, ancestry, items
├── Markdown/           # Project documentation (this folder)
├── Materials/          # Unity materials
├── Models/             # 3D models
├── Prefabs/            # Reusable Unity prefabs
├── Resources/          # Runtime-loaded assets
├── Scenes/             # Unity scenes (Level1, Level2, Level3, menus)
├── Scripts/            # All C# game logic (see below)
├── Settings/           # Unity project settings
├── SoundsMusic/        # Audio assets
├── Tests/              # Unity Test Framework (NUnit) tests
├── Textures/           # Texture assets
├── UI Toolkit/         # Unity UI Toolkit assets
└── UIStuff/            # UI layouts, controllers, HUD
```

### Scripts Breakdown (`Assets/Scripts/`)

| Folder | Purpose |
|---|---|
| `Grid/` | 3D grid movement, pathfinding, coordinate conversion, range indicators |
| `Grid/FSM/` | Finite state machine — Idle, Stride (move), Strike (attack) states |
| `Combat/` | Turn-based combat manager, turn queue, action controllers (player & AI), line-of-sight |
| `Creature/` | Character stats, conditions, abilities, equipment (weapons/armor), JSON serialization |
| `Decorator/` | Environmental objects — doors, obstacles |
| Misc | Audio manager, camera control, entity effects |

---

## Tech Stack

- **Engine:** Unity (C#)
- **UI:** Unity UI Toolkit + TextMesh Pro
- **Data:** JSON files for data-driven character/item definitions
- **Testing:** Unity Test Framework (NUnit-based)
- **CI/CD:** GitHub Actions

---

## Core Game Loop

1. Players create/select a character (class, ancestry, stats, equipment)
2. Combat begins on a 3D grid across 3 levels
3. Each turn a character can **Stride** (move) and **Strike** (attack)
4. Line-of-sight validation gates attacks
5. AI-controlled enemies take turns via the combat action controller
6. Conditions and effects modify creature stats dynamically

---

## Key Conventions

- Character and item definitions live in `Assets/DataFiles/` as JSON — prefer editing data there over hardcoding values in scripts
- The FSM in `Assets/Scripts/Grid/FSM/` controls all action state transitions — new actions should be added as FSM states
- UI is built with Unity UI Toolkit (`.uxml` / `.uss` files in `Assets/UIStuff/` and `Assets/UI Toolkit/`)
- Tests live in `Assets/Tests/` and run via GitHub Actions on push

---

## Active Branches of Note

- `main` — stable release branch
- `HUD_RYAN_SAVE` — HUD development work
