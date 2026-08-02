# Authoritative Encounter Rules Architecture

This is the canonical as-built guide for encounter rules in the Unity `6000.2.1f1` project. Use it
when changing encounter lifecycle, combatant enrollment, a rules-backed action, or a feature module.
The longer [operations-based rules architecture](Ops_Based_Rules_Proposal.md) explains the design
model and contains conceptual examples; this guide records what production code does now.

The rules migration is intentionally incomplete. The claims below apply to encounter state and the
named migrated action slices, not to every gameplay system or every conceptual example in the
proposal.

## Authority after encounter cutover

`UnityCombatRulesBridge` owns one `RuleDispatcher` and its `RulesState` for an encounter. Once a
controller and creature are attached to that exact bridge, the following state is authoritative in
`RulesSnapshot`:

| Slice | Authoritative rules state and boundary |
| --- | --- |
| Turn ownership | `EncounterState.CurrentTurn` and exact `TurnIdentity`; `ActionController.HasTurnAuthority` is a read projection. |
| Actions and reactions | `ActionEconomyState`; `ActionController.ActionPoints` and `Reacted` project it. |
| Multiple attack penalty | `MultipleAttackPenaltyState`; `ActionController.StrikePenalty` projects its attack count. |
| Health | `HealthState`; `CreatureComponent.Health`, `hp`, `maxHp`, and `tempHp` read it while attached. Health Facts project committed values and presentation back to Unity. A private temporary-Hit-Point pool revision distinguishes exact internal mutations for rollback safety but is deliberately absent from public equality, Facts, outcomes, and persistence. |
| Position and movement | `RulesSnapshot.Positions`, movement budgets, permissions, and movement reducers. Token movement is a committed-Fact projection. |
| Encounter roster, initiative, round, and outcome | `EncounterState`, its roster and cursor, durable published-boundary turn-start checkpoint, and encounter reducers/listeners. `CombatManager` orchestrates and presents this state; it is not a second encounter scheduler. |
| Active-effect timing | `ActiveEffectInstance` and `ActiveEffectTimingState`, advanced at encounter initiative boundaries. |
| Prepared rule participation | `RulesSnapshot.PreparedInputs` owns normalized creature facts and `RulesSnapshot.RuleBindings` alone controls whether definition-owned compiled behavior participates. `PreparedRulePackage` is only the ephemeral compiler result used to seed those slices. |
| Migrated action slices | Stride, Strike, Reload, Rage, and supported Cast a Spell variants use rules operations, validation, action lifecycle, reducers, and state. |

Cutover never means “try rules, then fall back.” A detached `ActionController` exposes deliberately
unavailable read projections (`HasTurnAuthority == false`, zero action points, `Reacted == false`,
and zero MAP). A rules-backed action reports unavailable when `TryGetCombatRules` fails. Operations
that require authority, such as positive action spending or health mutation, throw when the bridge
is absent or structurally incomplete. The bridge performs that operational check before allocating
a health-origin identity, so blocked gameplay cannot change the provenance of a later committed
health Fact. `CreatureComponent.Health` may still read serialized or
initialized health before attachment and after final projection; that is an initialization and
persistence boundary, not competing combat authority.

Legacy-named `CombatManager` entry points still exist for scene compatibility, and many gameplay
features remain Unity-native. They must not dual-write a migrated slice or revive a legacy fallback.

## Production composition

[`UnityEncounterModuleSet`](../Assets/Scripts/Rules/Unity/Composition/UnityEncounterModuleSet.cs)
constructs the only production module sequence:

1. `UnityPreparedRulesEncounterModule`
2. `RottingAuraEncounterModule`
3. `ConditionEncounterModule`
4. `SlowedEncounterModule`
5. `UnityRageEncounterModule`
6. `UnityStrikeEncounterModule`
7. `UnitySpellcastingEncounterModule`
8. `UnityLightEncounterModule`
9. `UnityHealthProjectionModule`
10. `UnityEncounterProjectionModule`

Before constructing that module array, `UnityEncounterModuleSet.Create` performs a separate,
explicit static-composition pass that feature modules cannot defer to `ConfigureDispatcher`:

- It constructs `CombatActionCatalog` from `strideDefinition`, `strikeContext`, `spellCatalog`,
  `new UnitySpellBookProvider(creatures)`, and the Rage feature catalog, `rageDefinition`. The
  result implements `IActionCatalog`, `IStrikeActionCatalog`, and `ISpellActionCatalog`.
- It composes a `RuleRegistryBuilder` with every deduplicated catalog-backed
  `PreparedRuleDefinitionSpec`, `RageRules.DefineRuleBindings(registryBuilder)`,
  `registryBuilder.AddOutcomeRule()`,
  `registryBuilder.Define(UnitySpellcastingEncounterModule.RestoredTimedEffectDefinitionId)`, and
  each distinct spell-effect `DefinitionId` from `spellCatalog.Definitions`, then calls
  `registryBuilder.Build()`.

`RuleRegistry` is immutable. `CombatActionCatalog` is instead a stable composed interface over
encounter-live adapters. Both are constructed before `UnityCombatRulesBridge` supplies them to
`UseActionLifecycle(modules.ActionCatalog)`, `UseActiveEffectRules(modules.Registry)`, and
`UseEncounterRules(..., modules.Registry)`, and before any module's `ConfigureDispatcher` pass.
The shared registry instance keeps Unity enrollment and encounter join reducers under the same
definition authority. The catalog's composed capabilities are stable, but
combatant-specific data remains encounter-live:
`UnityStrikeContext.Register` adds item definitions during reinforcement preparation, and
`UnitySpellBookProvider` reads the live creature map. Do not snapshot combatant-specific catalog
data during static composition. This is an allowed named composition-root responsibility: the root
may mention Rage and spell definitions to wire feature-owned catalogs and IDs, but it does not
implement their conditions or workflow and does not permit static discovery or self-registration.

Prepared definitions are compiled from the complete PF2e item catalog before registry construction,
so a reinforcement cannot introduce an unknown definition. Per-creature compilation then seeds
deterministic stateless bindings and `PreparedCreatureInputs` into rules state; Unity retains no
package. Generic typed collection operations run definition-local middleware against the same
operation snapshot used by registry selection. Enable, disable, and remove operations therefore
affect the next snapshot without rebuilding the registry. Effect-backed bindings remain exclusively
owned by active-effect lifecycle operations.

[`UnityEncounterComposition`](../Assets/Scripts/Rules/Unity/Composition/UnityEncounterComposition.cs)
copies that explicit sequence and never scans assemblies, scene objects, statics, or attributes.
Each pass filters the same sequence by capability, preserving supplied order:

| Capability | Responsibility |
| --- | --- |
| `IUnityEncounterDispatcherModule.ConfigureDispatcher` | Register feature-owned handlers, reducers, validators, and engine composition with `RuleDispatcherBuilder`. |
| `IUnityEncounterTurnStartModule.CreateTurnStartAdapter` | Supply a transitional `IEncounterTurnStartAdapter`. Adapters run sequentially in module order. |
| `IUnityEncounterRuntimeModule.RegisterRuntime` | Register observers and other encounter-scoped resources into the supplied `CompositeLifetime`. |
| `IUnityEncounterTopologyModule.RefreshTopology` | Refresh a feature-owned Unity topology adapter after a live grid change. |
| `IUnityCombatantEnrollmentModule.PrepareCombatant` | Precompute state and Unity installation contributions for every enrolled combatant. |

Implement only the capabilities a module needs. Ordering dependencies must be visible in the module
list or expressed through rules lifecycle phases and causal operations; do not invent priorities or
registration side effects.

## Construction responsibilities and order

`UnityCombatRulesBridge.Create` performs these boundaries in order:

1. Create the mutable topology provider, shared feature contexts, catalogs, registry, explicit
   module set, composition, and enrollment pipeline.
2. Call `UnityCombatantEnrollmentPipeline.Prepare` for all initial participants.
3. Call `UnityCombatantEnrollmentPlan.SeedInitial` into one `RulesStateSeed`. Prepared active
   effects are part of each combatant's immutable state. Initial enrollment uses the strict
   `AddUniqueActiveEffect`, `AddUniqueRuleBinding`, and `AddUniqueActiveEffectTiming` APIs so a
   duplicate effect, binding, or timing identity rejects instead of replacing an earlier value.
4. Configure shared runtimes on `RuleDispatcherBuilder`: health, MAP, checks, active effects,
   encounter rules, action lifecycle, movement, and Stride.
5. Invoke `ConfigureDispatcher` for feature modules in module order, build the dispatcher, then
   invoke `RegisterRuntime` in module order.
6. Call `AttachAndInstall`.
7. Call `FinalizeBatch`, then transfer the prepared plan to the encounter's single
   `CompositeLifetime`. The transfer applies the already validated, non-failing finalizations only
   after ownership succeeds.

`AttachAndInstall` is deliberately after authoritative state and runtime observers exist. For each
combatant it attaches health authority first, attaches `ActionController` combat authority second,
and applies already prepared feature installations last, in module contribution order. Do not move
Unity attachment or action-list mutation into preparation. This post-authority Unity mutation is an
adapter responsibility, not rules authority: installation plans may reconcile Unity action lists
from frozen prepared data, and post-commit projections such as `UnityHealthProjectionModule` may
update Unity health and presentation from committed Facts. Neither may mutate authoritative
`RulesState` or maintain a competing feature mirror.

A rules-native spell action permanently closes over its action definition and catalog. Spell
installation may retain an existing action only when both references belong to the current
encounter catalog; matching spell and variant values alone do not prove ownership. Fresh encounter
composition therefore replaces actions left by a failed or released encounter, including when
registration order reassigns the controller's creature ID.

### One cleanup boundary

`UnityCombatRulesBridge` owns exactly one encounter-level `CompositeLifetime`. Encounter-scoped
runtime observer registrations and every successfully transferred enrollment plan belong to it.
`ReleaseOwnership` disposes it once in reverse registration order, attempts all cleanup even after
failures, and only then runs release callbacks. Modules must add disposable registrations and
resources to the lifetime they receive; they must not keep a second encounter cleanup list or
manually unregister observers.

That ownership rule applies to registrations and resources intended to remain active for the
encounter. A temporary observer that exists for one rules root owns its registration locally and
disposes it as soon as that root completes. `DispatchProjectedStride` intentionally uses a local
`using` registration for this reason; adding that observer to the encounter lifetime would leak its
delivery into later roots.

`ActionController.DetachCombatRules` and `CreatureComponent.DetachHealthRules` use
`ReferenceEquals` against the owning `UnityCombatRulesBridge`. Delayed cleanup from an older
encounter therefore cannot detach a newer bridge or overwrite its health projection. Preserve this
exact identity check at every ownership-release boundary.

## Combatant enrollment

[`UnityCombatantEnrollmentPipeline`](../Assets/Scripts/Rules/Unity/Composition/UnityCombatantEnrollmentPipeline.cs)
is the one path for constructor-time participants and later reinforcements.

### Reversible preparation

`Prepare` validates the complete controller batch, reserves creature/player identity allocation,
creates Unity-to-rules maps provisionally, validates future attachments, invokes every enrollment
module, captures initiative modifiers, and freezes `CombatantRulesState` plus installation plans.
`CombatantRulesState.ActiveEffects` is the exhaustive generic payload for prepared active-effect,
binding, and optional timing state; the pipeline does not inspect feature or condition types.
`UnityCombatantEnrollmentBuilder` exposes the supported contribution APIs:

- `Own<TResource>` for reversible preparation resources;
- `AddState(IUnityCombatantStateContribution)` for state that supports both initial seeding and
  reinforcement registration;
- `AddSpellSlots` and `AddRuleBindings` for atomic base combatant state;
- `AddActiveEffects` for effect, binding, and optional timing registrations that must join
  atomically with the roster;
- `AddInstallation(IUnityCombatantInstallationContribution)` for precomputed Unity changes;
- `AddOwnershipRelease(IUnityCombatantOwnershipReleaseContribution)` for a final immutable
  feature projection while the exact rules authority is still attached;
- `AddFinalization(IUnityCombatantBatchFinalizationContribution)` for one-shot input that may be
  consumed only after every attachment and installation in the batch succeeds.

If any preparation read or validation fails, the preparation `CompositeLifetime` rolls back maps,
identity allocation, and feature-owned resources. Cleanup failures are retained with the original
failure. Preparation must therefore perform every fallible Unity query needed by `Reconcile`.
`RegisterCombatants` checks ownership both before and immediately after it materializes the caller's
enumerable, before `Prepare`; an iterator that begins release cannot cause provisional feature reads
after ownership is gone.

This is not a promise that arbitrary work after a reinforcement commit can roll back `RulesState`.
The commit boundary is intentional. A root dispatch or state registration can commit and then
surface an observer failure, so every reinforcement checkpoint must accept an exact replay as a
no-op without new Facts or a version change while rejecting different committed state.
Installation contributions have the same retry requirement at the Unity boundary: `Reconcile`
must converge feature-owned projection from a partially changed live state without duplicating
entries, skipping removals, or disturbing another feature's entries. It must not repeat fallible
discovery or validation.

### Initial participants versus reinforcements

Both routes call the same `PrepareCombatant` methods in the same module order and build the same
`CombatantRulesState`.

`UnityPreparedRulesEncounterModule` is the single prepared-rules enrollment capability. It creates
the package seeds with the enrolled `CreatureId` and contributes them through `AddRuleBindings` for
both routes. JSON, mutable build choices, and Unity components do not cross that boundary; runtime
predicates and collectors receive only the frozen package, typed current context, and authoritative
snapshot.

- Initial participants call `SeedInitial`. Base creature, health, position, land speed, action
  economy, MAP, spell slots, bindings, and feature contributions enter the seed before the
  dispatcher exists. Initiative is rolled later by `StartEncounterOp`.
- Reinforcements call `CommitReinforcements`. One `JoinEncounterOp` atomically adds each prepared
  `CombatantRulesState` (including spell slots, base bindings, and prepared active effects) to the
  active encounter, rolls initiative, and assigns `EligibleFromRound` so a higher-than-current
  result waits until the next round. The join reducer first validates every base registration, then
  stages the complete future roster and base slices on one draft before adopting all active effects
  in participant order through the shared generic adoption reduction. Owner and timing validation
  therefore see the full future roster independent of participant order. Any effect, binding,
  timing, registry, or owner rejection discards the entire draft and its Facts, rolls back prepared
  Unity state, and leaves the enrollment gate clear for a corrected batch. Unrelated
  `IUnityCombatantStateContribution` objects, such as Strike equipment and ammunition, retain
  their later exact-replay registration workflows.

The encounter stores the immutable reinforcement registration receipt separately from mutable live
combatant state, and checkpoints initiative-assignment publication separately from roster joining.
An exact replay therefore converges both checkpoints even when roster Fact delivery failed, while
state legitimately changed by an assignment listener does not make the original registration look
conflicting. A post-commit roster observer failure is preserved for the caller only after the
assignment checkpoint is attempted, so retry cannot alter causation, Facts, or version.

Assignment-triggered feature workflows must close their own uncertain commit boundaries before a
child failure escapes. Quick-Tempered never infers completion from the actor's current temporary
Hit Points or immunity. The feature creates an exact root-derived Rage effect in `Pending` phase.
The public `GrantTemporaryHitPointsOp` remains the single observable THP boundary and traverses the
complete middleware chain, including Prevention. Only after that chain allows resolution does the
health handler ask its immutable feature intent for the commit operation; Rage's feature reducer
then atomically records both the health transition and exact grant outcome in the pending effect.
A second feature reducer advances the receipt to `Settled` before the public child action returns.
Failures after either commit are recovered only from that exact actor,
effect, binding, origin, trigger, and causal-root identity; every captured observer or middleware
failure is rethrown after convergence. Quick-Tempered consumes its trigger only from the same-root
`Settled` receipt, so a child resolved observer or post-next middleware cannot strand an already
completed Rage. A thrown public THP failure is recoverable only when that exact grant checkpoint
already exists; an `Invalid` or interrupted result remains authoritative pipeline rejection and can
never invoke the private feature reducer as a bypass. Settlement is likewise dispatched once and a
thrown failure is recoverable only from the exact already-committed `Settled` receipt. It is never
retried merely because that receipt is absent. The public THP operation is not replayed after its
reducer checkpoint and runs once on ordinary success. A failed grant or pre-commit settlement
promptly aborts its exact Pending receipt while preserving the original and cleanup failures. If
the Rage grant displaced a lower pool, that same atomic abort restores the recorded prior amount
and stable source only when the private pool revision and public amount/source still exactly match
the committed Rage-owned pool. Restoration changes only the pool, preserves unrelated current Hit
Points and immunities, and advances the private revision. If a later revision owns a foreign pool
or the pool was fully consumed, that newer state wins and the abort removes the Pending effect pair
without resurrecting prior temporary Hit Points. If a later revision still has a nonempty
Rage-owned pool, including an identical-looking ABA pool, cleanup rejects atomically and retains the
Pending effect pair for diagnosis rather than claiming ownership of the newer Rage pool. A
same-revision amount/source mismatch is invariant corruption and also rejects without cleanup
Facts. A no-op grant leaves health untouched. Expiration, explicit end, and encounter end recognize
any remaining Pending tombstone and remove only that effect pair: they never remove temporary Hit
Points or add Rage immunity until a start reached `Settled`. Once the published assignment receipt
exists, Join retry remains a true no-op and cannot replay the feature's Facts or versions.
`IsRaging` exposes only settled effects, while validation rejects another start in either pending
or settled phase and requires registered, non-defeated authoritative health before manual or
Quick-Tempered action costs and effect creation.

After either state path, call `AttachAndInstall`, `FinalizeBatch`, then
`TransferTo(encounterLifetime)`. Finalization first validates every contribution; successful
ownership transfer then applies the non-failing contributions and consumes one-shot input. A
reinforcement plan whose store registration already committed remains the exact pending batch
after a later failure. Retry resumes at the uncertain checkpoint: an already committed Join or
state contribution resolves as an exact no-op, and an installation reconciles the prepared result.
This does not reroll initiative, duplicate Facts, or advance the store version. While such a plan is
pending, unrelated bridge dispatches fail closed and turn authority reports unavailable so a
partially enrolled roster cannot participate in play. Ownership release rejects new registration
before preparation and disposes any pending plan once. A new feature must support both paths; never
assume all participants existed at encounter construction.

Ownership-release contributions are added after controller attachment, so reverse lifetime cleanup
projects them before controller and health detachment. Conditions use this boundary to copy their
final exact applications into a feature-owned detached value after encounter-scoped cleanup has
settled. The detached value retains no bridge, store, or snapshot. Its explicit validity is
independent of application count: an authoritative empty set is valid persistence input. It is also
the next encounter's one-shot adoption input and is consumed only by batch finalization; empty input
adds no adoption state contribution but still participates in finalization. Cleanup remains
best-effort and aggregates projection and detachment failures. A detached exploration-action
composition neither adopts nor consumes this input because it does not own Unity combat authority
and cannot project an encounter snapshot during release.

Each detached condition also retains its immutable dungeon `SourceActorId`. The bridge maintains a
scoped, one-to-one mapping between configured dungeon identity components and encounter
`CreatureId` values. An actor with neither `DungeonPartyMemberIdentity` nor
`DungeonEncounterMember` is intentionally nondurable and receives no mapping. Component presence
is an identity assertion: a present component must be completely configured, its serialized
durable ID must pass `DurableActorSourceIdentity.RequireCanonical` unchanged, and the two component
types are mutually exclusive even when either is unconfigured. Party actors map their exact
`RosterSlotId`. Generated enemies retain their floor-local `InstanceId` for encounter lifecycle and
save dictionaries, but map provenance through the versioned
`dungeon-enemy-v1/<floor-depth>/<instance-id>` identity computed by `DungeonEncounterMember`.
`DungeonEncounterMaterializer` requires the document's explicit nonnegative generation depth and
configures it on every member. The bridge validates both the raw `InstanceId` and computed durable
identity without normalization. Party identities may not occupy the reserved `dungeon-enemy-v1/`
namespace. Enrollment fails and rolls back the batch when any identity invariant is violated.
Condition provenance must likewise be a nonempty canonical durable ID; it is never trimmed,
inferred from the owner, or replaced with the owner. Only the exact scoped enemy identity maps to a
live enemy. An older unscoped enemy string, or the same local instance ID from another depth,
remains an absent historical source. An absent source receives a deterministic, reversible
base64url `CreatureId` in a reserved namespace disjoint from live `combat-creature-*` identities,
and
release decodes that identity back to the exact original durable string without a global registry.
An active absent-source indefinite condition remains active and selectable by its target. An active
absent-source finite condition is normalized before adoption to one expired, disabled tombstone
with no timing and exactly the next effect version; an already expired condition retains its
version and remains disabled with no timing. The adoption Fact contains that normalized state and
no separate expiration Fact is emitted. Dungeon save-graph validation treats condition provenance
as historical and therefore does not require its source on the current floor; timed spell-effect
sources retain their current-floor membership requirement. These adapter rules keep Unity objects
and persistence types out of the generic rules runtime.

### Restored-effect adoption

`UnitySpellcastingEncounterModule` is the production example of state that crosses both enrollment
routes. During preparation it converts supported `SpellEffectController` entries into
`RestoredSpellEffectContribution` objects with stable `ActiveEffectId` and `BindingId` values.
The contribution retains projection lifetime ownership and adds its generic registrations to
`CombatantRulesState.ActiveEffects`. Initial participants seed those registrations directly;
reinforcements adopt them inside the same join reduction that commits roster and base state.
`ActiveEffectAdoptedFact` retains the exact feature effect and binding payload, never publishes
`ActiveEffectCreatedFact`, and therefore cannot trigger gameplay-creation listeners. When adoption
occurs inside Join, the Fact envelope has encounter/join operation provenance while the payload
retains feature provenance; no source override is used.
`RestoredSpellEffectTimingObserver` projects initiative-boundary counts and removes expired or
removed Unity effects. Do not bypass the active-effect runtime for restored effects.

## Dispatcher and encounter runtime

`RuleDispatcher` owns operation frames, external-root serialization, nested dispatch, causal
fact-listener roots, deterministic middleware/listener selection, Fact aggregation, and settlement
notifications. Handlers orchestrate; reducers are the only writers to `RulesStateDraft`; committed
Facts are the notification contract.

Before every allocation, the default dispatcher operation-ID sequence rebases strictly after the
greatest authoritative `CreationOrder` in all current rule bindings, including disabled tombstones,
and active-effect timings. Exhaustion in the initial snapshot rejects construction; exhaustion
introduced by later reinforcement or adoption rejects the next allocation. Explicitly injected ID
providers retain their supplied sequence unchanged. Condition creation additionally allocates from
at least its frame ID and strictly above the current binding/timing maximum, probing both
`condition-effect-N` and `condition-binding-N` until an available pair is found. This
condition-local collision probing does not mutate the dispatcher provider and fails closed at
exhaustion.

[`EncounterRuleRuntime`](../Assets/Scripts/Rules/Runtime/EncounterRuleRuntime.cs) installs the
encounter handlers and engine reducers. Its current division of responsibility is:

- `StartEncounterHandler`: roll initiative through `IRollService`, retain registration-order ties,
  commit the roster, publish initiative assignments, and trigger the first boundary causally.
- `JoinEncounterHandler`: validate an active turn, roll reinforcement initiative, atomically
  commit full combatant and active-effect state, and publish assignments from a later frame so new
  bindings can observe them. Listener selection remains frozen: newly committed bindings cannot
  observe the Join frame, but can observe that later initiative-assignment frame.
- `AdvanceEncounterHandler`: settle pending expirations, outcomes, initiative boundaries, skipped
  or ineligible roster slots, and effect timing in deterministic order. When
  `EncounterState.IsTurnStartPending` is set, advancement resumes `BeginInitiativeTurnOp` for the
  exact already published actor, round, and roster slot instead of consuming another boundary.
- `BeginInitiativeTurnHandler`: reset movement budget, run ordered turn-start adapters, stop if the
  actor is defeated, then commit the exact turn and final action contribution. The published-boundary
  checkpoint stores the next adapter index and current contribution; each adapter result commits
  before the next adapter runs, so a fresh-root recovery resumes only unfinished adapters. The engine
  checkpoints a successfully returned adapter, not partial internal work: an adapter with multiple
  commits must itself be transactional or exact-replay-safe on internal failure. The narrow
  `CommitFinalDamageBatchAndCompleteAdapter` boundary lets a transitional adapter atomically commit
  an ordered same-actor damage batch and its completion checkpoint before fallible presentation;
  an adapter using it may perform only presentation before returning the checkpointed contribution.
  A completed
  zero-HP turn-start attempt atomically clears the published-boundary checkpoint with
  `InitiativeTurnStartSkippedFact` before deferred defeat reactions run, so rescuing the actor does
  not replay adapters for the already resolved slot.
- `EndTurnHandler`: require the exact current `TurnIdentity`, run turn-end work, reset movement,
  clear turn resources through reducers, and advance. It may retry a safe pre-publication advance
  checkpoint inside the same root, but an already published turn-start checkpoint must unwind so
  deferred Fact notification finishes first. `UnityCombatRulesBridge.EndTurn` then dispatches the
  generic `AdvanceEncounterOp` recovery from a new host root only when the old turn committed, the
  encounter remains active, and no current turn exists. Successful recovery still reports the
  original failure; a recovery failure is aggregated with it. An already begun turn or ended
  encounter is never replayed.
- `EncounterOutcomeListener`: after reaction-phase zero-HP listeners settle, finalize defeat and
  evaluate encounter outcome.
- `SuspendEncounterHandler` and `EndEncounterHandler`: expire encounter-owned timed effects before
  committing suspension or outcome. Feature Prevention middleware must complete feature-owned
  tombstone cleanup before either lifecycle transition commits.
- Encounter reducers and the shared reducers they invoke atomically mutate roster, initiative
  boundary, current turn, actions, reactions, MAP, movement reset state, phase, and outcome while
  emitting committed Facts.

`ActionOp<TResult>` uses the engine-owned lifecycle implemented by `RuleDispatcher`: freeze the
effective profile, validate, commit all costs atomically, dispatch `ActionBegunOp`, stop on
disruption, and only then invoke feature middleware and the handler. Feature code must not spend the
same costs or publish a parallel action-begun event.

## Projection, settlement, and topology

`UnityEncounterProjectionModule` observes `EncounterStartedFact`, `TurnBeganFact`, `TurnEndedFact`,
and `EncounterOutcomeCommittedFact`. Turn and outcome callbacks are queued by `RootOpId`, not raised
immediately. Its one settlement observer implements both `IRootSettlementObserver` and
`ICausalTreeSettlementObserver`: root settlement records exact causal-parent links, and causal-tree
settlement drains the root followed by children in recorded order. This ensures turn/outcome Unity
presentation occurs only after fact-listener roots such as defeat finalization, healing reactions,
and encounter outcome evaluation have settled. A duplicate settled root or an unsettled recorded
child root is an invariant failure.

Other projections use the narrowest contract:

- `IFactObserver<TFact>` for committed transitions and their current snapshot;
- `IResolvedOpObserver<TOp,TResult>` only to pace external presentation from a resolved calculation
  before its parent continues; it is not proof of mutation and has no dispatch authority;
- `IRuleFactListener<TFact>` or `IRuleFactBatchListener<TFact>` for binding-selected rules behavior
  that may causally dispatch more operations.

`UnityCombatRulesBridge.RefreshTopology` first builds and installs a new immutable `GridTopology`,
then calls every `IUnityEncounterTopologyModule` in module order and updates `CurrentTiles`.
`MutableGridTopologyProvider` rejects replacement while a rules resolution is active, so one root
sees one topology. Current Strike and spell-attack Unity contexts also refresh their feature-owned
tile adapters through this capability. Refresh topology after any live grid mutation and before the
next rules root.

## Recipe: add or migrate a vertical feature

Follow these steps in order. Use the existing production examples instead of adding a parallel
framework.

1. **Define the rules-owned slice.** Put immutable Ops, results, validators, handlers, reducers,
   Facts, selectors, and typed effect state with the feature. Use `ActionOp<TResult>` for a PF2e
   action and ordinary `IRuleOp<TResult>` for nested work. `StrideRules`, `StrikeRules`,
   `RageRules`, and `SpellcastingRules` show current contracts.
2. **Register dispatcher behavior explicitly.** Implement
   `IUnityEncounterDispatcherModule.ConfigureDispatcher(RuleDispatcherBuilder)` and call existing
   `Use...Rules` extensions or exact `RegisterHandler`, `RegisterReducer`, and
   `RegisterActionValidator` APIs. `UnityStrikeEncounterModule` and
   `UnitySpellcastingEncounterModule` are production examples.
3. **Model per-combatant enrollment for both routes.** Implement
   `IUnityCombatantEnrollmentModule.PrepareCombatant(UnityCombatantEnrollmentBuilder)`. Add base
   slots/bindings directly, add every prepared effect/binding/timing through `AddActiveEffects`,
   or implement `IUnityCombatantStateContribution.Seed` and `Register` only for unrelated state
   that genuinely needs a later exact-replay workflow. Use `Own` for every provisional disposable.
4. **Precompute Unity installation.** If the feature changes action lists or adapters, return an
   `IUnityCombatantInstallationContribution` from preparation. Its `Apply` method may only apply
   frozen work after rules authority is established. It may mutate Unity installation state, such as
   action lists, but has no rules authority and must not maintain a competing feature mirror.
   `UnityStrikeActionInstallationPlan` and `UnitySpellActionInstallationPlan` are the examples.
5. **Register presentation with encounter ownership.** Implement
   `IUnityEncounterRuntimeModule.RegisterRuntime(RuleDispatcher, CompositeLifetime)` and add every
   encounter-scoped registration/disposable to that lifetime. Keep a root-scoped observer locally
   owned and dispose it after that root. Strike uses resolved-operation observers for attack pacing;
   health and Light use committed Fact observers.
6. **Add topology or turn-start capabilities only if required.** Implement
   `IUnityEncounterTopologyModule` for a live geometry adapter. Use
   `IUnityEncounterTurnStartModule` only for a transitional Unity-owned calculation that cannot yet
   be a rules feature; Rotting Aura and Slowed are current seams, not templates for new rules.
7. **Complete static composition, then add the module once.** In
   `UnityEncounterModuleSet.Create`, define every `RuleDefinitionId` used by an
   `ActiveRuleBinding` or `ActiveEffectInstance` on the production `RuleRegistryBuilder` before
   `registryBuilder.Build()`. `RageRules.DefineRuleBindings(registryBuilder)` defines Rage's effect
   and listener bindings; spell composition explicitly defines
   `UnitySpellcastingEncounterModule.RestoredTimedEffectDefinitionId` and every distinct
   `effect.DefinitionId` from `spellCatalog.Definitions`.

   Also compose every action-profile dependency before dispatcher construction.
   `ActionOp<TResult>.GetBaseProfile(IActionCatalog)` defaults to
   `catalog.GetBaseProfile(DefinitionId)`, and the dispatcher freezes that profile before
   validation. A feature using that default must implement `IActionCatalog` and pass its catalog to
   the production `CombatActionCatalog`; `RageActionDefinition` and the `rageDefinition` constructor
   argument are the current example. An override that needs a typed catalog must have that
   capability composed too, as `CombatActionCatalog` does for `IStrikeActionCatalog` and
   `ISpellActionCatalog`.

   Finally, add the feature module to the explicit module array. Its array position is its position
   in every applicable composition pass. These named root references are wiring, not feature
   semantics; do not move rules into the root or self-register.
8. **Test the vertical boundary.** Add deterministic EditMode tests for reducers, handlers,
   lifecycle, initial seed, reinforcement registration, failure rollback, ordering, exact identity,
   and cleanup. Add PlayMode coverage only for scene/FSM/presentation behavior. Verify unavailable
   projections and required-operation failures without a bridge.
9. **Update this guide only if the shared architecture changed.** Feature-specific behavior belongs
   with its feature documentation and tests; do not duplicate the conceptual proposal here.

## Anti-patterns

- No legacy fallback, dual reads, dual writes, or “rules when available” mutation for a migrated
  slice.
- No static discovery, singleton lookup, static event registration, scene scanning, or feature
  self-registration during composition.
- No manual cleanup list or unregister path for encounter-scoped module observers and resources;
  add them to the provided encounter `CompositeLifetime`. Root-scoped temporary registrations stay
  locally owned and must end with their root.
- No new feature-named conditions, caches, trigger flags, dispatch helpers, or workflow semantics in
  `UnityCombatRulesBridge`, `RuleDispatcher`, shared managers, or generic projection modules. The
  existing Stride bridge helpers are the transitional exception described below, not a template.
- No feature that handles only initial participants. Reinforcements must receive equivalent state,
  bindings, installations, topology behavior, and lifetime ownership.
- No Unity object, callback, or mutable collection in a rules Op; translate to stable IDs and plain
  data at the adapter boundary.
- No direct authoritative `RulesState` mutation from handlers, middleware, listeners, observers,
  installers, or Unity components, and no competing feature-owned mirror. Dispatch an Op and let a
  reducer commit. Precomputed post-authority Unity installation and post-commit Unity projection are
  allowed adapter mutations; neither has rules authority.
- No treating `IResolvedOpObserver` as a Fact or granting it dispatch authority.
- No topology refresh during a root and no cached topology that ignores
  `IUnityEncounterTopologyModule.RefreshTopology`.

## Current transitional seams

The encounter is authoritative without being fully rules-native. Keep these seams explicit and
shrink them through vertical migrations:

- `RottingAuraEncounterModule` calls the Unity aura resolver at `TurnStartingOp` timing. It resolves
  every applicable aura before mutation, then atomically commits the ordered final-damage batch and
  adapter checkpoint through encounter rules before presentation, so post-commit failure cannot
  replay any aura damage.
- `SlowedEncounterModule` obtains the current action contribution through
  `ActionController.CalculateTurnStartActions` and legacy `ResetActionPointsEvent` listeners.
- `UnityStrikeContext` and `UnitySpellAttackContext` adapt current creature/equipment/team/grid data
  into rule definitions and validation. They are feature-owned adapters, not alternate authorities.
- Strike reinforcement replay compares the complete actor-owned equipment and ammunition
  collections plus the contribution-owned zero MAP state. Exact state is a no-op; extra, missing,
  or changed state rejects instead of being accepted as the original registration.
- Encounter preparation still reads serialized `CreatureComponent`, `ActionController`, `Team`,
  immutable prepared packages, spellbook, and restored-effect data to build authoritative state.
  Mutable `PreparedCharacter.ActiveEffects` persistence and spell pools/preparations remain explicit
  later-slice boundaries; migrated ownership, modifiers, dice, alterations, skills, predicates,
  options, diagnostics, definitions, and binding seeds do not read those boundaries.
- Unity action classes, selection coroutines, AI controllers, animation, combat logs, HUD, and scene
  transforms remain input/presentation adapters. Supported spell and Strike installers reconcile
  action lists at attachment.
- `CreateExplorationStride` creates a temporary, unattached rules composition for movement outside
  initiative and projects its committed boundary position before encounter composition begins.
- `UnityCombatRulesBridge` still has the first-slice Stride fields and
  `GetStrideAvailability`, `CreateStrideSelectionWorkflow`, `DispatchStride`, and
  `DispatchProjectedStride` helpers. They are a transitional exception to the feature-agnostic
  bridge direction and must not be copied, expanded, or used to justify new feature helpers; new
  slices should use feature-owned adapters and the bridge's generic dispatch boundary. Stride's
  asynchronous dispatch enters that same guarded boundary: pending enrollment and released
  ownership fail closed, and ownership release requested by a callback waits for the root to exit.
- Other actions, conditions, feats, spells, equipment behaviors, exploration systems, and scene
  flows not named as migrated above can still be Unity-native. Migrate them one vertical slice at a
  time; do not describe the whole game as rules-native.

## Tests that define the contract

Start with these suites when changing the architecture:

- [`UnityCombatRulesBridgeTests`](../Assets/Tests/EditMode/UnityCombatRulesBridgeTests.cs): module
  order, shared enrollment, restored effects, rollback, exact attachment, cleanup, health, and
  movement projection.
- [`EncounterRuntimeTests`](../Assets/Tests/EditMode/RulesRuntime/EncounterRuntimeTests.cs): roster,
  initiative, reinforcement eligibility, turn lifecycle, outcome, causal reactions, and effect
  timing.
- [`DispatcherTests`](../Assets/Tests/EditMode/RulesRuntime/DispatcherTests.cs): structural results,
  `CompositeLifetime`, serialized roots, nested ownership, Facts, and settlement.
- [`RulesStrikeUnityTests`](../Assets/Tests/EditMode/RulesStrikeUnityTests.cs),
  [`RulesRageUnityTests`](../Assets/Tests/EditMode/RulesRageUnityTests.cs), and
  [`RulesStrikeIntegrationPlayModeTests`](../Assets/Tests/PlayMode/RulesStrikeIntegrationPlayModeTests.cs):
  current production feature-module and projection examples.
