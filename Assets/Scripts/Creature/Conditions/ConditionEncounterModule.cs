using System;
using System.Linq;
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
                        source,
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
            seed.ApplyBeforeAttachment(state, source);
            display.Add(state.Condition.Value, displaySource);
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
            if (seed == null)
                return;
            // Freeze before the addition commit: its Facts will update the Unity projection.
            var applications = seed.Applications.ToArray();
            for (int i = 0; i < applications.Length; i++)
            {
                var application = applications[i];
                ActiveEffectId id = new($"condition-seed:{builder.CreatureId.Value}:{i}");
                builder.AddActiveEffects(
                    new[]
                    {
                        new ActiveEffectInstance(
                            id,
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
                            new BindingId(id.Value),
                            ConditionRules.DefinitionId,
                            builder.CreatureId,
                            id,
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
