using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Collects interceptable defense modifiers before an Armor Class is resolved.</summary>
    public sealed class CollectDefenseModifiersOp : IRuleOp<ModifierCollection>
    {
        private readonly IReadOnlyList<Modifier> initialModifiers;

        /// <summary>Creates one nested Armor Class modifier request.</summary>
        public CollectDefenseModifiersOp(
            CreatureId defender,
            IEnumerable<Modifier> initialModifiers,
            CheckSource source
        )
        {
            if (defender.IsEmpty)
                throw new ArgumentException("A defender is required.", nameof(defender));
            if (initialModifiers == null)
                throw new ArgumentNullException(nameof(initialModifiers));
            Modifier[] copied = initialModifiers.ToArray();
            if (copied.Any(modifier => modifier.IsEmpty))
                throw new ArgumentException(
                    "Defense modifiers cannot contain an empty value.",
                    nameof(initialModifiers)
                );
            if (source.IsEmpty)
                throw new ArgumentException(
                    "Trusted source provenance is required.",
                    nameof(source)
                );

            Defender = defender;
            this.initialModifiers = Array.AsReadOnly(copied);
            Source = source;
        }

        /// <summary>Gets the creature whose defense is being collected.</summary>
        public CreatureId Defender { get; }

        /// <summary>Gets the feature-provided candidates present before active middleware.</summary>
        public IReadOnlyList<Modifier> InitialModifiers => initialModifiers;

        /// <summary>Gets the ancestor calculation responsible for this request.</summary>
        public CheckSource Source { get; }
    }

    internal sealed class CollectDefenseModifiersHandler
        : IOpHandler<CollectDefenseModifiersOp, ModifierCollection>
    {
        public ValueTask<ModifierCollection> Handle(
            OpFrame<CollectDefenseModifiersOp> frame,
            OpHandlerContext context
        )
        {
            CheckHandlerSupport.RequireAncestorSource(frame.Id, frame.Op.Source, context.Trace);
            return new ValueTask<ModifierCollection>(
                new ModifierCollection(Statistic.ArmorClass, frame.Op.InitialModifiers)
            );
        }
    }

    internal sealed class OffGuardDefenseMiddleware
        : IOpMiddleware<CollectDefenseModifiersOp, ModifierCollection>
    {
        public async ValueTask<OpResult<ModifierCollection>> Invoke(
            OpFrame<CollectDefenseModifiersOp> frame,
            OpMiddlewareContext context,
            OpNext<ModifierCollection> next
        )
        {
            OpResult<ModifierCollection> result = await next();
            if (
                context.Binding.Owner != frame.Op.Defender
                || !context.Binding.EffectId.HasValue
                || !context.Snapshot.ActiveEffects.TryGet(
                    context.Binding.EffectId.Value,
                    out ActiveEffectInstance effect
                )
                || effect.Status != ActiveEffectStatus.Active
                || effect.DefinitionId != ConditionRuleDefinitions.OffGuard
                || effect.Source != context.Binding.Source
                || effect.State.GetType() != typeof(ConditionMarkerState)
                || result is not ResolvedOpResult<ModifierCollection> resolved
            )
                return result;

            return OpResult<ModifierCollection>.Resolved(
                resolved.Value.Add(
                    new Modifier(
                        -2,
                        ModifierType.Circumstance,
                        context.Binding.Source,
                        Statistic.ArmorClass
                    )
                )
            );
        }
    }
}
