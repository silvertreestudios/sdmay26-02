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

1. Locate the affected JSON under `Assets/Resources/DataFiles` and the loading/mapping code in `Assets/Scripts/Creature`.
2. Locate the runtime calculation path in combat, creature, equipment, action, or condition code.
3. Add deterministic tests for rules math before refactoring broad behavior.
4. Cover PF2e-sensitive areas: degree of success, multiple attack penalty, action economy, damage dice, resistances/weaknesses, conditions, proficiency, and item bonuses.
5. Keep UI labels and player-facing text short and source-safe.
6. For work intended for a PR, use `iterative-pr-delivery` after the rules implementation and verification steps.

## Data Rules

- Prefer JSON data updates for content-defined values.
- Keep schemas compatible with existing DTOs and `Resources.Load` paths.
- If importer output is regenerated, note the source and importer command or process used.
