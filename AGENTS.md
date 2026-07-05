# sdmay26-02 Codex Guide

## Project

- Unity project for a turn-based Pathfinder-style tactics game, built as a senior design capstone project.
- Players control characters on a 3D grid in turn-based combat, similar structurally to XCOM or Fire Emblem.
- Unity editor version: `6000.2.1f1` from `ProjectSettings/ProjectVersion.txt`.
- Runtime code lives mainly in `Assets/Scripts`.
- Runtime JSON data loaded through `Resources` lives in `Assets/Resources/DataFiles`.
- UI uses UI Toolkit assets in `Assets/UIStuff` and `Assets/UI Toolkit`.
- Tests use Unity Test Framework assemblies under `Assets/Tests/EditMode` and `Assets/Tests/PlayMode`.

## Repository Map

- `Assets/Scripts/Grid`: 3D grid movement, pathfinding, coordinate conversion, and range indicators.
- `Assets/Scripts/Grid/FSM` and `Assets/Scripts/Grid/States`: action-state flow such as Idle, Stride, and Strike.
- `Assets/Scripts/Combat`: turn management, action controllers, teams, and line of sight.
- `Assets/Scripts/Creature`: stats, conditions, abilities, equipment, portraits, tokens, and JSON conversion/loading.
- `Assets/Scripts/Decorator`: environmental objects such as doors and obstacles.
- `Assets/Scenes`: menu, character creation, level, homebase, and test scenes.
- `Assets/Prefabs`, `Assets/Materials`, `Assets/Models`, `Assets/Textures`, and `Assets/SoundsMusic`: Unity content assets.

## Game Flow

1. Players create or select a character.
2. Combat begins on a 3D grid across level scenes.
3. Each turn a character can Stride, Strike, and use implemented actions.
4. Line-of-sight validation gates attacks.
5. AI-controlled enemies act through combat action controllers.
6. Conditions, effects, equipment, and creature stats modify runtime behavior.

## Local Commands

Use the installed Unity editor for this exact version when possible.

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.2.1f1\Editor\Unity.exe" -batchmode -runTests -projectPath . -testPlatform editmode -testResults TestResults/EditModeResults.xml -logFile TestResults/EditMode.log
& "C:\Program Files\Unity\Hub\Editor\6000.2.1f1\Editor\Unity.exe" -batchmode -runTests -projectPath . -testPlatform playmode -testResults TestResults/PlayModeResults.xml -logFile TestResults/PlayMode.log
```

Do not add `-quit` to Unity Test Framework command-line runs in this project. The resolved Unity Test Framework `1.5.1` runner exits on its own and logs that tests will not work when `-quit` is specified.

Write test results outside tracked Unity asset folders. Do not commit `Library/`, `Logs/`, `Temp/`, generated `.csproj` files, generated `.sln` files, coverage output, or crash/recovery artifacts.

## Coding

- Follow existing C# style and Unity lifecycle patterns.
- Keep gameplay code in the existing assembly boundaries: `MainGameAssembly`, `EditModeAssembly`, and `PlayModeAssembly`.
- Prefer small vertical changes with targeted EditMode tests first, then PlayMode smoke coverage for scene, UI, or MonoBehaviour behavior.
- Avoid introducing new singleton/static-event coupling. When refactoring combat or rules logic, add testable seams for dice/randomness, data loading, and combat math.
- Keep PF2e calculations deterministic in tests. Save and restore Unity random state if a test touches random behavior.

## Data And Rules

- Prefer data-driven changes in JSON over hardcoded gameplay constants when the behavior is content-defined.
- Validate JSON shape against the project DTO/loading code before adding new data.
- Treat Archives of Nethys and ORC-licensed Paizo rules text as rules references, not as permission to import protected lore, setting prose, art, or trade dress.
- Keep license provenance explicit for imported or adapted rules content. This repo includes `ORCLicense.md`; do not add non-open Pathfinder content without approval.

## Unity Assets

- Do not hand-edit `.unity`, `.prefab`, `.asset`, `.mat`, or `.meta` YAML except for narrow, text-only metadata changes that are understood and reviewed.
- Use Unity Editor automation, Unity MCP, or explicit batchmode editor scripts for scene, prefab, level, import-setting, and visual asset work.
- Use Unity Smart Merge (`UnityYAMLMerge`) for serialized Unity files.
- Binary and large assets should be tracked with Git LFS when enabled by `.gitattributes`.

## Review Expectations

- End substantial code changes with targeted tests or a clear note explaining why tests could not be run.
- For scene, prefab, UI, level, or art changes, verify in the Unity Editor or with PlayMode/screenshots before considering the change done.
- Review Unity serialized diffs carefully for unintended scene or prefab churn.
- Capture deferred follow-up work as GitHub Issues using the repo issue label schema in `.agents/skills/gh-issue-capture`.
