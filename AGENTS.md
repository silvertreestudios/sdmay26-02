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
dotnet tool restore
dotnet csharpier check .
dotnet csharpier format .
& "C:\Program Files\Unity\Hub\Editor\6000.2.1f1\Editor\Unity.exe" -batchmode -runTests -projectPath . -testPlatform editmode -testResults TestResults/EditModeResults.xml -logFile TestResults/EditMode.log
& "C:\Program Files\Unity\Hub\Editor\6000.2.1f1\Editor\Unity.exe" -batchmode -runTests -projectPath . -testPlatform playmode -testResults TestResults/PlayModeResults.xml -logFile TestResults/PlayMode.log
```

Run CSharpier from the checkout root. Use `format` to fix C# formatting and `check` to verify it
without writing. Install the repository hook with `pre-commit install`; it formats staged C# files
using the pinned tool version. See `Docs/CSharp_Formatting.md` for onboarding and behavior.

Do not add `-quit` to Unity Test Framework command-line runs in this project. The resolved Unity Test Framework `1.5.1` runner exits on its own and logs that tests will not work when `-quit` is specified.

Write test results outside tracked Unity asset folders. Do not commit `Library/`, `Logs/`, `Temp/`, generated `.csproj` files, generated `.sln` files, coverage output, or crash/recovery artifacts.

Use `.agent-temp/` at the checkout root for temporary body files, generated command payloads, screenshots awaiting upload, and other short-lived agent workflow artifacts. Create it on demand, clean up task-specific files when done, and avoid OS temp directories for repo workflow files unless explicitly requested.

## Unity MCP

- This repo is configured for MCP for Unity through `Packages/manifest.json` and `.codex/config.toml`.
- For Editor-backed work, open Unity `6000.2.1f1`, start the MCP for Unity HTTP server on `http://localhost:8080/mcp`, then restart Codex from a trusted checkout.
- Use Unity MCP for scene, prefab, UI, package, console, compilation, screenshot, and Unity Test Runner workflows when the Editor state matters.
- Use `manage_tools` to activate only the tool groups needed for the task, and keep mutating/build/package/asset-generation tools approval-gated.
- After any MCP mutation, inspect `git status` and `git diff`; Unity may dirty serialized files or package locks unexpectedly.
- Use `UNITY_MCP.md` for setup, troubleshooting, and the evaluation checklist. Keep batchmode test commands as the CI-parity fallback.

## Git Workflow

- Do not make task changes directly in the main repository checkout.
- Create a local Git worktree for each issue, PR, or task branch.
- Keep worktrees under the sibling folder `../sdmay26-02-worktrees/`.
- Use descriptive worktree names tied to the task, for example `../sdmay26-02-worktrees/issue-62-tokenmesh-log-noise`.
- Create task branches from the intended base branch inside the worktree.
- When assigned a GitHub issue that requires implementation work, follow `.agents/skills/iterative-pr-delivery` together with the applicable domain skills.
- When implementation is complete and verified for a GitHub issue, push the task branch and create a draft PR linked to the issue.
- Delete task worktrees after the PR is merged, closed, or the work is abandoned.
- Before deleting a worktree, verify there are no uncommitted changes that should be preserved.
- Never delete another user's worktree or branch without explicit approval.

## Coding

- Follow existing C# style and Unity lifecycle patterns.
- Keep every new or modified C# file compliant with the repository-pinned CSharpier version. Run
  `dotnet csharpier format .` before tests and `dotnet csharpier check .` before handoff.
- Treat setting, passing, returning, or defaulting to `null` as a design smell. Prefer required dependencies, explicit construction paths, distinct types or states, empty collections, and Null Object implementations. Use `null` only at unavoidable framework or interop boundaries, or when absence is genuinely part of the domain and no clearer representation is practical; keep that boundary narrow and document the reason.
- Follow Microsoft's C# XML documentation comment conventions for new C# APIs. Add XML documentation to every new public type and every non-trivial public or protected constructor, method, property, event, and field; use `<inheritdoc/>` when an inherited contract already explains the member accurately.
- Document complex internal code where intent, invariants, ownership, lifecycle, concurrency, side effects, failure behavior, or other non-obvious constraints would otherwise be difficult to recover from the implementation.
- Write documentation for junior contributors: explain why the API exists, how it should be used, and the important guarantees or hazards without merely restating names or implementation steps. Keep shared concepts DRY by documenting them once and linking with `<see>`, `<seealso>`, or `<inheritdoc/>`.
- Keep code comments self-contained. Do not reference GitHub issues, pull requests, review threads, or other transient discussions. Link only to stable public documentation or rules references, such as Unity documentation or Archives of Nethys, or to repository-committed code and design documentation; still summarize the relevant contract locally.
- Update nearby XML documentation whenever existing code changes make it incomplete, inaccurate, or misleading.
- Keep gameplay code in the existing assembly boundaries: `MainGameAssembly`, `EditModeAssembly`, and `PlayModeAssembly`.
- Prefer small vertical changes with targeted EditMode tests first, then PlayMode smoke coverage for scene, UI, or MonoBehaviour behavior.
- Default each rule, feat, spell, and action to a cohesive feature-owned module. That module should
  own its feature-specific operations, validation, handlers, listeners, selectors, persistent state,
  and Unity data extraction or presentation adapters. Feature ownership does not require putting all
  of those responsibilities in one class.
- Keep shared rules runtime, bridge, manager, and facade APIs feature-agnostic. A composition root may
  name a feature to register its definitions or seed its bindings, but general-purpose classes must
  not implement the feature's conditions or workflow. Avoid feature-named methods, fields, caches,
  trigger flags, and switches when the feature can construct a generic operation, listen to a generic
  Fact, or query its own selector.
- Add horizontal/shared infrastructure only when the current vertical slice proves it necessary, and
  keep that API free of feature terminology. See `Docs/Ops_Based_Rules_Proposal.md`, especially
  "Feature modules own feature semantics."
- Avoid introducing new singleton/static-event coupling. When refactoring combat or rules logic, add testable seams for dice/randomness, data loading, and combat math.
- Keep PF2e calculations deterministic in tests. Save and restore Unity random state if a test touches random behavior.
- During active development, do not add compatibility layers, schema/data version dispatch, or migrations for unshipped formats. Make coordinated breaking changes to code, data, fixtures, and tests unless a human explicitly requests compatibility.

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

- For `/review`, `codex review`, and agentic pull-request reviews, read `.agents/review/code_review.md` in addition to these repository instructions.
- End substantial code changes with targeted tests or a clear note explaining why tests could not be run.
- For scene, prefab, UI, level, or art changes, verify in the Unity Editor or with PlayMode/screenshots before considering the change done.
- For any visual change, including UI, models, scenes, levels, materials, VFX, animation, or art assets, include one or more real full Game View or Unity Editor screenshots in the PR description. For gameplay or HUD/UI changes, prefer full Game View screenshots that show the complete screen framing, not cropped UI-panel renders or edge-clipped captures. Do not use fake screenshots, generated mockups, hand-drawn renderings, or programmatic stand-ins as PR visual evidence. Before attaching screenshots, inspect them carefully and confirm they clearly show the feature, behavior, or visual change the PR is meant to demonstrate. Never commit PR screenshot artifacts to the branch; upload them to a GitHub gist and embed/link the gist-hosted artifact in the PR description.
- Review Unity serialized diffs carefully for unintended scene or prefab churn.
- Do not open follow-up GitHub Issues without explicit human approval. Record deferred work in the PR on the applicable code when possible so the approved reviewer team can decide its disposition.
