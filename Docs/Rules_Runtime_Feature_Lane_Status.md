# Rules Runtime Feature Lane Status

This ledger describes the integrated rules-runtime head based on target base
`0c72371ecce0034445acd0f9eed7b3aeae01d239`. It distinguishes implemented behavior from adapter
boundaries and data-only content. Catalog presence never creates executable scope.

## Implemented feature-owned rules

| Feature | Current feature authority | Retained boundary |
| --- | --- | --- |
| Stride | `StrideRules` owns action and movement resolution; movement state is rules-owned. | Route planning and exploration orchestration remain callers. Transitional bridge helpers may be removed later without changing authority. |
| Strike, reload, ammunition, and MAP | `StrikeRules` owns validation, costs, checks, damage, reload, ammunition, and MAP. | Only explicitly prepared weapons are supported. |
| Flanking | `FlankingRules` owns the pure opposite-side selector over the authoritative snapshot; `FlankingRule` captures current teams, threat, and topology for production Strike. | General team composition and topology remain narrow Unity adapters. |
| Sourced conditions and Off-Guard | `ConditionRules` owns independent valued applications; `OffGuardRules` queries Off-Guard and the retained Flat-Footed import alias. Production Strike consumes the selector and rules statistics. | Condition-specific behavior requires its own feature. |
| Slowed | `SlowedRules` owns maximum-value turn-action reduction; `UnitySlowedModule` installs its binding. | No reaction rule or speculative condition decrement was added. |
| Rotting Aura | `RottingAuraRules` owns eligibility, deterministic rolls, typed damage, health orchestration, and completion facts. | `UnityRottingAuraModule` captures authored level, traits, geometry, and logging. |
| Rage and Quick-Tempered | `RageRules` owns validation, frequency, timing, temporary HP, and cleanup. | End-on-reload normalization is intentional; authored armor/build inputs remain preparation data. |
| Spellcasting | `SpellcastingRules` owns action, slot, target, effect, and timing lifecycles. `SpellFeatureRules` composes Divine Lance, Light, Shield, Guidance, Haunting Hymn, Bless, Infuse Vitality, and Heal. Production installs only `RulesCastSpellAction`. | Unselected catalog spells are data-only. |
| Active-effect persistence | Schema-4 dungeon actor state persists registered generic effects, source identity, timing, spell slots, and sourced conditions, restoring them before enrollment. | Unsupported effect codecs fail explicitly. Rage normalization remains separate and intentional. |
| Combat Open Door | `OpenDoorRules` owns eligibility and the one-action lifecycle; its feature observer projects the committed result to the exact stable Unity door. | Exploration doors and KayKit visual/collider work remain orchestration/presentation. |
| Pre-built statistics and equipment | Preparation and unified enrollment seed immutable `CreatureStatisticsState`; checked-in Maren and Mace use the same Resources path as production. | General immunity and trait authorities are not invented for content without behavior. |
| Typed presentation and targeting snapshots | Feature observers consume committed facts; immutable target/area snapshots prevent preview drift. | Presentation is not rules authority. |

## Supported production path

`CreatureJsonConverter.CreateByName("Maren")` loads checked-in `Maren.json`, resolves `mace.json`,
prepares the cleric, and enrolls it through the same `UnityCombatantEnrollmentPipeline` used for
initial participants and reinforcements. The production bridge installs the selected spell actions,
statistics, equipment actions, slots, and feature bindings. Focused regressions prove Mace Strike
and all six selected migrated spells execute through those installed actions, not a test-only or
legacy runtime.

Dungeon reload restores registered active effects, condition applications, exact source identity,
timing, and spell slots before normal encounter enrollment. Cross-floor source absence and global
effect identity are covered; there is no parallel fallback restore path.

## Deliberately deferred or excluded

- The legacy character builder remains disconnected and excluded. Do not repair, connect, delete,
  test as a supported source, or add compatibility from its partial `PlayerCharacter` model.
- Generalized weaknesses, resistances, immunities, traits, team state, and topology are deferred
  until an executable vertical feature proves the need. Existing feature adapters remain narrow.
- The remaining spell catalog, Goblin Scuttle, Scamper, Grab, Void Healing, catalog feats and class
  features, and unsupported rule keys are data-only. They are not reported as migrated.
- Slowed reaction behavior is not implemented. Rage still intentionally normalizes on reload.
- Retained `.orig` files and commented `LineOfSight.cs` are cleanup inventory, not executable
  fallbacks.

## Verification map

| Behavior | Production regression evidence |
| --- | --- |
| Flanking and Off-Guard | `FlankingRulesTests`, `OffGuardRulesTests`, `RulesStrikeUnityTests` |
| Slowed and Rotting Aura | Slowed unit/UI tests, `Pf2eRottingAuraTests`, `RottingAuraPlayModeTests` |
| Selected spells | `SpellFeatureRulesTests`, `SpellcastingRulesTests`, `SpellcastingPresentationPlayModeTests` |
| Effect/condition persistence | `DungeonActorStateAdapterTests`, `DungeonRulesEffectPersistencePlayModeTests`, `DungeonEncounterCombatPlayModeTests` |
| Combat Open Door | Open Door runtime/observer/bridge tests and dungeon door PlayMode tests |
| Maren and Mace | `Pf2eRulesTests.MarenLoadsCompleteClericRulesFromCheckedInData` and checked-in cleric production PlayMode tests |

The exact repository inventory, load-site accounting, and row-by-row disposition are in
[Rules Runtime Migration Plan](Rules_Runtime_Migration_Plan.md). Architecture and lifetime rules
remain in [Encounter Rules Runtime Implementation Guide](Encounter_Rules_Architecture.md).
