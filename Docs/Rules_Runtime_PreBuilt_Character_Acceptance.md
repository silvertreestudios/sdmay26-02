# Rules Runtime Pre-Built Character Acceptance

## Purpose

Bulk rules-runtime migration acceptance is limited to the pre-built characters that can enter the
current game. The disconnected legacy character builder is not a supported character source and
does not block that acceptance. It may remain disconnected or deteriorate until a later rebuild
defines its product rules and an explicit handoff into the supported path.

This contract is deliberately narrower than the rules-runtime migration inventory, feature-lane
status, and contributor contracts maintained in other work. It identifies the playable boundary
and the evidence required at final integration; it does not replace those documents or claim that
unintegrated sibling work is present.

## Supported pre-built path

A character is in scope when its authored creature data already reaches gameplay through one of the
checked-in pre-built entry points:

1. `CreatureJsonConverter.CreateFromFile` loads a `Resources` creature definition, applies its DTO
   to a prefab or creature object, derives `CharacterBuild` selections, prepares supported rules
   data through `Pf2eCharacterPreparer`, and initializes runtime actions. This covers the checked-in
   player characters and catalog-materialized encounter creatures.
2. Directly placed combatant prefabs and scenes supply their serialized `CreatureComponent` data.
   `CreatureComponent.InitializeRuntimeActions` prepares a serialized build when one exists and is
   idempotent when JSON materialization already performed preparation.
3. `CombatManager` selects the initial participants or reinforcements.
   `UnityCombatantEnrollmentPipeline.Prepare` captures their complete supported rules state and
   feature installation plans. Initial participants and reinforcements both commit through
   `UnityCombatantEnrollmentPlan.Commit` and `AddCombatantsOp` before Unity authority attaches.
4. The attached `UnityCombatRulesBridge` is authoritative for migrated encounter state. Installed
   rules-backed actions and feature modules drive gameplay; the prepared character and Unity
   components remain inputs or projections only where the implementation guide documents a
   transitional capture.
5. Dungeon saves capture the supported mutable actor projections. Reload materializes the same
   pre-built party and encounter actors, calls `DungeonActorStateAdapter.PrepareRestore`, applies
   every restore before encounter enrollment, and then uses the same preparation and enrollment
   path when play resumes.

The production code remains the source of truth. The detailed authority, composition, and lifetime
rules in the [encounter implementation guide](Encounter_Rules_Architecture.md) still apply.

## Acceptance boundary

Final bulk integration must verify the following on the integrated head with Unity `6000.2.1f1`:

| Boundary | Required evidence |
| --- | --- |
| Preparation | Representative checked-in player data for both Torgrim and Lena imports its build and produces the expected prepared feature ownership. A catalog-backed encounter creature materializes with its JSON applied. |
| Enrollment | A prepared pre-built character succeeds as an initial participant and through the common reinforcement path. Rules state and installed actions remain owned by the exact attached encounter. |
| Gameplay | Representative rules-backed gameplay executes with a pre-built player character and projects committed state back to Unity. At minimum, cover one prepared feature at encounter start and one Strike path. |
| Reload | A saved run restores pre-built party and encounter actors, their supported mutable actor state, floor state, and unfinished encounter, then can resume through normal enrollment. |
| Hygiene | The current-head targeted EditMode and PlayMode tests pass, their XML root `<test-run>` totals are recorded, CSharpier reports clean, and the complete integration diff contains no generated artifacts or unintended serialized changes. |

Final integration may add equivalent or broader current-head tests, but historical reports and test
results from sibling branches are not evidence for this contract. Any failure must be traced to a
supported pre-built path before production persistence or composition is expanded.

## Explicit exclusions and retained obligations

- Do not connect, repair, delete, migrate, or test the legacy character builder for bulk migration.
  Do not add a compatibility bridge from its partial `PlayerCharacter` object to creature JSON,
  `CharacterBuild`, preparation, or enrollment. Its UI-owned calculations are outside this
  acceptance boundary.
- Excluding the builder does not exclude any checked-in pre-built player, creature JSON, combatant
  prefab, supported prepared feature, initial participant, reinforcement, or saved-run reload path.
- Shield, Guidance, Haunting Hymn, Bless, Infuse Vitality, and Heal remain required by the separately
  approved spell-migration scope. They are dormant here: this contract neither activates them nor
  removes that later obligation.
- Unsupported final-integration obligations remain required. In particular, this scope does not
  waive integration of separately owned statistics, persistence, door, feature, or caller work, and
  it does not convert known unsupported behavior into accepted behavior.
- Catalog presence alone does not make an action, feat, spell, condition, or rule executable.
  Acceptance covers only behavior actually installed for the supported pre-built path.

## Source and regression map

Use these production boundaries when diagnosing a failure:

| Stage | Primary production boundary | Focused regression evidence |
| --- | --- | --- |
| Import and preparation | `CreatureJsonConverter`, `CreatureDtoMapper`, `Pf2eCharacterPreparer`, `CreatureComponent.InitializeRuntimeActions` | `Pf2eRulesTests`, `DungeonEncounterMaterializerTests` |
| Initial and reinforcement enrollment | `CombatManager`, `UnityCombatRulesBridge`, `UnityCombatantEnrollmentPipeline`, `UnityCombatantEnrollmentPlan` | `UnityCombatRulesBridgeTests`, `Pf2eBarbarianSmokeTests` |
| Rules-backed gameplay | Feature modules installed by `UnityEncounterModuleSet` and actions attached to the exact bridge | `Pf2eBarbarianSmokeTests`, `RulesStrikeIntegrationPlayModeTests` |
| Save and reload | `DungeonAutosaveCoordinator`, `DungeonActorStateAdapter`, `DungeonRunPersistenceBootstrap.RestoreFloorRuntime` | `DungeonActorStateAdapterTests`, `DungeonProductionFlowPlayModeTests` |

A failure in the builder scene or `CharacterCreationScript` is not a regression under this
contract. A failure in any mapped pre-built boundary is in scope and must be fixed or reported with
the exact failing fixture and supported path.
