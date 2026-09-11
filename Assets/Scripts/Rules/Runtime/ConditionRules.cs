using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Applies one independent valued condition with an existing active-effect lifetime.</summary>
    /// <remarks>
    /// Applying is distinct from reducing or removing a condition. Applications do not overwrite
    /// one another; consumers query their effective value through <see cref="ConditionRules"/>.
    /// </remarks>
    public sealed class ApplyConditionOp : IRuleOp<ActiveEffectCreationOutcome>, IRuleSourcedOp
    {
        /// <summary>Gets the creature receiving the condition.</summary>
        public CreatureId Target { get; }

        /// <summary>Gets the condition kind and this application's value.</summary>
        public ConditionState State { get; }

        /// <summary>Gets the originating creature, which anchors finite effect timing.</summary>
        public CreatureId SourceCreature { get; }

        /// <inheritdoc/>
        public RuleSource Source { get; }

        /// <summary>Gets the lifetime interpreted by existing encounter timing.</summary>
        public EffectDuration Duration { get; }

        /// <summary>Requests a new application without merging its value or lifetime with others.</summary>
        /// <param name="target">The creature receiving the condition.</param>
        /// <param name="condition">The condition kind.</param>
        /// <param name="value">This application's positive value.</param>
        /// <param name="sourceCreature">The originating creature.</param>
        /// <param name="source">The originating rule, spell, or ability.</param>
        /// <param name="duration">The application's lifetime.</param>
        public ApplyConditionOp(
            CreatureId target,
            ConditionId condition,
            int value,
            CreatureId sourceCreature,
            RuleSource source,
            EffectDuration duration
        )
        {
            if (target.IsEmpty)
                throw new ArgumentException("A condition target is required.", nameof(target));
            if (sourceCreature.IsEmpty)
                throw new ArgumentException(
                    "A source creature is required.",
                    nameof(sourceCreature)
                );
            if (source.IsEmpty)
                throw new ArgumentException("A rule source is required.", nameof(source));
            Target = target;
            State = new ConditionState(condition, value);
            SourceCreature = sourceCreature;
            Source = source;
            Duration = duration;
        }
    }

    /// <summary>Shares application storage and highest-value queries, not condition-specific behavior.</summary>
    public static class ConditionRules
    {
        /// <summary>Identifies application bindings, which own lifetime but no mechanical extensions.</summary>
        public static readonly RuleDefinitionId DefinitionId = new("condition-application");

        /// <summary>Enumerates a creature's enabled condition applications from authoritative state.</summary>
        /// <param name="snapshot">The snapshot to query.</param>
        /// <param name="target">The affected creature, not the effect's originating creature.</param>
        /// <returns>Independent applications with their existing effect identity and lifetime.</returns>
        public static IEnumerable<ActiveEffectInstance> GetApplications(
            RulesSnapshot snapshot,
            CreatureId target
        )
        {
            foreach (var entry in snapshot.RuleBindings)
            {
                ActiveRuleBinding binding = entry.Value;
                if (
                    binding.DefinitionId == DefinitionId
                    && binding.Owner == target
                    && binding.IsEnabled
                )
                    yield return snapshot.ActiveEffects[binding.EffectId.Value];
            }
        }

        /// <summary>Derives the highest active value, or zero when the condition is absent.</summary>
        /// <remarks>
        /// Weaker applications keep their own lifetimes. Expiring the strongest needs no restoration
        /// state or cached aggregate. This query describes application stacking, not value reduction.
        /// See https://2e.aonprd.com/Rules.aspx?ID=774.
        /// </remarks>
        /// <param name="snapshot">The snapshot to query.</param>
        /// <param name="target">The affected creature.</param>
        /// <param name="condition">The condition kind to query.</param>
        /// <returns>The highest applicable value.</returns>
        public static int GetValue(RulesSnapshot snapshot, CreatureId target, ConditionId condition)
        {
            int value = 0;
            foreach (ActiveEffectInstance effect in GetApplications(snapshot, target))
            {
                ConditionState state = effect.GetState<ConditionState>();
                if (state.Condition == condition)
                    value = Math.Max(value, state.Value);
            }
            return value;
        }

        /// <summary>Registers condition application using the existing active-effect reducers.</summary>
        /// <param name="builder">The dispatcher being composed with active-effect rules.</param>
        public static void ConfigureDispatcher(RuleDispatcherBuilder builder) =>
            builder.RegisterHandler<ApplyConditionOp, ActiveEffectCreationOutcome>(
                new ApplyHandler()
            );

        private sealed class ApplyHandler
            : IOpHandler<ApplyConditionOp, ActiveEffectCreationOutcome>
        {
            public async ValueTask<ActiveEffectCreationOutcome> Handle(
                OpFrame<ApplyConditionOp> frame,
                OpHandlerContext context
            )
            {
                ApplyConditionOp op = frame.Op;
                if (
                    !context.Snapshot.Creatures.Contains(op.Target)
                    || !context.Snapshot.Creatures.Contains(op.SourceCreature)
                )
                    throw new InvalidOperationException(
                        "Condition application requires registered target and source creatures."
                    );
                // Frames are unique even for several applications in one root operation.
                ActiveEffectId id = new($"condition-effect:{frame.Id.Value}");
                ActiveEffectInstance effect = new(
                    id,
                    DefinitionId,
                    op.SourceCreature,
                    op.Source,
                    op.Duration,
                    op.State
                );
                ActiveRuleBinding binding = new(
                    new BindingId(id.Value),
                    DefinitionId,
                    op.Target,
                    id,
                    op.Source,
                    frame.Id.Value
                );
                OpResult<ActiveEffectCreationOutcome> result = await context.Dispatch(
                    new CreateActiveEffectOp(effect, binding)
                );
                if (result is ResolvedOpResult<ActiveEffectCreationOutcome> resolved)
                    return resolved.Value;
                throw new InvalidOperationException("Condition application did not resolve.");
            }
        }
    }
}
