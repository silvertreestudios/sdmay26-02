# Rules Runtime Pre-Built Character Acceptance

## Purpose

Bulk rules-runtime acceptance covers the pre-built characters that can enter the current game. The
disconnected legacy character builder is not a supported character source and does not block
acceptance. Do not repair, connect, delete, or add compatibility for it without a separately
approved product design.

This contract identifies the playable boundary and its integrated-head evidence. The production
code remains authoritative, and the lifetime/composition rules in
[Encounter Rules Runtime Implementation Guide](Encounter_Rules_Architecture.md) still apply.

## Supported path

1. `CreatureJsonConverter.CreateFromFile` and `CreateByName` load a checked-in `Resources` creature
   definition, apply its DTO, derive `CharacterBuild`, prepare supported rules data through
   `Pf2eCharacterPreparer`, and initialize runtime actions. This covers Torgrim, Lena, Maren, and
   catalog-materialized encounter creatures.
2. Directly placed combatant prefabs and scenes use the same idempotent preparation path.
3. `CombatManager` selects initial participants and reinforcements.
   `UnityCombatantEnrollmentPipeline.Prepare` captures complete supported state and installation
   plans. Both paths commit through `AddCombatantsOp` before Unity authority attaches.
4. The exact attached `UnityCombatRulesBridge` owns migrated encounter state. Feature actions and
   modules drive gameplay; prepared data and Unity components are inputs/projections only at the
   documented adapter boundaries.
5. Dungeon saves restore supported mutable actor state, registered effects, sourced conditions,
   timing, and spell slots before the same preparation and enrollment path resumes play.

## Integrated acceptance ledger

| Boundary | Production evidence | Current regression evidence | Status |
| --- | --- | --- | --- |
| Resources import and preparation | Checked-in Torgrim, Lena, and Maren creature JSON; 13 equipment definitions including Mace | `Pf2eRulesTests`, especially `MarenLoadsCompleteClericRulesFromCheckedInData`; `DungeonEncounterMaterializerTests` | Complete |
| Initial and reinforcement enrollment | Unified preparation plan and `AddCombatantsOp` commit | `UnityCombatRulesBridgeTests`, enrollment tests, barbarian smoke tests | Complete |
| Rules-backed gameplay | Feature modules installed by `UnityEncounterModuleSet` on the exact bridge | Strike integration and checked-in cleric PlayMode tests | Complete for supported features |
| Maren/Mace | `Maren.json` resolves `mace.json`, prepared statistics, Strike, slots, and spell definitions | `CheckedInClericEnrollsCompleteRulesAndMaceStrike` | Complete |
| Six selected cleric spells | Shield, Guidance, Haunting Hymn, Bless, Infuse Vitality, and Heal install only as `RulesCastSpellAction` | `CheckedInMarenCastsAllSixMigratedSpellsThroughProductionActions` plus `SpellFeatureRulesTests` | Complete for selected variants |
| Save and reload | Schema-4 actor state restores before enrollment | `DungeonActorStateAdapterTests`, `DungeonRulesEffectPersistencePlayModeTests`, `DungeonEncounterCombatPlayModeTests`, production-flow PlayMode tests | Complete for registered codecs |
| Legacy character builder | Disconnected UI-owned model under `Assets/Resources/Data` | Exclusion documented here and in the migration ledger | Excluded, not complete or deferred work in this acceptance |
| Unsupported catalog content | No executable authority | Load/catalog tests only | Data-only/deferred, not accepted as migrated |

## Required verification

Acceptance requires current integrated-head CSharpier format/check, focused cross-module tests, and
fresh full Unity `6000.2.1f1` EditMode and PlayMode suites without `-quit`. Historical or sibling
reports are not evidence. The implementation handoff records exact XML totals, logs, commit
identity, ancestry, and status/diff checks.

A builder-scene or `CharacterCreationScript` failure is outside this contract. A failure in any
supported pre-built import, preparation, enrollment, gameplay, or restore boundary is in scope and
must be fixed or reported against its exact fixture.
