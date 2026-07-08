# PF2e Class and Rule Preparation

Imported PF2e item JSON under `Assets/Resources/DataFiles` is treated as source data. Keep FoundryVTT PF2e files verbatim where practical so future imports can diff and refresh them cleanly. Unity-owned data belongs outside imported item JSON: saved character choices live in `CharacterBuild`, runtime state lives in `PreparedCharacter`, and provenance/import notes live in docs or sidecar files outside `Resources`.

## Preparation Flow

Player creature JSON is still loaded into `CreatureComponent` for the existing combat fields. When `playerOnlyStuff` contains a class build, the loader also creates a `CharacterBuild` and prepares a `PreparedCharacter`:

1. Reset prepared state.
2. Resolve the selected class from `DataFiles/classes`.
3. Apply class base math from the class item.
4. Grant class items from `system.items` up to the creature level.
5. Add selected subclass and class feat items from saved build choices.
6. Resolve `ChoiceSet` and `GrantItem` rules into owned items.
7. Collect typed rule synthetics for combat evaluation.

Legacy `passives` callbacks remain only for creatures without a player build, so monster imports are not broken by the player-class pipeline.

## Supported Rule Elements

The first implementation supports the subset needed for level-1 barbarian behavior:

- `FlatModifier` for selector-based numeric bonuses such as Rage damage.
- `AdjustModifier` for slug-targeted upgrades and multipliers such as Fury Instinct and agile Rage damage.
- `ChoiceSet` for persisted build selections.
- `GrantItem` for adding owned items while preserving the granting item slug.
- `ItemAlteration` for conditional trait changes such as Raging Intimidation adding `rage`.
- `RollOption` for feature/effect flags.
- `TempHP` through source-tracked Rage temp HP runtime state.
- `Resistance` is accepted as schema data but not applied to damage yet.

Unsupported rule keys are collected on `PreparedCharacter.UnsupportedRuleKeys` and ignored rather than removed from source JSON.

## Predicates

Predicates support atomic roll options, `and`, `or`, `not`, and numeric `gte`. The implemented atoms cover current barbarian data, including class, feat, feature, effect, item trait, ranged/thrown, armor category, and skill-rank checks.

## Barbarian V1

Rage applies active effect state and source-tracked temporary HP. Strike damage is evaluated from prepared rule modifiers instead of static `OnStrikeEvent` listeners. Quick-Tempered runs from combat-start timing after initiative is rolled. Fury Instinct defaults Torgrim's bonus level-1 barbarian feat selection to Raging Intimidation when saved build data does not override it.

## Tests

Rules changes should prefer deterministic EditMode tests for catalog resolution, preparation, predicates, rule grants, active effects, temp HP, and damage modifiers. Use PlayMode smoke tests for scene/combat timing such as Quick-Tempered.

Run Unity tests with the project Unity version and do not pass `-quit`.