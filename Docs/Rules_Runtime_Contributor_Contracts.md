# Rules Runtime Contributor Contracts

This guide is a practical handoff for contributors extending the existing rules runtime. Read the
[durable design](Rules_Runtime_Design.md) first and use the
[encounter implementation guide](Encounter_Rules_Architecture.md) for the current production map.
Those documents and production code remain authoritative; this guide explains how downstream
feature and caller work should use their contracts without expanding the foundation speculatively.

## Choose the owner before changing code

Every change belongs to one lane. If a proposed change crosses lanes, split the work or coordinate
it with the exclusive owner instead of making a partial integration.

| Lane | Owns | Does not own |
| --- | --- | --- |
| Foundation | Feature-agnostic operations, structural results, reducers, Facts, selectors, and dispatcher contracts required by reachable behavior | Named rule conditions, Unity presentation, feature workflow, or state for hypothetical consumers |
| Feature | One named action, rule, feat, spell, or condition and its operations, validation, handlers, listeners, selectors, immutable extraction, and Unity adapters | Shared manager or bridge switches, unrelated features, or separate initial/reinforcement paths |
| General caller/composition | Module order, complete combatant registration, controller/UI/dungeon callers, persistence boundaries, and removal of replaced general entry points | Reimplementation of feature legality, calculations, reactions, or presentation decisions |

The general caller/composition lane is the exclusive integration owner for
`UnityEncounterModuleSet`, `UnityCombatRulesBridge`, the common enrollment pipeline and complete
combatant registration, general combat controllers, and persistence DTOs. A feature worker may
prepare an isolated module or adapter, but must not edit those shared entry points independently.
Conversely, the caller integrator wires a completed feature and removes its old caller; it must not
copy the feature's rules into composition code.

## Feature contract

A downstream feature should follow this boundary:

1. Convert Unity objects or authored data into immutable, Unity-free values in a feature-owned
   adapter.
2. Dispatch a typed operation. Use `ActionOp<TResult>` when the interaction spends action or rule
   resources; the engine owns validation and atomic cost commitment for that lifecycle.
3. Keep named orchestration in the feature handler. Dispatch existing generic operations for
   health, checks, damage, movement, resources, bindings, or effects instead of reproducing their
   reducers.
4. Return a structural outcome. An ordinary illegal request is `InvalidOpResult<TResult>`; a
   resolved miss or other unsuccessful game outcome is still a resolved result.
5. React to committed state with a feature-owned Fact listener when more rules work is required.
   A rules-backed action emits `ActionBegunFact<TResult>` after costs and the action-begun window,
   then `ActionResolvedFact<TResult>` after its awaited child mechanics. Register feature visuals
   through `IUnityActionPresenter<TOp, TResult>`; keep generic health, hit, and defeat projection in
   the shared projectors that already own it.
6. Add pure deterministic EditMode tests at the lowest owning layer. Add bridge or PlayMode tests
   only when enrollment, attachment, lifecycle, selection, or presentation is part of the change.

The current Strike entry point is a concrete example of the public dispatch shape:

```csharp
OpResult<StrikeResolution> result = await dispatcher.Dispatch(
    new StrikeActionOp(actor, item, target)
);
```

`StrikeActionOp(CreatureId, ItemId, CreatureId)` derives from `ActionOp<StrikeResolution>` and
supplies its fixed definition through `GetBaseProfile`. Its `StrikeActionHandler.Handle` implements
`IOpHandler<StrikeActionOp, StrikeResolution>`: after the engine-owned action boundary, it awaits
`OpHandlerContext.Dispatch` for `ResolveStrikeOp`, `ApplyDamageOp`, loaded-state changes, and MAP,
then returns the feature outcome value. These are executable production contracts in
`StrikeRules.cs`, not placeholder APIs. Follow the same separation for a new feature while using
that feature's real immutable values, validators, and result type.

## Caller and composition contract

The caller integrator completes a feature's production boundary in one coordinated change:

1. Define every feature-used `RuleDefinitionId` and compose every required action profile or typed
   catalog before dispatcher construction.
2. Add the feature explicitly to `UnityEncounterModuleSet` in a reviewed deterministic order. Give
   it only the capability interfaces it actually needs; modules never self-register or rely on
   Unity discovery order.
3. When state or installed actions belong to a combatant, contribute the complete immutable state
   and precomputed installation through the common enrollment plan. Initial participants and
   reinforcements use the same `AddCombatantsOp` commit path.
4. Perform fallible Unity reads during reversible preparation. After the authoritative addition
   commits, installation applies precomputed changes; a later notification or installation error
   does not roll back committed rules state or identity reservations.
5. Transfer encounter-scoped registrations and resources to the encounter `CompositeLifetime`.
   Keep temporary root-scoped registrations locally owned. Detach only the exact bridge that was
   attached.
6. Remove the former writer and fallback in the same integration. Do not synchronize two writable
   representations of a migrated state slice.

This is an exclusive boundary: a feature-only change should stop before central composition,
enrollment, controller, or persistence edits. A caller-only change should consume a completed
feature contract rather than inventing its semantics.

## Determinism contract

Determinism is observable behavior, not only a testing convenience:

- Use the callback-scoped `IRollService`; deterministic fixtures use `ScriptedRollService`, while
  separately constructed `RandomRollService` instances with the same seed repeat the same sequence
  without sharing state.
- Copy caller-owned mutable collections when constructing operations or state values.
- Order registrations and outcome-affecting collections explicitly by stable identity or semantic
  phase. Do not depend on scene discovery, hash iteration, current culture, or lifecycle timing.
- Keep selectors pure over one `RulesSnapshot`. Do not consume resources, mutate effect state, or
  perform Unity work during a query.
- Assert structural results, exact Facts and order, state versions, persistent state, and remaining
  scripted rolls rather than checking only the final number.

Data-backed dice and distance parsers accept their documented invariant shapes regardless of the
process culture. Localized or malformed shapes return `false` and reset their `out` value; adapters
must treat that as invalid authored data instead of silently substituting a rules value.

## Invalid requests and errors

Use the result or failure form that preserves the operation boundary:

| Situation | Required behavior |
| --- | --- |
| Expected illegal choice, stale target, missing affordability, or failed validation | Return `InvalidOpResult<TResult>` with no cost, state-version change, or committed Facts |
| Player cancellation | Return `CancelledOpResult<TResult>` and leave authoritative state unchanged unless an earlier documented boundary already committed |
| Rules interruption after atomic costs | Return `InterruptedOpResult<TResult>` and preserve the already committed costs |
| Miss, successful save, or another ordinary game outcome | Return a resolved result whose outcome describes what happened |
| Broken composition, impossible invariant, or external infrastructure failure | Throw; do not translate it into an ordinary invalid rules choice |

Reducers validate against their draft and stage Facts only for a committed transition. A rejected
reduction must leave every slice, state version, Fact identity, and observer untouched. If an error
occurs after a documented commit boundary, report it without pretending the durable state rolled
back.

## Minimum handoff evidence

For a downstream change, record:

- the owning lane and any exclusive integration owner involved;
- the old and new writable authority for each changed state slice;
- focused deterministic tests for valid and invalid paths, including exact state versions and
  Facts;
- initial and reinforcement enrollment evidence when combatant state or actions are installed;
- persistence and PlayMode evidence when those boundaries change; and
- proof that the former writer and fallback were removed when authority migrated.

If no reachable feature demonstrates a missing shared contract, do not add a foundation API. A
short document or regression test that preserves an existing contract is a complete wave-one
change.
