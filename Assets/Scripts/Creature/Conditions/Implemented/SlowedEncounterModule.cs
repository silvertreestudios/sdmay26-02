using System;
using System.Threading.Tasks;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;
using UnityEngine;

namespace Game.Creature.Rules
{
    /// <summary>Stores the value owned by one authoritative Slowed active effect.</summary>
    /// <remarks>
    /// Multiple applications retain the highest value, as required for redundant valued
    /// conditions: https://2e.aonprd.com/Rules.aspx?ID=774.
    /// </remarks>
    internal sealed class SlowedEffectState : IEffectState, IEquatable<SlowedEffectState>
    {
        internal SlowedEffectState(int value)
        {
            if (value is < 1 or > 3)
                throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }

        internal int Value { get; }

        public bool Equals(SlowedEffectState other) => other != null && Value == other.Value;

        public override bool Equals(object obj) => obj is SlowedEffectState other && Equals(other);

        public override int GetHashCode() => Value;
    }

    /// <summary>Requests application of one supported Slowed value to a rules combatant.</summary>
    internal sealed class ApplySlowedOp : IRuleOp<int>
    {
        internal ApplySlowedOp(CreatureId actor, int value)
        {
            if (actor.IsEmpty)
                throw new ArgumentException("A Slowed actor is required.", nameof(actor));
            if (value is < 1 or > 3)
                throw new ArgumentOutOfRangeException(nameof(value));
            Actor = actor;
            Value = value;
        }

        internal CreatureId Actor { get; }
        internal int Value { get; }
    }

    /// <summary>Owns Slowed's active effect, resource contribution, and Unity projection.</summary>
    internal sealed class SlowedEncounterModule
        : IUnityEncounterDispatcherModule,
            IUnityEncounterRuntimeModule,
            IUnityCombatantEnrollmentModule
    {
        internal static readonly RuleDefinitionId DefinitionId = new("slowed-turn-resources");
        private static readonly RuleSource Source = RuleSource.FromSlug("slowed");
        private readonly UnityCombatRulesBridge owner;

        internal SlowedEncounterModule(UnityCombatRulesBridge owner) =>
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

        internal static void DefineRuleBindings(RuleRegistryBuilder builder) =>
            builder
                .Define(DefinitionId)
                .Middleware<CalculateTurnResourcesOp, TurnResourceContribution>(
                    RuleLifecyclePhase.Transformation,
                    new TurnResourceMiddleware()
                );

        internal static void Apply(GameObject target, int value)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (value is < 1 or > 3)
                throw new ArgumentOutOfRangeException(nameof(value));

            ActionController controller = target.GetComponent<ActionController>();
            if (
                controller != null
                && controller.TryGetCombatRules(
                    out UnityCombatRulesBridge bridge,
                    out CreatureId actor
                )
            )
            {
                RequireResolved(bridge.Dispatch(new ApplySlowedOp(actor, value)));
                return;
            }

            SlowedSeed seed =
                target.GetComponent<SlowedSeed>() ?? target.AddComponent<SlowedSeed>();
            seed.ApplyBeforeAttachment(value);
        }

        /// <inheritdoc/>
        public void ConfigureDispatcher(RuleDispatcherBuilder builder) =>
            builder.RegisterHandler<ApplySlowedOp, int>(new ApplySlowedHandler());

        /// <inheritdoc/>
        public void RegisterRuntime(RuleDispatcher dispatcher, CompositeLifetime lifetime)
        {
            SlowedEffectProjection projection = new(owner);
            lifetime.Add(dispatcher.RegisterFactObserver<ActiveEffectCreatedFact>(projection));
            lifetime.Add(dispatcher.RegisterFactObserver<ActiveEffectStateUpdatedFact>(projection));
            lifetime.Add(dispatcher.RegisterFactObserver<ActiveEffectRemovedFact>(projection));
        }

        /// <inheritdoc/>
        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder)
        {
            SlowedSeed seed = builder.Controller.GetComponent<SlowedSeed>();
            if (seed == null || seed.Value == 0)
                return;

            ActiveEffectId effectId = EffectId(builder.CreatureId);
            builder.AddActiveEffects(
                new[]
                {
                    new ActiveEffectInstance(
                        effectId,
                        DefinitionId,
                        builder.CreatureId,
                        Source,
                        EffectDuration.Indefinite,
                        new SlowedEffectState(seed.Value)
                    ),
                }
            );
            builder.AddRuleBindings(
                new[]
                {
                    new ActiveRuleBinding(
                        BindingId(builder.CreatureId),
                        DefinitionId,
                        builder.CreatureId,
                        effectId,
                        Source,
                        0
                    ),
                }
            );
        }

        internal static ActiveEffectId EffectId(CreatureId actor) =>
            new($"slowed-effect:{actor.Value}");

        internal static BindingId BindingId(CreatureId actor) =>
            new($"slowed-binding:{actor.Value}");

        private static TResult RequireResolved<TResult>(OpResult<TResult> result)
        {
            if (result is ResolvedOpResult<TResult> resolved)
                return resolved.Value;
            if (result is InvalidOpResult<TResult> invalid)
                throw new InvalidOperationException(invalid.Reason);
            throw new InvalidOperationException("Slowed rules work did not resolve.");
        }

        private sealed class ApplySlowedHandler : IOpHandler<ApplySlowedOp, int>
        {
            public async ValueTask<int> Handle(
                OpFrame<ApplySlowedOp> frame,
                OpHandlerContext context
            )
            {
                ActiveEffectId effectId = EffectId(frame.Op.Actor);
                if (
                    !context.Snapshot.ActiveEffects.TryGet(
                        effectId,
                        out ActiveEffectInstance current
                    )
                )
                {
                    RequireResolved(
                        await context.Dispatch(
                            new CreateActiveEffectOp(
                                new ActiveEffectInstance(
                                    effectId,
                                    DefinitionId,
                                    frame.Op.Actor,
                                    Source,
                                    EffectDuration.Indefinite,
                                    new SlowedEffectState(frame.Op.Value)
                                ),
                                new ActiveRuleBinding(
                                    BindingId(frame.Op.Actor),
                                    DefinitionId,
                                    frame.Op.Actor,
                                    effectId,
                                    Source,
                                    frame.RootId.Value
                                )
                            )
                        )
                    );
                    return frame.Op.Value;
                }

                SlowedEffectState state = current.GetState<SlowedEffectState>();
                int effective = Math.Max(state.Value, frame.Op.Value);
                if (effective == state.Value)
                    return effective;

                RequireResolved(
                    await context.Dispatch(
                        UpdateActiveEffectStateOp.Create(
                            current.Id,
                            current.EffectStateVersion,
                            new SlowedEffectState(effective),
                            Source
                        )
                    )
                );
                return effective;
            }
        }

        private sealed class TurnResourceMiddleware
            : IOpMiddleware<CalculateTurnResourcesOp, TurnResourceContribution>
        {
            public async ValueTask<OpResult<TurnResourceContribution>> Invoke(
                OpFrame<CalculateTurnResourcesOp> frame,
                OpMiddlewareContext context,
                OpNext<TurnResourceContribution> next
            )
            {
                OpResult<TurnResourceContribution> result = await next();
                if (
                    context.Binding.Owner != frame.Op.Turn.Actor
                    || result is not ResolvedOpResult<TurnResourceContribution> resolved
                )
                    return result;
                if (
                    !context.Binding.EffectId.HasValue
                    || !context.Snapshot.ActiveEffects.TryGet(
                        context.Binding.EffectId.Value,
                        out ActiveEffectInstance effect
                    )
                )
                    throw new InvalidOperationException(
                        "An active Slowed binding requires its authoritative effect."
                    );

                int value = effect.GetState<SlowedEffectState>().Value;
                return OpResult<TurnResourceContribution>.Resolved(
                    new TurnResourceContribution(Math.Max(0, resolved.Value.Actions - value))
                );
            }
        }

        internal sealed class SlowedEffectProjection
            : IFactObserver<ActiveEffectCreatedFact>,
                IFactObserver<ActiveEffectStateUpdatedFact>,
                IFactObserver<ActiveEffectRemovedFact>
        {
            private readonly UnityCombatRulesBridge owner;

            internal SlowedEffectProjection(UnityCombatRulesBridge owner) =>
                this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

            public void OnFactCommitted(
                ActiveEffectCreatedFact fact,
                OpId rootId,
                RulesSnapshot currentSnapshot
            ) => ProjectCurrent(fact.DefinitionId, fact.EffectId, currentSnapshot);

            public void OnFactCommitted(
                ActiveEffectStateUpdatedFact fact,
                OpId rootId,
                RulesSnapshot currentSnapshot
            ) => ProjectCurrent(fact.DefinitionId, fact.EffectId, currentSnapshot);

            public void OnFactCommitted(
                ActiveEffectRemovedFact fact,
                OpId rootId,
                RulesSnapshot currentSnapshot
            )
            {
                if (fact.DefinitionId != DefinitionId)
                    return;
                ActionController controller = owner.GetController(fact.Binding.Owner);
                controller.GetComponent<Conditions>()?.Clear("Slowed");
                SlowedSeed seed = controller.GetComponent<SlowedSeed>();
                seed?.Project(0);
            }

            private void ProjectCurrent(
                RuleDefinitionId definitionId,
                ActiveEffectId effectId,
                RulesSnapshot snapshot
            )
            {
                if (
                    definitionId != DefinitionId
                    || !snapshot.ActiveEffects.TryGet(effectId, out ActiveEffectInstance effect)
                )
                    return;
                ActionController controller = owner.GetController(effect.SourceCreature);
                SlowedSeed seed =
                    controller.GetComponent<SlowedSeed>()
                    ?? controller.gameObject.AddComponent<SlowedSeed>();
                seed.Project(effect.GetState<SlowedEffectState>().Value);
            }
        }
    }
}
