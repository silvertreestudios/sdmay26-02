---
name: pf2e-rules
description: Implement, validate, or review Pathfinder 2e rules, data files, combat math, character options, actions, conditions, equipment, and license provenance in this Unity tactics project.
---

# PF2e Rules

Use this skill when changing Pathfinder 2e rules behavior or data in this repository.

## Source Policy

- Use project JSON and code as the local source of truth for implemented behavior.
- Validate rules against Archives of Nethys or ORC-licensed Paizo rules sources when current rules matter.
- Keep license provenance explicit. Do not add protected setting prose, lore, art, trade dress, or non-open text without approval.
- Prefer compact factual data and original summaries over copied rules text.

## Implementation Workflow

For encounter, action, active-effect, bridge, or combatant-enrollment work, first read the canonical
as-built `Docs/Encounter_Rules_Architecture.md` and the durable design constraints in
`Docs/Rules_Runtime_Design.md`.

1. Locate the affected JSON under `Assets/Resources/DataFiles` and the loading/mapping code in `Assets/Scripts/Creature`.
2. Locate the runtime calculation path in combat, creature, equipment, action, or condition code.
3. Identify the cohesive feature module that should own the rule-specific operations, validation,
   handlers, listeners, selectors, state, and Unity adapter code. Follow the boundary in `AGENTS.md`
   and `Docs/Encounter_Rules_Architecture.md`.
4. Keep new shared engine, bridge, manager, and facade APIs feature-agnostic. Existing
   Stride-specific `UnityCombatRulesBridge` fields and helpers are a transitional first-slice
   exception; do not copy or expand them. Prefer publishing generic timing Facts or dispatching
   generic Ops so the feature module can decide how its rule responds.
5. Add horizontal infrastructure only when the current vertical slice requires it, and keep new
   shared APIs free of feature terminology.
6. For encounter features, explicitly compose every feature-used `RuleDefinitionId` and required
   action profile or typed catalog in `UnityEncounterModuleSet.Create` before dispatcher build, then
   register the module once. Support both initial seed and reinforcement enrollment, transfer every
   encounter-scoped module observer/resource to the encounter `CompositeLifetime`, keep root-scoped
   temporary registrations locally owned until that root ends, and preserve exact bridge identity
   at cleanup.
7. Never introduce legacy fallback or dual authority for a migrated state slice, static discovery
   or self-registration, manual cleanup of encounter-scoped module observers/resources, or new
   feature semantics in shared bridges.
8. Add deterministic tests for rules math before refactoring broad behavior.
9. Cover PF2e-sensitive areas: degree of success, multiple attack penalty, action economy, damage dice, resistances/weaknesses, conditions, proficiency, and item bonuses.
10. Keep UI labels and player-facing text short and source-safe.

## Data Rules

- Prefer JSON data updates for content-defined values.
- Keep schemas compatible with existing DTOs and `Resources.Load` paths.
- If importer output is regenerated, note the source and importer command or process used.
