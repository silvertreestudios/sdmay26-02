# Project Code Review Guidance

This guidance augments Codex's built-in `/review` and `codex review` behavior. Do not replace the built-in reviewer with a standalone review prompt or repository skill.

## Review contract

- Review the complete diff from the intended base branch or merge base to the exact head SHA. State both SHAs; any later code change invalidates the review.
- Keep the review session independent and non-mutating. Read the task, acceptance criteria, `AGENTS.md`, and any domain skill relevant to the changed files before judging the implementation.
- Distinguish verified behavior from assumptions. Record commands and evidence inspected, and state any verification that could not be performed.
- Report only concrete, actionable defects caused or exposed by the change. Include the smallest useful file/line location, reachable evidence, concrete impact, and the property a correct fix must preserve.
- If no actionable finding remains, state `No actionable findings for <head-sha>.`

## Unity runtime risks

When runtime C# changes, inspect the affected call sites and test for project-specific failure modes:

- Unity lifecycle ordering across `Awake`, `OnEnable`, `Start`, `OnDisable`, and `OnDestroy`; disabled components; destroyed-object pseudo-null behavior; and scene reloads.
- Event, delegate, coroutine, task, and static-state ownership, including symmetric subscription and cleanup and duplicate registration after re-enable.
- Frame, turn, animation, physics, and coroutine timing; stale selections or action state; and callbacks that can fire after their owner is disabled or destroyed.
- Grid coordinate conversion, 3D pathfinding, range indicators, occupancy, movement costs, and reachable versus displayed cells.
- FSM transitions for Idle, Stride, Strike, and cancellation; action-economy consumption; turn advancement; line-of-sight gates; teams; and AI action completion.
- Assembly boundaries among `MainGameAssembly`, `EditModeAssembly`, and `PlayModeAssembly`, including runtime code that accidentally depends on test or editor-only APIs.

## Rules and data risks

When PF2e behavior or runtime data changes, inspect:

- Degree-of-success math, multiple attack penalty, action costs and timing, damage dice, resistances and weaknesses, conditions, proficiency, item bonuses, and effect duration.
- Determinism and controllable randomness in tests, including saving and restoring Unity random state.
- JSON shape, DTO conversion, defaults, missing or malformed fields, `Resources.Load` paths, data-driven versus hardcoded content, and compatibility with existing data files.
- ORC provenance and the boundary between open rules mechanics and protected lore, prose, art, or trade dress.

## UI, scenes, and assets

When UI or serialized Unity content changes, inspect:

- UI Toolkit element names, queries, event registration and cleanup, focus/input behavior, scaling, and complete-screen layout at the supported resolutions.
- Scene, prefab, material, asset, and `.meta` changes for unintended YAML churn, broken GUIDs or references, lost overrides, duplicate objects, and import-setting changes.
- Whether serialized mutations were made through Unity Editor automation or an understood editor script rather than unsafe hand edits.
- Whether real full Game View or Unity Editor screenshots clearly demonstrate every visual change and remain outside the branch.

## Verification expectations

- Map changed production code to targeted EditMode tests first. Require PlayMode coverage for scene wiring, lifecycle, UI, coroutine/frame behavior, or other Editor-backed behavior.
- Use Unity `6000.2.1f1` and the commands in `AGENTS.md`; never add `-quit` to this project's Unity Test Framework runs.
- Keep test outputs outside tracked asset folders. Inspect Unity console or compilation evidence when Editor state matters.
- Inspect `git status`, the complete diff, generated files, and serialized diffs. Treat green CI or narrow tests as supporting evidence, not proof of unrelated behavior.
- For visual changes, verify that the PR body contains the required real screenshots and that they show the full feature without clipping or misleading framing.

## Review handoff

Return prioritized findings first, followed by the reviewed base/head SHAs and verification limitations. Do not implement fixes in the review session.
