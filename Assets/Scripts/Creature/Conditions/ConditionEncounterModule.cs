using System;
using System.Linq;
using Game.DungeonPersistence.Actors;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;
using UnityEngine;

namespace Game.Creature.Rules
{
    /// <summary>Adapts condition applications to common enrollment and committed-Fact projection.</summary>
    internal sealed class ConditionEncounterModule
        : IUnityEncounterDispatcherModule,
            IUnityEncounterRuntimeModule,
            IUnityCombatantEnrollmentModule
    {
        private readonly UnityCombatRulesBridge owner;
        private readonly ConditionSource displaySource = new();

        internal ConditionEncounterModule(UnityCombatRulesBridge owner) => this.owner = owner;

        internal static void Apply(
            GameObject target,
            ConditionState state,
            RuleSource source,
            ConditionSource displaySource
        )
        {
            RuleSource applicationSource =
                displaySource != null
                && displaySource.TryGetReplaySource(out RuleSource replaySource)
                    ? replaySource
                    : source;
            Conditions display =
                target.GetComponent<Conditions>() ?? target.AddComponent<Conditions>();
            ActionController controller = target.GetComponent<ActionController>();
            if (
                controller != null
                && controller.TryGetCombatRules(
                    out UnityCombatRulesBridge bridge,
                    out CreatureId actor
                )
            )
            {
                OpResult<ActiveEffectCreationOutcome> result = bridge.Dispatch(
                    new ApplyConditionOp(
                        actor,
                        state.Condition,
                        state.Value,
                        actor,
                        applicationSource,
                        EffectDuration.Indefinite
                    )
                );
                if (result is not ResolvedOpResult<ActiveEffectCreationOutcome>)
                    throw new InvalidOperationException("Condition application did not resolve.");
                // Legacy passive import uses this exact source object to avoid applying twice.
                if (!display.Contains(state.Condition.Value, displaySource))
                    display.Add(state.Condition.Value, displaySource);
                return;
            }
            ConditionSeed seed =
                target.GetComponent<ConditionSeed>() ?? target.AddComponent<ConditionSeed>();
            if (!HasPendingPassiveReplay(target, state, applicationSource, displaySource))
                seed.ApplyBeforeAttachment(state, applicationSource);
            display.Add(state.Condition.Value, displaySource);
        }

        private static bool HasPendingPassiveReplay(
            GameObject target,
            ConditionState state,
            RuleSource source,
            ConditionSource displaySource
        )
        {
            if (
                displaySource == null
                || !displaySource.TryGetReplaySource(out RuleSource replaySource)
                || replaySource != source
            )
                return false;
            DungeonRulesEffectSeed restored = target.GetComponent<DungeonRulesEffectSeed>();
            if (restored == null)
                return false;

            // Only an explicitly identified passive replay may consume its matching restored
            // application. Ordinary ConditionSource instances always create independent effects.
            bool Matches(
                IEffectState candidate,
                RuleSource effectSource,
                RuleSource bindingSource
            ) =>
                effectSource == source
                && bindingSource == source
                && candidate is ConditionState condition
                && condition.Equals(state);

            if (
                restored.Projections.Any(projection =>
                    projection.Effect.DefinitionId == ConditionRules.DefinitionId
                    && projection.Binding.DefinitionId == ConditionRules.DefinitionId
                    && projection.Binding.EffectId == projection.Effect.Id
                    && projection.Effect.SourceCreature == projection.Binding.Owner
                    && Matches(
                        projection.Effect.State,
                        projection.Effect.Source,
                        projection.Binding.Source
                    )
                )
            )
                return true;

            return restored.Effects.Any(effect =>
                string.Equals(
                    effect.DefinitionId,
                    ConditionRules.DefinitionId.Value,
                    StringComparison.Ordinal
                )
                && effect.SourceActor.Equals(effect.BindingOwnerActor)
                && string.Equals(effect.RuleSource, source.Slug, StringComparison.Ordinal)
                && DungeonRulesEffectPersistence.Codecs.Restore(
                    ConditionRules.DefinitionId,
                    effect.StateKind,
                    effect.StatePayload,
                    _ =>
                        throw new InvalidOperationException(
                            "A condition payload cannot contain an actor reference."
                        )
                )
                    is ConditionState condition
                && condition.Equals(state)
            );
        }

        /// <inheritdoc/>
        public void ConfigureDispatcher(RuleDispatcherBuilder builder) =>
            ConditionRules.ConfigureDispatcher(builder);

        /// <inheritdoc/>
        public void RegisterRuntime(RuleDispatcher dispatcher, CompositeLifetime lifetime)
        {
            Projection projection = new(this);
            lifetime.Add(dispatcher.RegisterFactObserver<ActiveEffectCreatedFact>(projection));
            lifetime.Add(dispatcher.RegisterFactObserver<ActiveEffectStateUpdatedFact>(projection));
            lifetime.Add(dispatcher.RegisterFactObserver<ActiveEffectRemovedFact>(projection));
        }

        /// <inheritdoc/>
        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder)
        {
            ConditionSeed seed = builder.Controller.GetComponent<ConditionSeed>();
            var applications =
                seed == null
                    ? new System.Collections.Generic.List<(
                        ConditionState State,
                        RuleSource Source
                    )>()
                    : seed.Applications.ToList();
            Conditions display = builder.Controller.GetComponent<Conditions>();
            var represented = new System.Collections.Generic.HashSet<string>(
                applications
                    .Select(value => value.State.Condition.Value)
                    .Concat(
                        builder
                            .ActiveEffects.Where(effect => effect.State is ConditionState)
                            .Select(effect => effect.GetState<ConditionState>().Condition.Value)
                    ),
                StringComparer.OrdinalIgnoreCase
            );
            // A restore/detach transport is a complete authority snapshot, including absence.
            // Its leftover display names must never recreate removed or expired applications.
            if (
                display != null
                && builder.Controller.GetComponent<DungeonRulesEffectSeed>() == null
            )
                foreach (var imported in display.CaptureApplications())
                    if (!represented.Contains(imported.ConditionId))
                        applications.Add(
                            (
                                new ConditionState(new ConditionId(imported.ConditionId), 1),
                                imported.Source != null
                                && imported.Source.TryGetReplaySource(out RuleSource source)
                                    ? source
                                    : RuleSource.FromSlug("imported-condition")
                            )
                        );
            // Freeze before the addition commit: its Facts will update the Unity projection.
            for (int i = 0; i < applications.Count; i++)
            {
                var application = applications[i];
                var identity = builder.CreateActiveEffectIdentity(
                    "condition-seed",
                    $"{builder.CreatureId.Value}:{i}"
                );
                builder.AddActiveEffects(
                    new[]
                    {
                        new ActiveEffectInstance(
                            identity.EffectId,
                            ConditionRules.DefinitionId,
                            builder.CreatureId,
                            application.Source,
                            EffectDuration.Indefinite,
                            application.State
                        ),
                    }
                );
                builder.AddRuleBindings(
                    new[]
                    {
                        new ActiveRuleBinding(
                            identity.BindingId,
                            ConditionRules.DefinitionId,
                            builder.CreatureId,
                            identity.EffectId,
                            application.Source,
                            i
                        ),
                    }
                );
            }
        }

        private void Project(
            CreatureId target,
            ConditionId changedCondition,
            RulesSnapshot snapshot
        )
        {
            ActionController controller = owner.GetController(target);
            ActiveEffectInstance[] applications = ConditionRules
                .GetApplications(snapshot, target)
                .ToArray();
            ConditionSeed seed =
                controller.GetComponent<ConditionSeed>()
                ?? controller.gameObject.AddComponent<ConditionSeed>();
            seed.Project(applications);

            Conditions display =
                controller.GetComponent<Conditions>()
                ?? controller.gameObject.AddComponent<Conditions>();
            // The legacy display persists names only. Mechanics and individual lifetimes stay in effects.
            if (ConditionRules.GetValue(snapshot, target, changedCondition) == 0)
                display.Clear(changedCondition.Value);
            else if (!display.Contains(changedCondition.Value))
                display.Add(changedCondition.Value, displaySource);
        }

        private sealed class Projection
            : IFactObserver<ActiveEffectCreatedFact>,
                IFactObserver<ActiveEffectStateUpdatedFact>,
                IFactObserver<ActiveEffectRemovedFact>
        {
            private readonly ConditionEncounterModule module;

            internal Projection(ConditionEncounterModule module) => this.module = module;

            public void OnFactCommitted(
                ActiveEffectCreatedFact fact,
                OpId rootId,
                RulesSnapshot snapshot
            )
            {
                if (fact.DefinitionId == ConditionRules.DefinitionId)
                    ProjectCurrent(fact.EffectId, fact.BindingId, snapshot);
            }

            public void OnFactCommitted(
                ActiveEffectStateUpdatedFact fact,
                OpId rootId,
                RulesSnapshot snapshot
            )
            {
                if (fact.DefinitionId != ConditionRules.DefinitionId)
                    return;
                ActiveRuleBinding binding = snapshot
                    .RuleBindings.Single(entry => entry.Value.EffectId == fact.EffectId)
                    .Value;
                ProjectCurrent(fact.EffectId, binding.Id, snapshot);
            }

            public void OnFactCommitted(
                ActiveEffectRemovedFact fact,
                OpId rootId,
                RulesSnapshot snapshot
            )
            {
                if (fact.DefinitionId == ConditionRules.DefinitionId)
                    module.Project(
                        fact.Binding.Owner,
                        fact.Effect.GetState<ConditionState>().Condition,
                        snapshot
                    );
            }

            private void ProjectCurrent(
                ActiveEffectId effectId,
                BindingId bindingId,
                RulesSnapshot snapshot
            ) =>
                module.Project(
                    snapshot.RuleBindings[bindingId].Owner,
                    snapshot.ActiveEffects[effectId].GetState<ConditionState>().Condition,
                    snapshot
                );
        }
    }
}
