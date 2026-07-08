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

The implementation supports the subset needed for the current level-1 Barbarian and Thief Rogue slices:

- `FlatModifier` for selector-based numeric bonuses such as Rage damage and Thief finesse melee damage.
- `AdjustModifier` for slug-targeted upgrades and multipliers such as Fury Instinct and agile Rage damage.
- `ChoiceSet` for persisted build selections.
- `GrantItem` for adding owned items while preserving the granting item slug.
- `ItemAlteration` for conditional trait and other-tag changes such as Raging Intimidation adding `rage` and Sneak Attack tagging eligible weapons.
- `RollOption` for non-toggle feature/effect flags. Toggleable and target-scoped options are not enabled automatically.
- `TempHP` through source-tracked Rage temp HP runtime state.
- `DamageDice` for strike-damage dice such as Rogue Sneak Attack precision damage.
- `ActiveEffectLike` for the supported skill-rank and numeric actor-flag paths used by the current class feature data.
- `Resistance` is accepted as schema data but not applied to damage yet.

Unsupported rule keys are collected on `PreparedCharacter.UnsupportedRuleKeys` and ignored rather than removed from source JSON.

## Predicates

Predicates support atomic roll options, `and`, `or`, `not`, and numeric `gte`. The implemented atoms cover current Barbarian and Rogue data, including class, feat, feature, effect, item trait, item tag, ranged/thrown, armor category, target condition, and skill-rank checks.

## Barbarian V1

Rage applies active effect state and source-tracked temporary HP. Strike damage is evaluated from prepared rule modifiers instead of static `OnStrikeEvent` listeners. Quick-Tempered runs from combat-start timing after initiative is rolled. Fury Instinct defaults Torgrim's bonus level-1 barbarian feat selection to Raging Intimidation when saved build data does not override it.

## Rogue V1

The first Rogue slice supports a level-1 Thief Rogue. Rogue's Racket resolves saved `roguesRacket` selections or defaults from `CharacterBuild.SubclassName`. Sneak Attack adds 1d6 precision damage only for eligible Strikes against targets with Off-Guard condition state. Off-Guard AC penalties are resolved by the shared `Conditions` modifier provider, while this pipeline only consumes target condition options for damage predicates and maps legacy `Flat-Footed`/`OffGuard` condition names to the same target option. Thief finesse melee Strikes use Dexterity for base ability damage. Surprise Attack and level-1 Rogue class feats are imported as catalog/build data, but their custom action, reaction, and ephemeral-effect behavior is deferred.

## Tests

Rules changes should prefer deterministic EditMode tests for catalog resolution, preparation, predicates, rule grants, active effects, temp HP, and damage modifiers. Use PlayMode smoke tests for scene/combat timing such as Quick-Tempered.

Run Unity tests with the project Unity version and do not pass `-quit`.
