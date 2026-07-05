---
name: unity-coding
description: Work on this Unity 6 C# tactics project, including gameplay code, tests, asmdef-aware refactors, Unity lifecycle issues, and compile/test validation.
---

# Unity Coding

Use this skill for C# gameplay, tests, architecture refactors, compile fixes, and Unity Test Framework work in this repository.

## Workflow

1. Read `AGENTS.md`, `ProjectSettings/ProjectVersion.txt`, relevant asmdefs, and nearby code before editing.
2. Keep changes within existing runtime and test assemblies unless a new boundary is clearly needed.
3. Prefer pure, deterministic logic for combat/rules calculations and thin MonoBehaviour integration.
4. Add EditMode tests for pure rules/data behavior. Add PlayMode tests for scene, prefab, UI, lifecycle, and interaction behavior.
5. Run the narrowest useful Unity test command, then broaden when shared behavior changes.

## Guardrails

- Do not raw-edit scenes, prefabs, materials, or serialized assets for gameplay refactors.
- Avoid new global state. If existing singleton/static-event behavior is involved, isolate it behind a seam before expanding it.
- Save and restore random state in tests that touch randomness.
- Keep generated Unity files and results out of version control.

## Common Commands

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.2.1f1\Editor\Unity.exe" -batchmode -runTests -projectPath . -testPlatform editmode -testResults TestResults/EditModeResults.xml -logFile TestResults/EditMode.log
& "C:\Program Files\Unity\Hub\Editor\6000.2.1f1\Editor\Unity.exe" -batchmode -runTests -projectPath . -testPlatform playmode -testResults TestResults/PlayModeResults.xml -logFile TestResults/PlayMode.log
```

Do not pass `-quit`; the Test Framework runner exits after the run and suppresses command-line test execution when `-quit` is present.
