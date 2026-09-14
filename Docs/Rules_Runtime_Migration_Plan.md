# Rules Runtime Migration Plan

## Purpose and authority

This document maps the functionality present at repository base
`f5b4755fabebc2fc6b7e9d3eab1376e0731d60a4` to the operations-based rules runtime.
It is an implementation inventory and migration schedule, not a promise to implement additional
Pathfinder content. The production code and data remain the source of truth when this plan becomes
stale. Architectural constraints come from [Rules Runtime Design](Rules_Runtime_Design.md), while
the current composition and lifetime contract comes from
[Encounter Rules Runtime Implementation Guide](Encounter_Rules_Architecture.md).

The inventory uses these classifications:

- **Rules behavior** decides legality, values, costs, outcomes, or persistent game state.
- **Presentation** displays committed results through animation, logs, UI, highlights, audio, or
  transforms. Presentation does not become rules authority.
- **Infrastructure** supplies typed dispatch, immutable state, deterministic ordering, ownership,
  topology, persistence, or loading without deciding a named rule.
- **Orchestration** selects participants, modes, or workflows and calls rules entry points.
- **Data-only/unimplemented** is authored content which is loadable or displayed but has no complete
  executable behavior. Its presence must not create a runtime contract.

"Migrated" means that `RulesState` is the only writable authority for that state during an attached
encounter. "Transitional" means a rules operation calls a Unity-backed adapter or captures Unity
state. A populated generic state type is not assumed merely because its slice exists.

## Reproducible inventory and completeness

The inventory was generated from the checkout root with the following read-only shape. The ordered
sets cover all authored player-runtime C#, its production assembly definitions and retained source
artifacts, authored data/presentation assets loaded through `Resources` or serialized references,
rule-seeding combatant prefabs and their scene entry points, and both test roots. `.meta` files are
excluded from counts and hashes, but their GUIDs are read to resolve Unity references without
editing serialized YAML.

```powershell
function Get-UnityGuid([string] $metaPath) {
  $line = Get-Content -LiteralPath $metaPath |
    Where-Object { $_ -match '^guid: ' } |
    Select-Object -First 1
  if (-not $line) { throw "Missing GUID in $metaPath" }
  $line -replace '^guid: ', ''
}

$creatureComponentGuid = Get-UnityGuid 'Assets/Scripts/Creature/CreatureComponent.cs.meta'
$actionControllerGuids = @(
  Get-UnityGuid 'Assets/Scripts/Combat/PlayerActionController.cs.meta'
  Get-UnityGuid 'Assets/Scripts/Combat/MindlessController.cs.meta'
)
$teamGuid = Get-UnityGuid 'Assets/Scripts/Combat/Team.cs.meta'
$conditionsGuid = Get-UnityGuid 'Assets/Scripts/Creature/Conditions/Conditions.cs.meta'
$allCreatureComponentPrefabs = @(foreach ($prefab in Get-ChildItem -LiteralPath 'Assets' -Recurse -File -Filter '*.prefab') {
  $yaml = Get-Content -Raw -LiteralPath $prefab.FullName
  if ($yaml.Contains("guid: $creatureComponentGuid")) { $prefab }
})
$combatantPrefabs = @(foreach ($prefab in $allCreatureComponentPrefabs) {
  $yaml = Get-Content -Raw -LiteralPath $prefab.FullName
  $hasController = $false
  foreach ($guid in $actionControllerGuids) {
    if ($yaml.Contains("guid: $guid")) { $hasController = $true; break }
  }
  if ($hasController) {
    if (-not $yaml.Contains("guid: $teamGuid") -or -not $yaml.Contains("guid: $conditionsGuid")) {
      throw "Combatant prefab lacks Team or Conditions: $($prefab.FullName)"
    }
    $prefab
  }
})
$combatantPrefabGuids = @($combatantPrefabs | ForEach-Object {
  Get-UnityGuid ($_.FullName + '.meta')
})
$ruleEntryScenes = @(foreach ($scene in Get-ChildItem -LiteralPath 'Assets/Scenes' -Recurse -File -Filter '*.unity') {
  $yaml = Get-Content -Raw -LiteralPath $scene.FullName
  foreach ($guid in $combatantPrefabGuids) {
    if ($yaml.Contains("guid: $guid")) { $scene; break }
  }
})

$sets = [ordered]@{
  'Assets/Scripts' = @(Get-ChildItem -LiteralPath 'Assets/Scripts' -Recurse -File |
    Where-Object Extension -ne '.meta')
  'Assets/KayKit/Runtime' = @(Get-ChildItem -LiteralPath 'Assets/KayKit/Runtime' -Recurse -File |
    Where-Object Extension -ne '.meta')
  'Assets/UIStuff C#' = @(Get-ChildItem -LiteralPath 'Assets/UIStuff' -Recurse -File -Filter '*.cs')
  'Assets root/runtime support' = @(Get-Item -LiteralPath 'Assets/InputSystem_Actions.cs',
    'Assets/InputSystem_Actions.inputactions', 'Assets/MainGameAssembly.asmdef',
    'Assets/TutorialInfo/Scripts/Readme.cs')
  'Assets/Maps/KayKit' = @(Get-ChildItem -LiteralPath 'Assets/Maps/KayKit' -Recurse -File |
    Where-Object Extension -ne '.meta')
  'Assets/KayKit/Catalogs' = @(Get-ChildItem -LiteralPath 'Assets/KayKit/Catalogs' -File |
    Where-Object Extension -ne '.meta')
  'Serialized combatant prefabs' = $combatantPrefabs
  'Serialized rule-entry scenes' = $ruleEntryScenes
  'Grid topology prefab' = @(Get-Item -LiteralPath 'Assets/Prefabs/SceneEssentials/Grid.prefab')
  'Assets/Resources/DataFiles' = @(Get-ChildItem -LiteralPath 'Assets/Resources/DataFiles' -Recurse -File |
    Where-Object Extension -ne '.meta')
  'Assets/Resources/Data' = @(Get-ChildItem -LiteralPath 'Assets/Resources/Data' -Recurse -File |
    Where-Object Extension -ne '.meta')
  'Assets/Resources/Icons' = @(Get-ChildItem -LiteralPath 'Assets/Resources/Icons' -Recurse -File |
    Where-Object Extension -ne '.meta')
  'Assets/UIStuff/Resources' = @(Get-ChildItem -LiteralPath 'Assets/UIStuff/Resources' -Recurse -File |
    Where-Object Extension -ne '.meta')
  'Assets/Tests/EditMode' = @(Get-ChildItem -LiteralPath 'Assets/Tests/EditMode' -Recurse -File |
    Where-Object Extension -ne '.meta')
  'Assets/Tests/PlayMode' = @(Get-ChildItem -LiteralPath 'Assets/Tests/PlayMode' -Recurse -File |
    Where-Object Extension -ne '.meta')
}
foreach ($entry in $sets.GetEnumerator()) {
  $entry.Value | Sort-Object FullName
}
```

The resulting closed inventory is:

| Selection | Files | Contents |
| --- | ---: | --- |
| `Assets/Scripts` | 275 | 271 `.cs`, 2 `.asmdef`, and 2 noncompiled `.orig` conflict artifacts |
| `Assets/KayKit/Runtime` | 16 | 16 player-runtime C# files |
| `Assets/UIStuff` C# | 11 | 11 player-runtime C# files under menus, HUD, and character creation |
| `Assets` root/runtime support | 4 | Generated input C#, its `.inputactions` source, `MainGameAssembly.asmdef`, and `TutorialInfo/Scripts/Readme.cs` |
| `Assets/Maps/KayKit` | 2 | Authored dungeon-map JSON assigned to scene `TextAsset` fields |
| `Assets/KayKit/Catalogs` | 5 | One dungeon topology catalog and four presentation/source catalogs |
| Serialized combatant prefabs | 8 | Controller-bearing prefabs with serialized `CreatureComponent`, `Team`, and `Conditions` components |
| Serialized rule-entry scenes | 6 | Scenes whose YAML resolves at least one of those eight prefab GUIDs |
| Grid topology prefab | 1 | Serialized map settings and tile/prefab references used by the six rule-entry scenes |
| `Assets/Resources/DataFiles` | 140 | 139 JSON files and 1 `DungeonEncounterCreatureCatalog.asset` |
| `Assets/Resources/Data` | 3 | Legacy character-creation JSON: ancestry, class, and a player-character template |
| `Assets/Resources/Icons` | 19 | UI icon resources; 17 have literal UI Toolkit references and 2 are currently unreferenced |
| `Assets/UIStuff/Resources` | 5 | Storyboard JSON and UI font assets loaded through `Resources` |
| `Assets/Tests/EditMode` | 61 | 59 C# files and 2 assembly definitions |
| `Assets/Tests/PlayMode` | 25 | 24 C# files and 1 assembly definition |

An independent recursive `.cs` sweep under `Assets` found 397 C# files: 83 in the two test roots,
14 editor-only files in `Editor` path segments, and 300 player-runtime files. The latter are
accounted for exactly by 271 under `Assets/Scripts`, 16 under `Assets/KayKit/Runtime`, 11 under
`Assets/UIStuff`, `Assets/InputSystem_Actions.cs`, and `Assets/TutorialInfo/Scripts/Readme.cs`.
The 14 editor-only files are tooling, not player entry points. `Assets/TextMesh Pro/Resources`
contains nine imported package resources and is not authored game data. Serialized creature seeds,
scene placements, map settings, and topology catalogs are rule inputs and are explicitly inventoried
below. Other scenes, prefabs, UI documents/styles, models, and art were inspected where they
establish a caller or reference but remain orchestration or presentation wiring.

For a reproducible closed-set check, relative paths were normalized to `/`, sorted ordinally,
joined with LF including a final LF, encoded as UTF-8 without BOM, and hashed with SHA-256:

| Selection | Path-inventory SHA-256 |
| --- | --- |
| `Assets/Scripts` | `af1c8e8e1423d6a0ef89dd670448396a88a01043ca6e7d59be10f475b10f03f0` |
| `Assets/KayKit/Runtime` | `14d7ae7e097e91433b800c27d910a32ed6a16f6e2e456179910deebd4c35505e` |
| `Assets/UIStuff` C# | `4e6a2d784e4193dfd3d5a8c39e97b082615900617e4d48893f99d72ccbd2c6a4` |
| `Assets` root/runtime support | `ad21d95d2faefcda779fdc2f0e8b37a61584f1ec8643007160e8f143b53e732c` |
| `Assets/Maps/KayKit` | `ba676a472a013edc90054b46528b381429aede35796661343c064eea8d2dd068` |
| `Assets/KayKit/Catalogs` | `b7472687feebf34ec85c702b05d803e4892308d482b1b54506acc1a32412e928` |
| Serialized combatant prefabs | `8e572bf39091acd7654b987855b66d803841513e57784b000ec9f22e5d20ab91` |
| Serialized rule-entry scenes | `4be16a90ed2ebeee7b30aa1cbb025629f7fee43971ad310a278831d74031018d` |
| Grid topology prefab | `940b6466aa40f9baf6b8d1cb16929eb88b71cddff313c8129eb98106f19f8def` |
| `Assets/Resources/DataFiles` | `7e0e3e18d690b53a3328d01d27aee4864ab03b842c8153838a689a2163417095` |
| `Assets/Resources/Data` | `53d37ebfb35d65fb41108d8f414c6496ab0c428f10f02d7023880aaab9cfb38b` |
| `Assets/Resources/Icons` | `44e96509772079ac523d59ff127de4ef38e93a59dce4867b7d73d8be67fd4aa9` |
| `Assets/UIStuff/Resources` | `2386c9d507c8a4986adeb191dd7d9c3a2dd256ff555588758f30f5ab05d84818` |
| `Assets/Tests/EditMode` | `fa0b46013dc879e4c943c6f150b5660f117816314259acfdd3d77df18ad89164` |
| `Assets/Tests/PlayMode` | `3036ac368709b1b9a042e53522a64aee43141cbabdda78a9f573b1c038f267bb` |

The exact hash operation for each selection was:

```powershell
$paths = [System.Collections.Generic.List[string]]::new()
$entry.Value | ForEach-Object {
    $paths.Add([IO.Path]::GetRelativePath((Get-Location).Path, $_.FullName).Replace('\', '/'))
}
$paths.Sort([StringComparer]::Ordinal)
$bytes = [Text.UTF8Encoding]::new($false).GetBytes(([string]::Join("`n", $paths) + "`n"))
[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
```

Every production-code file was included in one of the subsystem rows below. Final validation
repeated the counts and hashes and checked every unique backticked current path beginning with
`Assets/` using `Test-Path`; none were missing. The plan does not add a second machine-readable
inventory because the checked-in path selections and count/hash gates provide the completeness
proof.

### Production-code inventory by subsystem

| Subsystem | Count | Enumerated files or closed directory inventory | Disposition |
| --- | ---: | --- | --- |
| Rules runtime | 95 | Every non-meta file in `Assets/Scripts/Rules/Runtime`: `ActionCostFacts.cs`, `ActionCosts.cs`, `ActionDefinitions.cs`, `ActionLifecycle.cs`, `ActionLifecycleHandlers.cs`, `ActionLifecycleOperations.cs`, `ActionLifecycleRuntime.cs`, `ActionValidation.cs`, `ActiveEffectFacts.cs`, `ActiveEffectOperations.cs`, `ActiveEffectReducers.cs`, `ActiveEffectRuleRuntime.cs`, `CallbackWork.cs`, `CheckHandlers.cs`, `Checks.cs`, `CombatMath.cs`, `CreatureStatistics.cs`, `Dispatch.cs`, `DispatcherRegistrations.cs`, `DistanceValues.cs`, `EncounterCombatantState.cs`, `EncounterFacts.cs`, `EncounterOperations.cs`, `EncounterReducers.cs`, `EncounterRuleRuntime.cs`, `EncounterValues.cs`, `FactContexts.cs`, `FactListenerDispatch.cs`, `FactObservers.cs`, `HealthFacts.cs`, `HealthOperations.cs`, `HealthReducers.cs`, `HealthRuleRuntime.cs`, `Identifiers.cs`, `MiddlewareDispatch.cs`, `ModifierCollectionOperations.cs`, `Modifiers.cs`, `MovementFacts.cs`, `MovementOperations.cs`, `MovementReducers.cs`, `MovementRuleRuntime.cs`, `MovementTopology.cs`, `MovementValidation.cs`, `MovementValues.cs`, `MultiAttackPenaltyRules.cs`, `ObserverFailureState.cs`, `OperationContexts.cs`, `OperationContracts.cs`, `OperationFrames.cs`, `OperationResults.cs`, `Pf2eSlug.cs`, `PromptAdapters.cs`, `PromptContracts.cs`, `PromptDispatch.cs`, `RageRules.cs`, `Reduction.cs`, `ReferenceEqualityComparer.cs`, `ResolutionDiagnostics.cs`, `ResolutionRolls.cs`, `ResolutionTrace.cs`, `ResolvedOperationObservers.cs`, `ResourceStateValues.cs`, `RollServices.cs`, `RuleBindingStateValues.cs`, `RuleDefinitions.cs`, `RuleDispatcherBuilder.cs`, `RuleDispatcherFacts.cs`, `RuleDispatcherLifecycle.cs`, `RuleDispatcherResolution.cs`, `RuleExtensions.cs`, `RuleRegistrations.cs`, `RuleRegistry.cs`, `RulesRuntime.asmdef`, `RulesSelectors.cs`, `RulesSnapshot.cs`, `RulesState.cs`, `RulesStateData.cs`, `RulesStateDraft.cs`, `RulesStateSeed.cs`, `RuleValues.cs`, `SelectionChoiceValues.cs`, `SelectionOutcomes.cs`, `SelectionResolution.cs`, `SelectionWorkflow.cs`, `SelectionWorkflowFactories.cs`, `Skills.cs`, `SpellAttackContracts.cs`, `SpellAttackRules.cs`, `SpellcastingContracts.cs`, `SpellcastingRules.cs`, `StateSlices.cs`, `StateValues.cs`, `StrideRules.cs`, `StrikeRules.cs`, and `TypedDamage.cs` | Foundation plus migrated vertical features; details below |
| Unity rules integration | 20 | Every non-meta file in `Assets/Scripts/Rules/Unity`, including `Attack/*`, `Composition/*`, `Light/*`, `Spells/*`, `Strike/*`, `FactObserverBehaviour.cs`, `StrideSelectionResolvers.cs`, `UnityCombatRulesBridge.cs`, `UnityCoroutineTask.cs`, `UnityRageActorStateProvider.cs`, and `UnityStrideProjectionObserver.cs` | Feature adapters, composition, projection, and ownership |
| Legacy/shared rule facade | 3 | `Assets/Scripts/Rules/IPf2eModifierProvider.cs`, `Assets/Scripts/Rules/Pf2eModifierCollection.cs`, `Assets/Scripts/Rules/Pf2eModifiers.cs` | Transitional Unity modifier authority |
| Combat and actions | 48 | Every non-meta file in `Assets/Scripts/Combat`, including `Actions/Attacks/{AttackResultPipeline.cs,Strike.cs,Unarmed.cs.orig}`, `Actions/Strike/RulesStrikeAction.cs`, `Actions/{RulesRageAction.cs,RulesStrideAction.cs,Stride.cs.orig}`, all 10 `Spells/*.cs` files, controllers, manager/interfaces, team/line-of-sight, combat events/logging, dungeon encounter lifecycle/director/materialization/runtime/state, exploration policy/runtime/state/planner, and `FlankingRule.cs` | General callers plus transitional or dead legacy behavior |
| Creature and PF2e preparation | 36 | Every non-meta file in `Assets/Scripts/Creature`: conditions/abilities, `CreatureComponent.cs`, converter/data/equipment/damage/dice/portrait/sign/token files, `Rules/{CharacterBuild.cs,Pf2eCharacterPreparer.cs,Pf2eItem.cs,Pf2eItemCatalog.cs,Pf2ePredicate.cs,Pf2eRulesEngine.cs,Pf2eSlug.cs,PreparedCharacter.cs}`, and all six `Rules/Auras/*.cs` files | Initial-data adapters, transitional rules, presentation, and unimplemented declarations |
| Grid and targeting | 29 | Every non-meta file in `Assets/Scripts/Grid`: area/aura/targeting/visual/input/token/movement/FSM/pathfinding files and all generation/scene-object files | Topology, selection, presentation, and transitional Unity calculations |
| Dungeon generation | 15 | Every non-meta file in `Assets/Scripts/DungeonGeneration` | Non-encounter procedural policy and persistence data; no rules-runtime migration |
| Dungeon persistence | 8 | Every non-meta file in `Assets/Scripts/DungeonPersistence` | General save/caller integration; must follow authority migrations |
| General Unity infrastructure | 21 | The five root files in `Assets/Scripts` (`AssemblyInfo.cs`, `AudioManager.cs`, `Movement.cs`, `SceneTransitionManager.cs`, and `UnversalEvents.cs`), `Assets/Scripts/Camera/CameraManager.cs`, every non-meta file in `Assets/Scripts/Interfaces`, and every non-meta file in `Assets/Scripts/Utility` | No rules migration unless a caller changes |
| KayKit player runtime | 16 | Every C# file in `Assets/KayKit/Runtime`: animation/equipment presentation, dungeon document/catalog parsing, map/line-of-sight adapters, and door visual/collider state | Presentation and topology adapters; combat door legality/cost is transitional behavior described below |
| UI, menus, and character creation | 11 | Every C# file under `Assets/UIStuff`: `CharacterCreation/CharacterCreationScript.cs`, `CharacterCreation/TutorialManager.cs`, `HowToPlayMenu/HowToPlayMenuControl.cs`, `HUD/CombatLog.cs`, `HUD/CombatLogInterface.cs`, `HUD/HUDController.cs`, `MainMenu/MainMenuControl.cs`, `SettingsMenu/SettingsMenuControl.cs`, `StatusMenu/StatusMenuControl.cs`, `StoryBoard/StoryBoardControl.cs`, and `WinScreen/WinScreenControl.cs` | Presentation/callers plus the disconnected legacy character-build calculator described below |
| Root/runtime support | 4 | `Assets/InputSystem_Actions.cs`, `Assets/InputSystem_Actions.inputactions`, `Assets/MainGameAssembly.asmdef`, and `Assets/TutorialInfo/Scripts/Readme.cs` | Generated input API/source configuration, production assembly boundary, and inert tutorial-template metadata |

Within the UI row, `HUDController` reads the current controller action list, invokes chosen actions,
controls presentation/camera/input settings, and requests dungeon autosave checkpoints.
`MainMenuControl` inspects autosave status and starts/replaces or continues dungeon runs through the
persistence service. The remaining menu/HUD scripts are presentation and scene-flow callers except
for the legacy character-build calculations mapped below. These callers must switch with an
authority migration but must not reproduce operation legality or outcomes.

These rows total 306 checked-in production/source inputs: 300 player-runtime C# files, three
production assembly definitions, one Input System source asset, and two `.orig` artifacts. The two
tracked `.orig` files contain conflict markers and are not compiled. They are historical copies,
not alternate Stride or Strike behavior. Removing them is repository cleanup outside this migration
plan, not a compatibility step.

### Rules, character-build, and presentation data inventory

The 143 non-meta files in `Assets/Resources/DataFiles` and `Assets/Resources/Data` divide exactly as
follows:

- `actions` (2): `quick-tempered`, `rage`.
- `ancestries` (1): `human`.
- `backgrounds` (3): `bandit`, `nomad`, `warrior`.
- `classes` (4): `barbarian`, `cleric`, `fighter`, `rogue`.
- `classfeatures` (12): `cleric-spellcasting`, `cloistered-cleric`, `deity-cleric`,
  `divine-font`, `doctrine`, `first-doctrine`, `first-doctrine-cloistered-cleric`,
  `fury-instinct`, `rogues-racket`, `sneak-attack`, `surprise-attack`, `thief`.
- `dungeon` (2): `DungeonEncounterCreatureCatalog.asset`, `encounter-enemies.json`.
- `effects` (2): `effect-rage`, `effect-rage-temporary-hit-points-immunity`.
- `equipment` (12): `breastplate`, `dogslicer`, `greataxe`, `halberd`, `leather-armor`,
  `longsword`, `padded-armor`, `scale-mail`, `scimitar`, `shortbow`, `sling`, `spear`.
- `feats` (9): `domain-initiate`, `natural-skill`, `nimble-dodge`, `overextending-feint`,
  `plant-evidence`, `raging-intimidation`, `reactive-shield`, `tumble-behind-rogue`, `twin-feint`.
- `heritages` (1): `skilled-human`.
- `iconics` (1): `valeros-level-1`.
- `pathfinder-monster-core` (5): `goblin-warrior`, `kobold-warrior`, `skeleton-guard`,
  `zombie-shambler`, `zombie-shambler-rotting-aura`.
- `playerCharacters` (2): `Lena`, `Torgrim`.
- `spells` (84): every JSON under `spells/1st-rank` and `spells/cantrip`. The exact names are
  `air-bubble`, `alarm`, `ant-haul`, `bane`, `bless`, `breathe-fire`, `caustic-blast`, `charm`,
  `cleanse-cuisine`, `command`, `create-water`, `daze`, `detect-magic`, `detect-poison`,
  `disguise-magic`, `divine-lance`, `dizzying-colors`, `electric-arc`, `enfeeble`, `fear`,
  `figment`, `fleet-step`, `forbidding-ward`, `force-barrage`, `frostbite`, `gentle-landing`,
  `goblin-pox`, `gouging-claw`, `grease`, `grim-tendrils`, `guidance`, `gust-of-wind`, `harm`,
  `haunting-hymn`, `heal`, `hydraulic-push`, `ignition`, `ill-omen`, `illusory-disguise`,
  `illusory-object`, `infuse-vitality`, `item-facade`, `jump`, `know-the-way`, `light`, `lock`,
  `mending`, `message`, `mindlink`, `mystic-armor`, `pest-form`, `pet-cache`,
  `phantasmal-minion`, `phantom-pain`, `prestidigitation`, `protection`, `pummeling-rubble`,
  `read-aura`, `runic-body`, `runic-weapon`, `sanctuary`, `shield`, `sigil`, `sleep`, `soothe`,
  `spider-sting`, `spirit-link`, `stabilize`, `summon-animal`, `summon-construct`, `summon-fey`,
  `summon-instrument`, `summon-plant-or-fungus`, `summon-undead`, `sure-strike`, `tailwind`,
  `tangle-vine`, `telekinetic-hand`, `telekinetic-projectile`, `thunderstrike`,
  `vanishing-tracks`, `ventriloquism`, `vitality-lash`, and `void-warp`.
- `legacy character creation` (3): `ancestry.json`, `class.json`, and `playerCharacter.json`.
  `CharacterCreationScript` loads the first two by literal `Resources` paths; the third is a
  template and has no literal production load call.

The other 24 authored resources are presentation inputs. Of the 19 files under
`Assets/Resources/Icons`, 17 have literal UI Toolkit `resource('Icons/...')` references; `move2.png`
and `speed.png` have no discovered literal reference but remain loadable resources. The five files
under `Assets/UIStuff/Resources` comprise `storyboard.json` (loaded by `StoryBoardControl` and
`WinScreenControl`) plus two font files and their two referenced SDF assets. These resources carry
no rules authority.

The two JSON files under `Assets/Maps/KayKit` are authored topology inputs rather than
`Resources` data. `GeneratedDungeonFixture.json` is assigned to the production
`ProceduralDungeon` scene's `Map.jsonSource`; `KayKitDungeonExample.json` is assigned to the
reference scene that is deliberately excluded from build settings. `Map` parses either such an
authored `TextAsset` or generated runtime JSON into dungeon topology. These files are dungeon
generation/topology fixtures, not encounter rules authority.

### Serialized rule-input inventory

The serialized inventory is derived from script and asset `.meta` GUIDs, then by read-only scans of
prefab and scene YAML; the YAML itself is not edited. A combatant seed is defined narrowly as a
prefab containing `CreatureComponent` and either the concrete `PlayerActionController` or
`MindlessController`. That query returns exactly these eight prefabs:

- `Assets/Prefabs/Creatures/EmptyCreature.prefab`
- `Assets/Prefabs/Creatures/Lena.prefab`
- `Assets/Prefabs/Creatures/Torgrim.prefab`
- `Assets/Prefabs/Creatures/goblin-warrior.prefab`
- `Assets/Prefabs/Creatures/kobold-warrior.prefab`
- `Assets/Prefabs/Creatures/skeleton-guard.prefab`
- `Assets/Prefabs/Creatures/zombie-shambler-rotting-aura.prefab`
- `Assets/Prefabs/Creatures/zombie-shambler.prefab`

All eight also serialize `Team` and `Conditions`. Their `CreatureComponent` fields can seed level,
initiative, speed, health, AC, attacks/damage, weaknesses/resistances, abilities, saves, skills,
actions/reactions/passives/auras, equipment/ammunition, and character-build inputs. A broader
`CreatureComponent` scan also finds `Assets/Prefabs/MapPieces/Walls/Bricks/Door.prefab` and
`Assets/Prefabs/UI/ViewModel.prefab`; neither has a concrete action controller, so neither is an
encounter-combatant rule seed.

Resolving those eight prefab GUIDs under `Assets/Scenes` finds exactly six serialized rule-entry
scenes: `KayKitDungeonExample.unity`, `Level1.unity`, `Level2.unity`, `Level3.unity`,
`ProceduralDungeon.unity`, and `UnitTestingScene.unity`. `Level2.unity`, for example, directly
places `zombie-shambler-rotting-aura.prefab`, so that prefab's serialized health, defenses,
passives, and aura are reachable inputs rather than presentation-only data. Only
`ProceduralDungeon.unity` and `UnitTestingScene.unity` are enabled in
`ProjectSettings/EditorBuildSettings.asset`; the other four are retained reference/test scenes.
Tracked scenes under `_Recovery` are recovery artifacts rather than player/test entry points and
are excluded from this `Assets/Scenes` query.

Catalog materialization is a separate entry path. `DungeonEncounterCreatureCatalog.asset` maps
goblin, kobold, skeleton, and zombie JSON paths to their corresponding prefabs;
`CreatureJsonConverter.CreateFromFile` instantiates the prefab and `ApplyFromDto` overlays imported
JSON before runtime action initialization. Direct scene placements retain their serialized seed
values unless another caller applies an overlay.

The six scenes reference `Assets/Prefabs/SceneEssentials/Grid.prefab`, whose serialized
`MapGenerator` configuration and tile/prefab references seed topology. GUID resolution also shows
that `ProceduralDungeon.unity` and `KayKitDungeonExample.unity` reference
`Assets/KayKit/Catalogs/KayKitDungeonCatalog.asset`; its entries define whether structures block
movement and line of sight. The other four assets in `Assets/KayKit/Catalogs` are visual,
animation, equipment-presentation, or source-manifest catalogs and carry no rules decisions.

A separate recursive JSON sweep found 146 files under `Assets`: the 139 DataFiles JSON, three
legacy character-creation files, `storyboard.json`, and these two KayKit maps account for all 145
authored game JSON files. The remaining `Assets/TextMesh Pro/Sprites/EmojiOne.json` belongs to the
imported TextMesh Pro package.

`Pf2eItemCatalog.LoadFromResources` makes parseable item JSON available for preparation, while
`UnitySpellDefinitionCatalog.Load` parses all 84 spell files into immutable definitions. Loading is
not implementation. In particular, the spell catalog recognizes a generic self-target
`CreateActiveEffect` directive and a narrow one-creature attack shape. Of the current JSON, only
Divine Lance passes that attack parser; nonempty overlays disqualify Ignition and Telekinetic
Projectile. An installed action is allowed only when the prepared spell and its supported behavior
compose successfully.

Across the JSON inventory, rule keys discovered were `ActiveEffectLike`, `ActorTraits`,
`AdjustModifier`, `Aura`, `ChoiceSet`, `CreateActiveEffect`, `DamageDice`, `EphemeralEffect`,
`FlatModifier`, `GrantItem`, `ItemAlteration`, `Note`, `Resistance`, `RollOption`, `Strike`,
`SubstituteRoll`, `TempHP`, `TokenEffectIcon`, and `TokenLight`. The preparer executes only
`ChoiceSet`, `GrantItem`, `ActiveEffectLike`, `FlatModifier`, `AdjustModifier`, `DamageDice`,
`ItemAlteration`, and non-toggleable/non-target `RollOption`. It deliberately ignores `TempHP` and
`Resistance` there, records other encountered keys as unsupported, and has separate adapters for
`Aura` and spell `CreateActiveEffect`. Therefore every other authored key is data-only until a
vertical feature explicitly implements it.

### Test inventory

The complete test-file inventory is the 61 files under `Assets/Tests/EditMode` and 25 under
`Assets/Tests/PlayMode`. Rules-relevant files are mapped to fixtures below. The remaining tests are
still accounted for:

- EditMode general/caller coverage: `Combat/Exploration/DungeonDoorInteractionPolicyTests.cs`,
  `Combat/Exploration/ExplorationStepPlannerTests.cs`, `CombatLogEntryFormatterTests.cs`,
  `DungeonEncounterMaterializerTests.cs`, `DungeonEncounterStateMachineTests.cs`, all four
  `DungeonGeneration/*.cs` tests, all four `DungeonPersistence/**/*.cs` tests,
  `EncounterLifecycleTestExtensions.cs`, `GridClassTests.cs`, all eight `KayKit/*.cs` tests,
  `LenaRogueSceneFixtureTests.cs`, `Pf2eAreaTargetingTests.cs`, `Pf2eModifierTests.cs`,
  `Pf2eRottingAuraTests.cs`, `Pf2eRulesTests.cs`, `Pf2eSlugAdapterTests.cs`, and
  `PreparedSpellBookTests.cs`.
- EditMode runtime/bridge coverage: every file in `Assets/Tests/EditMode/RulesRuntime`, plus
  `RulesRageUnityTests.cs`, `RulesStrikeUnityTests.cs`, `SpellAttackUnityTests.cs`, and
  `UnityCombatRulesBridgeTests.cs`.
- PlayMode caller/integration coverage: `DungeonEncounterCombatPlayModeTests.cs`,
  `DungeonEncounterDirectorPlayModeTests.cs`, `DungeonEncounterRuntimeControllerPlayModeTests.cs`,
  `DungeonExplorationRuntimePlayModeTests.cs`, `DungeonProductionFlowPlayModeTests.cs`,
  `DungeonRunTraversalPlayModeTests.cs`, `EncounterLifecycleTestExtensions.cs`,
  `FactObserverBehaviourPlayModeTests.cs`, all three `KayKit*PlayModeTests.cs`, `MinimalCombatGrid.cs`,
  `PlayModeBase.cs`, `ProceduralDungeonScenePlayModeTests.cs`,
  `RulesStrikeIntegrationPlayModeTests.cs`, `SpellcastingPresentationPlayModeTests.cs`, all five
  `TestsState/*.cs` files, and all three `TestsUI/*.cs` files.

## Existing production composition and authority

`UnityCombatRulesBridge.Create` constructs one `RuleDispatcher`, one `RulesState`, and one
`CompositeLifetime`. `UnityEncounterModuleSet.Create` is the only production module list and orders
Rotting Aura, Slowed, Rage, Strike, Spellcasting, Light, health projection, and encounter
projection. `UnityCombatantEnrollmentPipeline.Prepare` is the one initial/reinforcement preparation
path. `AddCombatantsOp` is the one authoritative commit path.

The complete production registration currently contains `CreatureState`, `HealthState`,
`GridPosition`, land `GridDistance`, initiative modifier, spell slots, active rule bindings,
equipment, ammunition, and active effects reconstructed from restored legacy
`SpellEffectController` timed effects. That reconstruction is an enrollment adapter, not general
dungeon persistence for rules-native effects. Addition also initializes action economy and multiple
attack penalty and inserts encounter timing. It does **not** populate
`CreatureStatisticsState`, `ConditionState`, or `FocusPointState`. Those generic slices and seed
APIs exist and have pure tests, but are not production encounter authorities. Any later migration
must extend the complete registration and its atomic reducer rather than seed a second path.

### Shared foundation already in production

| Capability | Current implementation and entry points | Authority/state | Migration and ownership | Representative verification and gaps |
| --- | --- | --- | --- | --- |
| Typed dispatch | `IRuleOp<TResult>`, `OpResult<TResult>`, handlers, middleware, reducers, Fact listeners/observers, root and causal-tree settlement in `Assets/Scripts/Rules/Runtime` | Dispatcher frames plus immutable snapshots; reducers alone write | **No migration. Foundation-owned.** Extend only for a demonstrated vertical need | `DispatcherTests`, `RuleExtensionTests`, `RulesRuntimeTests`, `FactObserverTests`, and `ResolvedOperationObserverTests` assert structural outcomes, rollback, ordering, observer failures, nested work, and settlement |
| Action lifecycle | `ActionOp<TResult>`, `ActionProfile`, `ActionRuntime`, `CommitActionCostsOp`, and `ActionBegunOp`; callers query `IActionDefinition.GetAvailability` | Action economy, spell slots, Focus Points when present, ammunition, and binding frequency are committed atomically | **No migration. Foundation-owned.** Every migrated action must use it once | `ActionLifecycleTests` plus Strike, spell, Stride, and Rage tests assert validation before cost, atomic payment, and begun-before-handler order |
| Active effects/bindings | `CreateActiveEffectOp`, `UpdateActiveEffectStateOp`, `RemoveActiveEffectOp`, `RuleRegistry`, `ActiveRuleBinding`, `ActiveEffectInstance`, `FrequencyState`, `ActiveEffectTimingState` | `RulesState.ActiveEffects`, `.RuleBindings`, `.Frequencies`, `.ActiveEffectTimings` | **No migration. Foundation-owned.** Feature definitions/listeners/state remain feature-owned | `ActiveEffectLifecycleTests` and `EncounterRuntimeTests` assert registry validation, exact types/versions, paired removal, timing, and deterministic expiration. Missing: dungeon saves do not serialize general rules-native effects, bindings, or timing; rules-native Light is a concrete reachable omission |
| Checks/modifiers | `AttackCheckOp`, `SkillCheckOp`, `SavingThrowOp`, collect-modifier Ops, `ModifierCollection`, `RulesSelectors` | Operation-local results plus `RulesState.Statistics` when seeded; production Statistics is currently absent | Foundation is complete for current consumers. **Caller/composition migration** must enroll statistics before general Unity reads are removed | `CheckOperationTests`, `CheckModifierCollectionTests`, and `ModifierSelectorTests`; missing: production enrollment/projection tests for all imported statistics |
| Typed damage | `TypedDamageResolver`, `TypedDamageDice`, `TypedFlatDamage`, and `TypedDefenseAdjustment`; Strike/spell handlers dispatch final `ApplyDamageOp` | Operation-local typed groups, then `HealthState` | **No shared migration.** Feature adapters currently capture defenses from Unity | Strike and spell attack tests cover critical-before-defense and per-type adjustments; missing: persisted/authoritative defense state |
| Prompt/selection | `ChoiceRequest<T>`, `IPromptAdapter<T>`, `PromptChoiceOp<T>`, `SelectionWorkflow<T>`, and `ISelectionResolver` | Operation-local immutable selection; no persistent state | **No migration. Foundation-owned.** Unity selectors remain feature adapters | `PromptOperationTests`, `SelectionValueTests`, `SelectionWorkflowTests` cover choice ownership, cancellation, invalid selections, and ordering |
| Roll service | `IRollService`, `RandomRollService`, `ScriptedRollService`, resolution roll trace | Dispatcher-owned operation rolls | **No migration. Foundation-owned.** Remove remaining direct `UnityEngine.Random` only while migrating its feature | `RollServiceTests`; remaining direct initiative helper and legacy `D20`/`Dice` paths are transitional |

## Mapped migrated features

Each row names the present behavior, not wider PF2e completeness.

### Encounter lifecycle, turns, and enrollment

- **Current behavior and entry points:** `CombatManager.StartCombat`, `EnterTactics`,
  `StartDungeonCombat`, `AddDungeonReinforcements`, `EndCurrentTurn`, `NextTurn`, and
  `TryReturnToExploration` call bridge methods backed by `InitEncounterOp`, `AddCombatantsOp`,
  `AdvanceEncounterOp`, `EndTurnOp`, `SuspendEncounterOp`, and `EndEncounterOp`.
  `DungeonEncounterDirector` and `DungeonEncounterRuntimeController` choose room participants and
  reinforcements but do not determine initiative or turns.
- **Actual rules behavior:** initiative rolls use `IRollService`; stable registration order breaks
  ties. An exact `TurnIdentity` gates action authority. Turn start resets movement, applies ordered
  adapters, and skips defeated actors; for an actor that reaches the turn commit, it records the
  final action contribution and refreshes the reaction. Turn end clears actions, resets MAP and
  movement, preserves the current reaction availability, and advances. Outcome policy is either
  victory-or-defeat or protagonist-defeat-only. Reinforcements never advance a turn and use round
  eligibility rules.
- **Authority/persistent state:** `EncounterState`, `ActionEconomyState`,
  `MultipleAttackPenaltyState`, `MovementBudgetState`, roster registrations, and effect timing.
  Unity `Combatants`/`activeCombatants` are host mappings and presentation lists, not turn state.
- **Migration:** no rules migration. General caller/composition work may remove obsolete manager
  aliases only after every scene caller is updated. Preserve the common enrollment transaction,
  exact detach identity, post-commit durability, and encounter lifetime.
- **Verification:** `EncounterRuntimeTests` asserts initialization, start, ties, exact turns,
  resets, defeat settlement, policies, reinforcements, invalid batches, and expiration.
  `UnityCombatRulesBridgeTests` asserts rollback/attachment/reinforcement identity.
  `DungeonEncounterCombatPlayModeTests` and director/runtime-controller tests assert scene flows.
  Missing coverage: no known rules gap; concrete manager compatibility methods remain caller debt.
- **Exact owner:** foundation files remain `Encounter*.cs`; general caller worker owns
  `CombatManager.cs`, `CombatManagerInterface.cs`, dungeon encounter callers, and composition-only
  changes in `UnityCombatRulesBridge.cs` and `Composition/*`.

### Health, temporary Hit Points, and defeat

- **Current behavior and entry points:** `CreatureComponent.ApplyFinalDamage`, `Heal`,
  `GrantSourceTemporaryHitPoints`, `RemoveSourceTemporaryHitPoints`, and
  `AddTemporaryHitPointImmunity` require an attached bridge. They dispatch the corresponding health
  Ops. `UnityHealthProjectionModule` projects Facts to serialized inspector/UI fields and defeat
  presentation.
- **Actual rules behavior:** damage consumes temporary HP before HP and clamps at zero; healing
  clamps at maximum; a positive-to-zero transition emits once; final defeat settles reaction work
  before encounter outcome. Source-scoped temporary HP replacement/removal and immunity are
  authoritative.
- **Authority/persistent state:** during an attached encounter, `HealthState` owns current, maximum,
  temporary amount/source, immunity sources, and committed defeat; `CreatureComponent.Health` and
  `hp` read that bridge snapshot. Dungeon persistence splits its projection across callers: the
  autosave coordinator writes party current HP and defeat to the outer party record, the encounter
  director writes living-enemy current HP and records defeated enemy identities separately, and the
  nested `DungeonActorStateAdapter` writes only temporary HP amount/source/immunities. Maximum HP is
  reconstructed from creature content on restore.
- **Migration:** no health-rules migration. Features must dispatch generic health Ops rather than
  mutate Unity. Persistence work must preserve the current split restore contract or replace it in
  one coordinated schema/caller change; the nested actor state is not a complete health snapshot.
- **Verification:** `HealthReducerTests`, `UnityCombatRulesBridgeTests`, Strike/spell/Rage tests,
  `DungeonActorStateAdapterTests`, `DungeonAutosaveCoordinatorTests`, and lethal-damage PlayMode
  tests cover rules health, temporary-health restoration, party autosave HP/defeat, and encounter
  defeat flows. Missing: non-encounter legacy spell calls cannot run during an encounter by design.
- **Exact owner:** foundation `Health*.cs`; caller projection in
  `UnityHealthProjectionModule.cs` and `CreatureComponent.cs`.

### Stride and movement

- **Current behavior and entry points:** `RulesStrideAction` creates a
  `StridePathSelectionRequest` and dispatches `StrideActionOp`; planned/AI selectors use
  `ISelectionResolver`. `UnityStrideProjectionObserver` projects each committed `TokenMovedFact`.
  `UnityCombatRulesBridge.CreateExplorationStride` builds a temporary one-action composition for
  destination travel without encounter action spending.
- **Actual rules behavior:** Stride validates the entire path before cost, spends one encounter
  action, starts a Speed budget, applies alternating diagonal and terrain costs, enforces topology,
  corner and occupancy rules, allows explicitly authorized friendly crossings, commits each step,
  and preserves diagonal phase across consecutive Strides in the turn. Relocation is distinct.
- **Authority/persistent state:** positions, land speeds, movement budgets, occupancy decisions, and
  diagonal phase in `RulesState`; `GridTopology` is immutable per root. Unity tiles/transforms are
  projections and topology inputs.
- **Migration:** rules slice is migrated. `GridBase`, `StateStride`, Dijkstra, and exploration
  planners remain selection/presentation/caller infrastructure. Do not move route planning into
  rules. Bridge Stride-specific methods are a documented first-slice exception and should be
  replaced by general caller dispatch only during a coordinated caller cleanup.
- **Verification:** `MovementValueTests`, `MovementPathRuleTests`, `MovementPermissionTests`,
  `StrideRulesTests`, legacy `TestsState/StrideTests.cs`, and 37 exploration PlayMode fixtures.
  Expected assertions include no cost on invalid paths, one cost on valid Stride, exact committed
  cells/costs, topology replacement only between roots, and committed-boundary interruption.
- **Exact owner:** `StrideRules.cs` owns feature rules; generic `Movement*.cs` owns movement;
  `UnityStrideProjectionObserver.cs`, `StrideSelectionResolvers.cs`, and `RulesStrideAction.cs`
  belong to the Stride vertical feature. Exploration planners and dungeon runtime belong to the
  general caller lane.

### Strike, reload, ammunition, MAP, and attack presentation

- **Current behavior and entry points:** `UnityStrikeEncounterModule` prepares every weapon plus
  unarmed `StrikeItemDefinition`, equipment/ammunition state, and installed
  `RulesStrikeAction`/`RulesReloadWeaponAction`. `StrikeActionOp`, `ResolveStrikeOp`, and
  `ReloadActionOp` own rules resolution. `UnityStrikePresentationObserver` animates and formats the
  result.
- **Actual rules behavior:** target legality checks enemy identity, range, line of effect/cover, and
  current topology through `UnityStrikeContext`; attack checks use typed modifiers and normal or
  agile MAP; miss still spends and advances MAP; critical doubles base damage before deadly; fatal
  upgrades the base die and adds its extra die after doubling; weaknesses/resistances apply once per
  type; ranged weapons spend ammo and loaded state; reload consumes its action only when legal.
  Flanking supplies off-guard for qualifying melee attacks. `UnityPreparedStrikeDataAdapter`
  captures prepared `strike-damage` modifiers/adjustments and damage dice, the Thief melee ability
  substitution, Rage roll options, target-condition predicate options used by Sneak Attack, and
  weapon `other-tags` alterations. Infuse Vitality is not prepared data: `UnityStrikeContext`
  separately adds its vitality die when a legacy `SpellEffectController` already holds that effect.
  No installed production cast creates it. Raging Intimidation's action/feat `traits` alteration is
  evaluated only by `Pf2eRulesEngine.GetAlteredTraits`; no production caller invokes that helper, so
  it does not alter a reachable Strike or action today.
- **Authority/persistent state:** actions, MAP, `EquipmentState`, `AmmunitionState`, and health are
  rules-owned. Targeting, base statistics, defenses, prepared feature contributions, conditions,
  and flanking are currently captured from Unity for each legal resolution; those inputs are
  transitional, not duplicate state writers.
- **Migration:** the Strike workflow itself is migrated. Later feature migrations replace the
  named Unity captures; do not rewrite Strike or create a second attack pipeline. The old
  `Combat/Actions/Attacks/Strike.cs` and `AttackResultPipeline.cs` types have no production
  resolution entry point; only legacy effect/type dependencies remain. Delete them only after those
  consumers migrate, with no fallback.
- **Verification:** `StrikeRulesTests` asserts hit/miss/critical, deadly/fatal, defense ordering,
  targeting before cost, MAP, ammo/load/reload, and missing state rejection.
  `RulesStrikeUnityTests` asserts extraction, feature contributions, projections, and logs.
  `RulesStrikeIntegrationPlayModeTests` asserts FSM, animation order, AI/player target selection,
  shared spell MAP, HUD, and health. Missing: authoritative statistics/conditions/defenses and pure
  flanking/topology fixtures independent of Unity.
- **Exact owner:** `StrikeRules.cs` and `Rules/Unity/Strike/*` are the Strike vertical feature;
  `Rules/Unity/Attack/*` is shared attack presentation/data adaptation; `FlankingRule.cs` remains a
  separately migratable named rule. General action-bar callers own no Strike semantics.

### Rules-native spell action shell, Divine Lance, and Light

- **Current behavior and entry points:** `UnitySpellDefinitionCatalog` parses all spell JSON.
  `PreparedSpellBook` authorizes exact `SpellReference`/slot pools. `RulesCastSpellAction` dispatches
  `CastSpellActionOp`. The action validates definition, rank, variant, preparation, targets, and
  resources before costs. `UnitySpellcastingEncounterModule` enrolls slots, reconstructs supported
  legacy `SpellEffectController` timed effects as rules registrations, and installs supported
  actions. `UnityResolvedSpellCastPresentationObserver` and attack/light observers project outcomes.
- **Actual rules behavior:** cantrips cost actions but no slot; ranked spells atomically spend the
  exact authorized slot and actions; interruption after costs retains those committed costs;
  self-target effect directives ignore player-supplied target IDs. `ResolveSpellAttackOp` uses the
  shared attack check, typed damage/defenses, and shared MAP. Current cleric preparation exposes
  rules-native Light and Divine Lance: Light creates the `spell-effect-light` self effect and a
  Unity light while active; Divine Lance is a two-action, 60-foot, one-creature spell attack for
  `2d4` spirit damage.
- **Authority/persistent state:** `SpellSlotState`, action economy, active effects/bindings/timing,
  MAP, and health. `PreparedSpellBook` is immutable authorization input. Restored legacy timed
  effects are converted to paired rules effect/binding registrations and later projected back. The
  dungeon actor adapter does not capture general `RulesState.ActiveEffects`, bindings, or timing.
  Consequently, a reachable rules-native Light cast can trigger its normal action-boundary
  autosave, but the Light effect and its rules-owned duration state are omitted and do not survive
  reload.
- **Migration:** shell, the legacy-timed-effect restoration adapter, Divine Lance, and Light are
  migrated for implemented encounter behavior; this does not claim durable persistence for
  rules-native effects. The 82 other catalog definitions are not thereby implemented. Divine Lance
  is the only current definition that matches the generic attack parser. Other attack-tagged spell
  shapes are rejected by current target, range, overlay, or damage constraints; in particular,
  nonempty overlays reject Ignition and Telekinetic Projectile. No action should be installed until
  its actual behavior is supported.
- **Verification:** `PreparedSpellBookTests`, `CastSpellRulesTests`, `SpellAttackRulesTests`,
  `SpellAttackUnityTests`, and `SpellcastingPresentationPlayModeTests`. Expected assertions include
  exact slot ownership, no partial costs on invalid choices, costs retained after interruption,
  stale target rejection, attack MAP sharing, idempotent effect presentation/removal, and initial
  plus reinforcement installation. Missing: a catalog fixture that explicitly proves rejection of
  the current overlaid attack spell variants, and save/restore coverage plus a persistence contract
  for rules-native Light's effect, binding, and timing.
- **Exact owner:** generic shell in `SpellcastingContracts.cs`, `SpellcastingRules.cs`, and
  `SpellAttack*.cs`; spell-specific rules/adapters own their definitions and presentation.
  `UnitySpellcastingEncounterModule.cs` remains an integration hotspot modified only by the caller
  composition wave after feature adapters are ready.

### Rage and Quick-Tempered

- **Current behavior and entry points:** `UnityRageEncounterModule` contributes initial bindings and
  configures `RageRules`; `RulesRageAction` dispatches `RageActionOp`.
  `QuickTemperedInitiativeAssignedListener` reacts to committed initiative assignment.
- **Actual rules behavior:** ordinary Rage validates ownership and Fatigued restriction,
  spends one action, creates the Rage effect, grants source temporary HP, and ends after ten source
  turn boundaries. Quick-Tempered has distinct traits, additionally rejects Encumbered and heavy
  armor without Invulnerable Rager, is one-shot, can occur before the winner's first turn adapter,
  and does not pay the ordinary Rage action cost. Rage end/expiration/encounter close removes its
  temporary HP and records the
  source-specific immunity while preserving foreign temporary HP.
- **Authority/persistent state:** bindings/frequency, `RageEffectState`, effect timing, health and
  action economy. Initial binding installation uses an enrollment-time Rage input snapshot.
  Availability, validation, and Rage start instead call `UnityRageActorStateProvider`, which reads
  prepared ownership, current Unity conditions and armor, level, and Constitution on each request.
  Dungeon capture stores only `RageWasActive` alongside the separately captured temporary-health
  fields. Restore deliberately does not resume Rage: `NormalizeRestoredHealth` clears Rage-owned
  temporary HP, records its source immunity, and preserves temporary HP from another source.
- **Migration:** rules workflow is migrated. The live prepared-character, condition, armor, and
  statistic reads are transitional feature-owned Unity dependencies; replace them when their
  authorities migrate, without adding Rage fields to bridge/shared state. Treat the current
  end-on-reload normalization as an explicit persistence contract, not evidence that general active
  effects are serialized; changing it requires a separately approved product decision.
- **Verification:** `RageRulesTests`, `RulesRageUnityTests`, and
  `TestsState/Pf2eBarbarianSmokeTests.cs`; expected assertions cover atomic cost/effect/temporary HP,
  restrictions, Quick-Tempered timing/one-shot, expiration, suspension/outcome cleanup, initial and
  reinforcement behavior. `DungeonActorStateAdapterTests` asserts the special Rage autosave round
  trip removes Rage-owned temporary HP without restoring the effect. Missing: a rules-authoritative
  replacement for the live Unity inputs and coverage of condition, armor, or statistic changes
  between enrollment and action evaluation.
- **Exact owner:** `RageRules.cs`, `UnityRageActorStateProvider.cs`, `RulesRageAction.cs`, and the
  Rage enrollment adapter in `UnityEncounterModuleSet.cs` until it can move to its own adapter file.

## Transitional and unmigrated rules behavior

### Slowed

- **Current implementation/entry points:** imported passive `Slow` is applied by
  `Pf2eRulesEngine.ApplyCombatStartRules` through `DefinedAbilities`; it adds sourced `Slowed` to
  `Conditions`, installs a `ResetActionPointsEvent` listener that subtracts the tier, and clears
  reactions. `SlowedEncounterModule` calls `ActionController.CalculateTurnStartActions` as the
  ordered turn-start adapter.
- **Actual behavior:** current shipped passive applies Slowed 1 once, changes turn-start actions
  from three to two, and removes reactions. `DefinedConditions` declares tiers 1-3, but only the
  passive fixture is exercised. Unsigned subtraction is not a general stacking/value model.
- **Authority/state:** Unity listener and `Conditions.AppliedConditions`; rules state stores only the
  final per-turn action contribution. This is a current gap, not a second rules authority.
- **Necessary integration:** create a Slowed feature operation/listener or selector over generic
  condition state that contributes the exact turn-start action count. Enroll the sourced condition
  through the common combatant registration, make generic condition reducers the only writer, and
  remove the Unity event listener, reaction-clearing side effect, and turn-start adapter in the same
  coordinated change. Do not preserve the old listener as fallback.
- **Verification/fixtures:** `Pf2eRulesTests.ZombiePassiveSlowAppliesAtCombatStart` and
  `ZombiePassiveSlowDoesNotStackWhenCombatStartRulesRunAgain`,
  `GameUITests.CombatTrackerShowsReducedActionsForSlowedCreature`, and Rotting Aura turn-start tests
  that prove adapter order/skip. Add pure tests for tiers, lower bound, duplicate sources, removal,
  reinforcement, and explicit current reaction behavior before deciding whether reaction clearing
  is intended.
- **Exact future owner:** a new `SlowedRules` module under the existing
  `Assets/Scripts/Rules/Runtime` directory and a feature-owned Unity adapter under the existing
  `Assets/Scripts/Creature/Conditions/Implemented` directory; retire `SlowedEncounterModule.cs`
  and the Slowed callback in `DefinedConditions.cs`. Generic condition Ops/Facts, if approved, are
  foundation-owned rather than Slowed-owned.

### Rotting Aura

- **Current implementation/entry points:** creature JSON `Aura` data becomes `CreatureAura` in
  `CreatureDtoMapper`. `RottingAuraEncounterModule` invokes `CreatureAuraResolver` on each acting
  creature's turn start. `RottingAuraRule.Resolve` rolls `1 + max(0, source.level) / 6` d6 void,
  applies target weakness/resistance with legacy `DamageRoller`, and the adapter dispatches final
  generic health damage. Presentation logs the result.
- **Actual behavior:** each living aura owner affects only the acting, living creature when wounded,
  in the 10-foot aura, and not undead or construct. Defeated owners do not apply it. Damage can
  defeat the actor before its turn begins.
- **Authority/state:** aura definitions, traits, level, defenses, and range evaluation are Unity
  inputs; HP/defeat is rules-owned. No aura state exists in `RulesState`.
- **Necessary integration:** the Rotting Aura feature should own a binding created during
  enrollment, listen at the existing initiative/turn-start timing, query immutable source/target
  facts, roll through callback `IRollService`, resolve typed damage through shared operations, and
  keep its Unity log adapter feature-local. Whether generic aura geometry belongs in runtime is an
  unresolved decision; the first migration should keep a feature-owned topology adapter unless a
  second implemented aura proves a shared contract.
- **Verification/fixtures:** `Pf2eRottingAuraTests` asserts eligibility and adjusted damage without
  health mutation. `RottingAuraPlayModeTests` asserts order, one acting target, defeated owner,
  defeat/skip. `ProceduralDungeonScenePlayModeTests` asserts aura visual refresh. Add pure tests for
  deterministic dice, boundary identity, range/traits/defenses capture, and restored/reinforcement
  enrollment.
- **Exact future owner:** `RottingAuraRule.cs` during extraction, followed by a new
  `RottingAuraRules` module under `Assets/Scripts/Rules/Runtime` and a feature-owned Unity adapter
  under the existing `Creature/Rules/Auras` directory; `AuraGridVisuals.cs` remains presentation.
  Only the general caller worker changes `UnityEncounterModuleSet.cs` wiring.

### Conditions: sourced membership, Off-Guard, and Deafened

- **Current implementation/entry points:** `Conditions` stores a dictionary from string name to
  `ConditionSource` list, persists source snapshots, and supplies legacy modifiers.
  `ConditionModifierRules` maps Off-Guard/Flat-Footed to one -2 circumstance AC modifier.
  `UnityStrikeContext` reads target conditions for Off-Guard targeting and prepared predicate
  options. `UnityRageActorStateProvider` reads Fatigued and Encumbered, case-insensitively, for Rage
  and Quick-Tempered restrictions. `Pf2eRulesEngine` also reads target conditions for the dead
  legacy `AttackResultPipeline`, but that reader has no production resolution entry. Haunting
  Hymn's direct legacy branch adds Deafened on critical failure. Most methods in
  `DefinedConditions` are documentation-only no-ops.
- **Actual behavior:** sourced Slowed changes turn-start actions, Off-Guard/Flat-Footed contributes
  the legacy AC modifier and Strike/Sneak Attack targeting option, and Fatigued/Encumbered can block
  reachable Rage behavior. Deafened membership is executable only through Haunting Hymn's dormant
  direct-call branch and has no further mechanical consumer. Other declared conditions are
  unimplemented content and must not be migrated as if they worked.
- **Authority/state:** Unity `Conditions` plus dungeon save DTOs. Generic `ConditionState` exists in
  `RulesState` but is neither enrolled nor mutated by production operations.
- **Necessary integration:** the demonstrated consumers justify generic sourced add/remove
  condition Ops, reducers and Facts over the existing `ConditionState`, plus complete
  enrollment/persistence. Slowed, Off-Guard, Rage/Quick-Tempered restrictions, and any approved
  Deafened behavior remain feature semantics. Replace each Unity reader/writer with its owning
  vertical change; do not synchronize both stores.
- **Verification/fixtures:** `Pf2eModifierTests` covers stacking with cover and armor;
  `Pf2eRulesTests` covers Sneak Attack aliases; `RulesRageUnityTests` proves lowercase imported
  Fatigued blocks Rage and Encumbered blocks Quick-Tempered; dungeon actor/save tests cover sourced
  condition round trips. Missing: generic condition reducer tests, multiple-source removal,
  Deafened mechanics, and live persistence from rules state.
- **Exact future owner:** the foundation owns new `ConditionOperations`, `ConditionReducers`,
  `ConditionFacts`, and `ConditionRuleRuntime` types under `Assets/Scripts/Rules/Runtime`; the
  Off-Guard feature owns a new `OffGuardRules` module in that directory, and the owning spell
  feature owns Deafened application. The caller migration owns `DungeonActorStateAdapter.cs` and
  enrollment wiring. This proposal reuses `ConditionId` and `ConditionState`; it must not introduce
  another condition DTO without resolving source identity.

### Legacy UI character builder

- **Current implementation/entry points:** `CharacterCreationScript.OnEnable` loads
  `Assets/Resources/Data/ancestry.json` and `Assets/Resources/Data/class.json`. UI callbacks apply
  ancestry boosts/flaws, a free ancestry boost, background boosts, a class boost, and four final
  boosts; they also calculate HP from ancestry plus class and copy speed/size, class proficiencies,
  feats, and subclass choices into an in-memory `PlayerCharacter`. `FinishCreation` checks for null
  fields, displays warnings for ability modifiers above 4 without clearing its readiness result,
  and returns to the main menu whenever the null-field check succeeds. Its `JsonUtility.ToJson`
  calls only update a string or log debug output; none writes a character save.
- **Actual behavior:** this is a separate, UI-hosted character-build calculator. It does not load
  `playerCharacter.json`, save the created JSON, construct a `CreatureComponent`, call
  `CreatureJsonConverter`/`Pf2eCharacterPreparer`, or enroll a combatant. The only production
  references to `PlayerCharacter` are inside `CharacterCreationScript`; the scene and
  `CharacterCreatorTests` instantiate that UI. Its calculated values therefore are neither current
  encounter authority nor a usable prepared-character source.
- **Authority/state:** the mutable `PlayerCharacter`, attribute contribution dictionary, and UI
  selection fields are disconnected legacy state. Treating those results as presentation-only
  would hide real rule calculations, but treating them as live game behavior would overstate their
  reachability.
- **Necessary integration:** first make a product decision whether this creator is intended to
  become a playable-character source and which build rules it supports. If approved, the owning
  character-build feature must validate and persist one explicit build representation, feed that
  representation through the normal preparation/enrollment path, and remove duplicate UI-owned
  calculations as each rule becomes authoritative. Do not infer additional feats, choices, or
  validation from the UI and do not connect its current partial object to combat silently.
- **Verification/fixtures:** `CharacterCreatorTests` exercises tutorial flow and default-character
  navigation but does not prove build math, validation, serialization, preparation, or combat
  enrollment. An approved integration needs deterministic build fixtures and an end-to-end
  created-character preparation test.
- **Exact future owner:** the selected character-build feature owns
  `Assets/UIStuff/CharacterCreation/CharacterCreationScript.cs`, its DTO/data contract, and focused
  tests. The general caller integrator owns only the eventual handoff to
  `CreatureJsonConverter`/`Pf2eCharacterPreparer` and enrollment.

### Prepared characters, build choices, and item rule elements

- **Current implementation/entry points:** `CreatureJsonConverter` imports creature stats,
  equipment, action names, passives, auras, weaknesses/resistances, build selections, and health.
  `Pf2eCharacterPreparer.Prepare` loads class/subclass/feat grants, roll options, skill ranks,
  proficiencies, spell setup, and supported rule synthetics into mutable `PreparedCharacter`.
  `Pf2eRulesEngine` supplies prepared Strike contributions and trait alterations.
- **Actual behavior:** tested verticals are Barbarian/Rage/Fury/Quick-Tempered/Raging
  Intimidation data preparation; Rogue/Thief finesse/Sneak Attack; Cleric preparation and the
  limited spell lists; skill/class proficiency math; supported predicate `and`/`or`/`not`/`gte`,
  atomic options and skill-rank checks. Raging Intimidation's trait helper has direct EditMode
  coverage but no production caller, so this is not a reachable action-trait behavior. Toggleable/
  target roll options are deliberately not always active.
- **Authority/state:** `CreatureComponent`, `CharacterBuild`, and mutable `PreparedCharacter` remain
  pre-enrollment data/derived caches. Selected results are captured by feature adapters. They are
  not a general live rules-state slice.
- **Necessary integration:** do not migrate the entire Foundry item model. Each implemented feature
  owns extraction of the exact immutable values/bindings it needs at enrollment. Statistics caller
  migration may capture base attack/AC/saves/skills/modifiers into existing
  `CreatureStatisticsState`. Remove a prepared cache only when its final consumer migrates.
- **Verification/fixtures:** `Pf2eRulesTests`, `PreparedSpellBookTests`, `Pf2eModifierTests`,
  `RulesRageUnityTests`, and `RulesStrikeUnityTests`. Add a catalog audit test that explicitly
  reports data-only keys if future workers need stricter coverage; do not treat unsupported keys as
  failed implemented features.
- **Exact future owner:** `Pf2eCharacterPreparer.cs`, `PreparedCharacter.cs`, `Pf2ePredicate.cs`,
  and `Pf2eItemCatalog.cs` remain data adapters. Each vertical feature owns its converter into
  runtime values. The general caller/composition worker owns only shared statistics enrollment.

### Dormant legacy Unity spell implementations

`SpellRegistry` contains six non-rules-native spell implementations, but no installed production
action can reach them. Creature initialization initially adds legacy `CastSpellAction` instances;
encounter attachment removes all of them and installs only supported `RulesCastSpellAction`
instances, while `SpellcastingRuntime.Cast` rejects legacy resolution during an attached encounter.
Outside an attached encounter, `ActionController.ActionPoints` returns zero, so the normal legacy
action call with `spendActions: true` fails its affordability check. A direct programmatic call with
`spendActions: false` can reach some definitions, but that is an API-level capability rather than an
installed production action. Heal and Haunting Hymn ultimately use health APIs that require an
attached rules bridge, so even their direct unattached paths cannot complete. `SpellcastingState`
is mutable legacy slot state; production encounters instead enroll `SpellSlotState` from
`PreparedSpellBook`.

The table records dormant/direct implementation semantics as migration evidence, not shipped
behavior that must automatically be preserved. A product decision must explicitly select each
spell and its desired semantics before a vertical migration may install it.

| Spell | Dormant/direct implementation semantics | Required vertical migration and exact owner | Verification and missing coverage |
| --- | --- | --- | --- |
| Shield | Self; `ShieldSpellEffect` supplies +1 circumstance AC and expires at source turn start | New `ShieldRules` module under `Assets/Scripts/Rules/Runtime` plus feature-owned Unity selection/presentation adapter in `Combat/Spells`; use active effect/binding and modifier collection | Legacy behavior has indirect spell/effect coverage only; add pure AC stacking, duration, refresh, and enrollment tests |
| Guidance | Friendly target within 30 feet; +1 status to the first attack/save/skill/initiative query, mutates `Consumed`, creates indefinite Guidance Immunity, and expires Guidance at source turn start | `GuidanceRules.cs` plus feature adapter; consumption must occur through an Op/reducer or listener, never during a selector; immunity needs an explicit implemented duration decision | No representative end-to-end current test. Add target, one-consumption, stacking, expiry, and immunity fixtures before migration |
| Haunting Hymn | 15-foot cone; each affected creature makes basic Fortitude against caster spell DC for `1d8` sonic; critical failure also adds mechanically inert Deafened | `HauntingHymnRules.cs` plus area-selection adapter; dispatch save, typed damage, and condition Ops | `Pf2eAreaTargetingTests` covers cone geometry, but no direct spell fixture. Add all degrees, deterministic roll/damage, multiple targets, line of effect, and Deafened assertions |
| Bless | Captures friendly creatures in a 15-foot emanation at cast time; each receives +1 status attack for ten target turn starts | `BlessRules.cs` plus feature adapter. Preserve current snapshot-target behavior unless product explicitly chooses a live aura | No direct spell fixture. Add target set, stacking, refresh, ten-boundary expiry, and source/target defeat tests |
| Infuse Vitality | Its unreachable legacy selector prompts for exactly one friendly target within 30 feet for every 1-, 2-, or 3-action variant. Direct `Cast` accepts from one through `ActionCost` unique friendly targets. Each accepted target gains `1d4` vitality weapon/unarmed Strike damage for ten target turn starts through a legacy Strike adjustment | Do not assume either target contract is intended and do not add a missing multi-target selector. A product decision must choose target count/selection behavior first; only then may an `InfuseVitalityRules` feature use Strike damage middleware and active-effect timing | `RulesStrikeUnityTests.PreparedRageThiefSneakAttackAndInfuseContributeToRulesDamage` covers captured contribution, not a reachable cast. An approved feature needs selection/action-variant, duration, duplicate-target, and typed-damage tests |
| Heal | 1 action: target within 5; 2 actions: target within 30 and +8 healing for living target; 3 actions: 30-foot emanation. Rolls `1d8`; heals friendly living creatures, deals basic Fortitude vitality damage to undead | `HealRules.cs` plus feature area/target adapter; reuse health, save, typed damage, and action/slot operations | No direct current spell fixture. Add every variant, friend/undead/nonfriend, range, degrees, slot/cost atomicity, and deterministic roll tests |

For each explicitly approved spell, install its rules-native action and then delete its legacy
class/effect path and update `CastSpellAction`/`SpellRegistry` in the same change. No compatibility
dispatch by spell slug is allowed. Feature workers create their own rules and Unity adapter files;
the caller composition
worker alone updates `UnitySpellcastingEncounterModule.cs`, the action installer/catalog wiring,
and shared test fixtures after a batch is ready.

### Combat door opening

- **Current implementation/entry points:** `DungeonEncounterRuntimeController.TryPrepareDoorInteraction`
  selects the current combat actor (or an exploration party member), rejects an actor already taking
  an action, and checks combat turn authority. `DungeonDoorInteractionPolicy.Evaluate` then decides
  party membership, life state, cardinal adjacency, closed-door state, one-action combat cost, and
  affordability. `ApplyDoorInteraction` calls `DungeonDoorController.TryOpen` first, calls
  `ActionController.SpendActions` second, then refreshes rules topology and records/publishes the
  open-door state.
- **Actual behavior:** combat door legality and its one-action cost are rules behavior outside the
  `ActionOp` lifecycle. The current mutation order can open the door before action spending is
  committed. This is an architectural/atomicity gap to preserve in the inventory, not authority to
  correct gameplay in this documentation change. Exploration opening is intentionally free and
  remains caller policy.
- **Authority/state:** attached encounter actions are already rules-owned. Door open/collider state,
  stable-ID persistence, encounter-room activation, and map/line-of-sight refresh are Unity-owned
  world projection. `DungeonDoorController` and the KayKit map adapters do not become legality or
  action-economy authorities.
- **Necessary integration:** an explicitly approved Open Door feature should dispatch one action
  operation/profile so turn, actor, affordability, and cost share the normal validation/cost/handler
  lifecycle. Its resolved handler/observer may then open the Unity door, persist the stable ID,
  activate newly reachable rooms, and refresh topology in a defined post-commit order. Preserve the
  current combat criteria unless a separate product decision changes them; do not route exploration
  opening through combat action economy and do not add a general door-state slice without a proven
  rules consumer.
- **Verification/fixtures:** `DungeonDoorInteractionPolicyTests` covers mode, actor, life, adjacency,
  open state, cost, and affordability in isolation. Missing coverage is action-lifecycle atomicity,
  cost failure without world mutation, exact current-turn identity, and PlayMode verification that
  projection, persistence, reachable-room activation, and topology refresh follow a committed
  operation.
- **Exact future owner:** a feature-owned Open Door rules module/action under
  `Assets/Scripts/Rules/Runtime` and a narrow Unity adapter own the combat operation. The general
  dungeon caller owns `DungeonEncounterRuntimeController`; KayKit retains door visual/collider and
  map-topology projection.

### Teams, flanking, targeting, line of effect, cover, and areas

- **Current implementation/entry points:** `TeamRules` is a Unity singleton with directed
  friendly/neutral/hostile dictionaries. Encounter enrollment converts team names to stable
  `PlayerId`; `RulesSelectors.IsEnemy` compares those identities. Stride friendship maps registered
  team relations. `FlankingRule` discovers Unity combatants and evaluates opposite threatened
  sides. `StrikeTargeting`, `GridTargeting`, `GridLineOfSightData`, and `AreaTargeting` compute grid
  range, rays, cover/line of effect, and burst/cone/line/emanation cells. `LineOfSight.cs` itself is
  fully commented out and implements nothing.
- **Actual behavior:** Strike/spell adapters revalidate numeric range and line of effect at dispatch;
  cover contributes AC; melee flanking can make a target Off-Guard. Area results carry occupants,
  ally flags, line of effect, and cover. Selection previews are not authority.
- **Authority/state:** topology and positions are rules-owned during encounters, but ray/area
  calculations and team relationship setup remain Unity infrastructure. `PlayerId` is the current
  runtime side identity; there is no general disposition state.
- **Necessary integration:** keep generic geometry pure and adapter-owned until a rules feature
  needs an immutable result. Migrate Flanking as a named feature using snapshot roster/positions and
  the current topology capability, then remove Unity discovery. Do not add Flanking helpers to the
  bridge. Team setup is general composition/caller work, not a named rule migration.
- **Verification/fixtures:** `Pf2eAreaTargetingTests`, `Pf2eModifierTests`, KayKit map line-of-sight
  tests, `SpellAttackUnityTests`, Strike tests, and dungeon PlayMode geometry tests. Missing: pure
  snapshot-based flanking and a single shared immutable line-of-effect contract for Strike and
  spells.
- **Exact future owner:** a new `FlankingRules` module under `Assets/Scripts/Rules/Runtime` plus a
  feature-owned Unity topology adapter; general caller owns `TeamRules.cs`/team-to-`PlayerId`
  composition. Grid files remain topology/selection infrastructure unless a proven operation
  boundary requires movement.

### Creature statistics, defenses, traits, equipment, and data-defined actions

- **Current implementation/entry points:** `CreatureComponent` and `CreatureDtoMapper` own imported
  abilities, base attack values, per-weapon bonuses, AC, saves, skills, initiative, traits,
  weaknesses/resistances, equipment and ammo. `ResolveAttackRoll`, `ResolveArmorClass`, save/skill/
  initiative/DC helpers use the legacy modifier resolver. Strike/spell Unity adapters capture the
  exact values they need. Strike enrollment already converts weapon definitions, ammo, and loaded
  state to rules values.
- **Actual behavior:** typed bonus/penalty stacking is implemented; equipped armor and cover affect
  AC; imported weapon action bonus is an untyped attack base. Creature immunities are authored in
  JSON, but the importer DTOs expose no immunity field and `CreatureDtoMapper` does not map them, so
  `JsonUtility` discards those arrays. Item inventory, bulk, prices, materials, most traits, and most
  action/passive/reaction descriptions are data-only.
- **Authority/state:** equipment/ammo/load are migrated; base statistics, typed defenses, traits,
  condition-provided modifiers, and most inventory remain Unity/prepared inputs.
- **Necessary integration:** a general caller/composition migration may add
  `CreatureStatisticsState` to `CombatantRulesState`, `UnityCombatantEnrollmentBuilder`, and the
  atomic addition reducer, then switch general check consumers to `RulesSelectors`. Typed defenses
  and traits should remain immutable feature adapter inputs until more than Strike/spells need a
  persistent query. Do not add inventory or immunities for content with no behavior.
- **Verification/fixtures:** `Pf2eModifierTests`, `Pf2eRulesTests`, `RulesStrikeUnityTests`,
  `SpellAttackRulesTests`, creature catalog/materializer tests, and data fixtures listed above.
  Missing: production statistics enrollment, initiative-statistic choice beyond current imported
  Perception modifier, immunity import coverage, and executable immunity rules.
- **Exact future owner:** general caller/composition owns changes to `EncounterCombatantState.cs`,
  `EncounterReducers.cs`, `UnityCombatantEnrollmentPipeline.cs`,
  `UnityEncounterComposition.cs`, and a feature-agnostic statistics capture adapter. Strike/spell
  feature workers only remove their former captures after that integration lands.

### Authored but unimplemented actions, passives, reactions, feats, and spells

The creature fixtures include Goblin Scuttle, Scamper, Grab, Void Healing, and a kobold Sneak Attack
passive; the broader item catalog includes Rogue/Cleric feats and class features; the spell catalog
contains 84 spells. Runtime creature initialization installs only implemented Rage, supported spell
actions, and encounter Strike/reload actions. String action/reaction/passive lists are otherwise
display/import data. `DefinedConditions` no-op methods and unsupported rule keys are likewise not
behavior.

Future work must start from an explicitly selected vertical feature and its existing fixture. It
must not bulk-register these names, create generic placeholder operations, or infer PF2e scope from
descriptions. The exact feature worker owns its operation, validation, handler/listeners, state,
data extraction, Unity selection/presentation adapter, and targeted tests. General caller work only
wires the finished module and installed action.

## Non-rules subsystems requiring no migration

The following discovered functionality stays outside the rules runtime unless a future implemented
rule proves a narrower boundary:

- Dungeon generation, layout validation, decoration, encounters-by-XP planning, JSON serialization,
  and deterministic random substreams in `Assets/Scripts/DungeonGeneration`.
- Dungeon room lifecycle state (dormant/active/suspended/cleared), materialization, traversal,
  autosave repository mechanics, and scene recovery. These orchestrate encounters and persist
  projections; they do not replace encounter rules authority.
- Exploration party formation and route planning. The leader's individual Strides are rules-backed,
  while follower planning, stairs, exploration-mode doors, and encounter-boundary interruption are
  caller policy. Combat door legality/cost is the transitional rules behavior documented above;
  door visuals and topology projection remain outside the rules slice.
- The 16 KayKit runtime files provide animation/equipment presentation, dungeon document/catalog
  adapters, grid/line-of-sight topology, and door visual/collider state. They do not decide combat
  action legality or costs. Their topology/projection role is an integration dependency for Stride,
  Strike, spells, and a future approved combat-door action, not a reason to migrate the package as a
  whole.
- Grid rendering, hover/highlight FSM, generated input bindings, camera, animation, audio,
  combat-log formatting, non-character-build UI, token meshes, scene transitions, tutorial metadata,
  object pooling, and coroutine/event utilities.
- `DungeonEncounterCreatureCatalog.asset` is a Unity address catalog. `encounter-enemies.json` is
  dungeon generation content. Neither is rules state.

Persistence is still an integration dependency, but it is not one adapter-owned state blob.
`DungeonAutosaveCoordinator` schedules action-boundary and persistent-state saves. For party members
it writes current HP and defeat directly to the outer `DungeonPartyMemberSaveState`; for enemies,
`DungeonEncounterRuntimeController` and `DungeonEncounterDirector` write current HP only for living
enemy records and preserve defeated enemies as lifecycle identities. The nested
`DungeonActorStateAdapter` captures temporary HP amount/source/immunities, Unity conditions, legacy
`SpellEffectController` timed effects, prepared-character effects, equipment, ammunition, and the
rules-derived `RageWasActive` marker. On restore, its callers supply outer current HP and defeat,
creature content supplies maximum HP, and the adapter reconstructs one `HealthState` before
encounter enrollment.

Those legacy timed and prepared-effect fields are not a general serialization of rules-native
`ActiveEffects`, paired `RuleBindings`, or `ActiveEffectTimings`. Rage has a narrow marker used to
normalize its temporary health to an ended effect, while rules-native Light currently has no save
representation and is lost on reload. Whenever one of these slices crosses the rules authority
boundary, the general caller worker must switch every owning capture/restore caller and its fixtures
in the same coordinated change, preserve any intentional normalization such as Rage unless a
product change is approved, and remove the old writer. No schema compatibility layer is required
for unshipped formats; update schema, fixtures, and code together.

## Work ownership and integration order

### Worker lanes

| Lane | Owns | Must not own |
| --- | --- | --- |
| Foundation | Generic Ops/results/reducers/Facts/selectors proven by a current vertical need; existing dispatcher/action/effect/check/health/movement contracts | Named feature conditions, spell workflows, Unity presentation, or speculative state |
| Feature-rule migration | One named rule/action/spell, including its immutable data extraction, rule registrations, handlers/listeners, state, Unity selection/installation/presentation adapters, and narrow tests | General manager APIs, other features, or central feature switches |
| General caller/composition migration | `CombatManager`, controllers, UI/dungeon/persistence consumers, common enrollment DTO/reducer changes, `UnityEncounterModuleSet` ordering/wiring, and removal of obsolete general entry points | Reimplementation of feature legality, calculations, listeners, or presentation adapters |

### Safe implementation waves

1. Preserve and verify the existing foundation. Land no speculative expansion.
2. If approved, add generic sourced condition operations because multiple reachable features
   consume sourced membership: Slowed, Off-Guard/Sneak Attack, and Rage/Quick-Tempered. Keep
   Deafened application with an explicitly approved Haunting Hymn migration; its dormant direct
   branch alone does not justify enabling that spell. Do not attach Unity authority yet.
3. Migrate one named feature at a time. The feature worker creates feature files and tests without
   editing central composition until ready.
4. In a serialized integration wave, the general caller worker updates complete combatant
   registration, `UnityEncounterModuleSet`, `UnityCombatRulesBridge`, persistence, and general
   consumers. It removes the former writer/fallback in the same change.
5. Only after product selection, migrate a dormant legacy spell individually or as a deliberately
   small, reviewed batch. Its direct implementation semantics are inputs to that decision, not an
   automatic behavior contract. Never keep both `SpellRegistry` and rules-native encounter behavior
   authoritative for the same spell.
6. Delete dead legacy attack/effect types only after `rg` proves no remaining compiled consumer.

### Hotspots that must not be edited concurrently

| Hotspot | Why it conflicts | Exclusive owner during integration |
| --- | --- | --- |
| `Assets/Scripts/Rules/Unity/Composition/UnityEncounterModuleSet.cs` | Defines rule IDs, action catalogs, module order, and Rage adapter | General caller/composition worker |
| `Assets/Scripts/Rules/Unity/UnityCombatRulesBridge.cs` | Dispatcher construction, identity maps, root queues, topology, enrollment entry points, release | General caller/composition worker |
| `EncounterCombatantState.cs`, `EncounterReducers.cs`, `UnityEncounterComposition.cs`, `UnityCombatantEnrollmentPipeline.cs` | Complete atomic registration and both addition routes | Foundation contract author plus one caller integrator, never independent feature workers |
| `UnitySpellcastingEncounterModule.cs`, `UnitySpellcastingComposition.cs`, `CastSpellAction.cs` | Shared spell enrollment/action reconciliation/restoration | One spell caller integrator after feature adapters land |
| `CreatureComponent.cs`, `CreatureJsonConverter.cs`, `Pf2eCharacterPreparer.cs`, `PreparedCharacter.cs` | Common imported/prepared data and projections | One data/caller integrator; feature workers supply isolated converters |
| `ActionController.cs`, `CombatManager.cs`, `DungeonActorStateAdapter.cs`, save DTOs | General authority, lifecycle, and persistence callers | General caller/persistence worker |
| `UnityStrikeContext.cs` and `UnityAttackDataAdapter.cs` | Current Unity capture for several independently migratable inputs | Strike integrator removes captures only after replacement authorities exist |

## Unresolved decisions and current gaps

These are decisions to make before the named migration, not implicit authorization to add scope:

1. **Condition source identity:** `ConditionState` currently contains one `ConditionId`, owner and
   source but does not model Unity `ConditionSource` object identity or multiple same-name sources.
   Define the minimal stable source contract before condition enrollment.
2. **Slowed reaction behavior:** current `Slow` clears reactions in addition to reducing actions.
   Confirm whether this is intended shipped behavior before encoding it in rules.
3. **Guidance immunity duration:** the legacy immunity is indefinite. Define the intended current
   behavior before migration; do not silently "correct" it from external rules text.
4. **Bless semantics:** current code snapshots friendly targets at cast time rather than maintaining
   a moving aura. Preserve this behavior unless a separate product/rules change is approved.
5. **Statistics registration:** the generic slice exists but complete combatant registration omits
   it. Decide the smallest immutable capture and coordinate it with Strike/spell readers.
6. **Defenses and traits:** weaknesses/resistances and traits are immutable Unity captures today.
   No demonstrated writer requires a new shared persistent slice yet.
7. **Flanking topology boundary:** prefer a feature-local immutable query over a new shared
   topology API unless another implemented feature proves the same need.
8. **Dungeon save boundary:** health persistence is split among the autosave coordinator/director,
   outer party or enemy/lifecycle records, and the nested actor adapter; condition and legacy effect
   persistence remains Unity-shaped. General rules-native effects/bindings/timing have no save
   representation, so Light currently does not survive reload, while Rage intentionally normalizes
   to an ended effect. Each approved durability or authority change requires a coordinated breaking
   schema/caller/fixture update; do not silently change Rage semantics, add compatibility versions,
   or create dual restore paths.
9. **Data-only catalog scope:** 84 loaded spell definitions and unsupported item rule keys are not an
   implementation backlog by themselves. A human must select any additional vertical feature.
10. **Tracked `.orig` files and commented `LineOfSight`:** these are cleanup gaps, not migration
    foundations or executable fallbacks.
11. **Legacy character-creation authority:** decide whether the disconnected UI builder is intended
    to create playable characters and which build rules it owns before connecting or migrating it.
    Its partial `PlayerCharacter` must not silently become a second preparation format.
12. **Dormant spell semantics:** select each legacy spell explicitly before enabling or migrating
    it. In particular, choose Infuse Vitality's target-count/selection behavior instead of treating
    either its one-target selector or its direct-call multi-target acceptance as authoritative.
13. **Combat door operation boundary:** the current criteria and one-action cost are known behavior,
    but moving them into the action lifecycle requires an approved Open Door vertical and coordinated
    projection/persistence tests; this plan does not perform that gameplay correction.

## Verification contract for future migrations

For every mapped vertical feature:

1. Preserve a representative real JSON/creature fixture named above; do not construct only a
   hypothetical rules example.
2. Add deterministic EditMode tests at the reducer/handler/selector layer using
   `ScriptedRollService` (or save/restore Unity random state at a Unity boundary).
3. Assert invalid requests leave action/resource/state versions unchanged; assert committed Facts,
   structural outcomes, operation order, and exact persistent state for valid requests.
4. Add bridge tests for initial enrollment, reinforcement enrollment, rollback, exact ownership,
   and release whenever feature state or an installed action is involved.
5. Add PlayMode coverage for selection, scene lifecycle, FSM/action completion, animation,
   projection, UI, or component installation.
6. Update persistence tests when authority crosses the save boundary. Assert the exact owner of
   outer current HP/defeat, nested temporary-health state, required rules-native
   effect/binding/timing durability, and intentional feature normalization such as Rage.
7. Remove the old writer and fallback in the same change, then use `rg` to prove no remaining call
   path. Coordinated breaking changes are required for unshipped formats.
8. Run Unity `6000.2.1f1` EditMode and PlayMode suites without `-quit`; store results outside
   `Assets`. Inspect the complete diff, generated files, and serialized changes.

At this inventory base, saved NUnit XML reports dated 2026-09-13 record 877 of 878 baseline EditMode
tests passing. The sole failure was
`DungeonRunMenuServiceTests.MissingCorruptAndCompatibleAutosavesDriveContinueStatus`, whose message
reports that its temporary `autosave.json` could not be removed for replacement. The saved PlayMode
report records all 190 tests passing. These are real baseline artifacts for the inventory date, not
a clean current-head gate; each implementation must produce its own current-head evidence.
