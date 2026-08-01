using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Typed immutable operation context used by compiled item predicates.</summary>
    public sealed class PreparedContributionContext
    {
        private readonly IReadOnlyList<string> itemTraits;
        private readonly IReadOnlyList<string> itemTags;
        private readonly IReadOnlyList<string> targetConditions;

        /// <summary>Creates current typed item and target facts for one collection request.</summary>
        public PreparedContributionContext(
            string itemSlug,
            string itemCategory,
            bool isRanged,
            int damageDieFaces,
            IEnumerable<string> itemTraits,
            IEnumerable<string> itemTags,
            IEnumerable<string> targetConditions
        )
        {
            ItemSlug = Pf2eSlug.FromName(itemSlug ?? string.Empty);
            ItemCategory = Pf2eSlug.FromName(itemCategory ?? string.Empty);
            IsRanged = isRanged;
            if (damageDieFaces < 0)
                throw new ArgumentOutOfRangeException(nameof(damageDieFaces));
            DamageDieFaces = damageDieFaces;
            this.itemTraits = Freeze(itemTraits, nameof(itemTraits));
            this.itemTags = Freeze(itemTags, nameof(itemTags));
            this.targetConditions = Freeze(targetConditions, nameof(targetConditions));
        }

        /// <summary>Gets the normalized item definition slug.</summary>
        public string ItemSlug { get; }

        /// <summary>Gets the normalized item category.</summary>
        public string ItemCategory { get; }

        /// <summary>Gets whether the current item is used at range.</summary>
        public bool IsRanged { get; }

        /// <summary>Gets the current item's base damage-die size.</summary>
        public int DamageDieFaces { get; }

        /// <summary>Gets a defensive copy of normalized item traits.</summary>
        public IReadOnlyList<string> ItemTraits => itemTraits;

        /// <summary>Gets a defensive copy of normalized derived item tags.</summary>
        public IReadOnlyList<string> ItemTags => itemTags;

        /// <summary>Gets a defensive copy of normalized target conditions.</summary>
        public IReadOnlyList<string> TargetConditions => targetConditions;

        internal IEnumerable<string> Options()
        {
            if (!string.IsNullOrWhiteSpace(ItemSlug))
                yield return $"item:slug:{ItemSlug}";
            if (!string.IsNullOrWhiteSpace(ItemCategory))
                yield return $"item:category:{ItemCategory}";
            if (IsRanged)
                yield return "item:ranged";
            if (DamageDieFaces > 0)
                yield return $"item:damage:die:faces:{DamageDieFaces}";
            foreach (string trait in itemTraits)
            {
                yield return $"item:trait:{trait}";
                if (trait.StartsWith("thrown", StringComparison.Ordinal))
                    yield return "item:thrown";
            }
            foreach (string tag in itemTags)
                yield return $"item:tag:{tag}";
            foreach (string condition in targetConditions)
                yield return $"target:condition:{condition}";
        }

        private static IReadOnlyList<string> Freeze(IEnumerable<string> values, string parameter) =>
            Array.AsReadOnly(
                (values ?? throw new ArgumentNullException(parameter))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(Pf2eSlug.FromName)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray()
            );
    }

    /// <summary>Immutable numeric contribution after ordered adjustments.</summary>
    public sealed class PreparedModifierValue
    {
        /// <summary>Creates one resolved immutable modifier contribution.</summary>
        public PreparedModifierValue(string slug, int value, string type, string ability)
        {
            Slug = slug ?? string.Empty;
            Value = value;
            Type = type ?? string.Empty;
            Ability = ability ?? string.Empty;
        }

        /// <summary>Gets the stable modifier slug.</summary>
        public string Slug { get; }

        /// <summary>Gets the resolved numeric value.</summary>
        public int Value { get; }

        /// <summary>Gets the normalized stacking type.</summary>
        public string Type { get; }

        /// <summary>Gets the optional ability supplying this modifier.</summary>
        public string Ability { get; }
    }

    /// <summary>Collects definition-owned modifiers for one selector.</summary>
    public sealed class CollectPreparedModifiersOp : IRuleOp<IReadOnlyList<PreparedModifierValue>>
    {
        /// <summary>Creates a modifier collection request for an actor and selector.</summary>
        public CollectPreparedModifiersOp(
            CreatureId owner,
            string selector,
            PreparedContributionContext contributionContext
        )
        {
            if (owner.IsEmpty)
                throw new ArgumentException("An owner is required.", nameof(owner));
            Owner = owner;
            Selector = selector ?? string.Empty;
            Context =
                contributionContext ?? throw new ArgumentNullException(nameof(contributionContext));
        }

        /// <summary>Gets the creature whose active bindings may contribute.</summary>
        public CreatureId Owner { get; }

        /// <summary>Gets the normalized selector requested by the consumer.</summary>
        public string Selector { get; }

        /// <summary>Gets the immutable current-operation facts.</summary>
        public PreparedContributionContext Context { get; }
    }

    /// <summary>Collects definition-owned extra damage dice for one selector.</summary>
    public sealed class CollectPreparedDamageDiceOp : IRuleOp<IReadOnlyList<PreparedDamageDiceSpec>>
    {
        /// <summary>Creates a damage-dice collection request for an actor and selector.</summary>
        public CollectPreparedDamageDiceOp(
            CreatureId owner,
            string selector,
            PreparedContributionContext contributionContext
        )
        {
            if (owner.IsEmpty)
                throw new ArgumentException("An owner is required.", nameof(owner));
            Owner = owner;
            Selector = selector ?? string.Empty;
            Context =
                contributionContext ?? throw new ArgumentNullException(nameof(contributionContext));
        }

        /// <summary>Gets the creature whose active bindings may contribute.</summary>
        public CreatureId Owner { get; }

        /// <summary>Gets the normalized selector requested by the consumer.</summary>
        public string Selector { get; }

        /// <summary>Gets the immutable current-operation facts.</summary>
        public PreparedContributionContext Context { get; }
    }

    /// <summary>Collects definition-owned item alterations for one typed item property.</summary>
    public sealed class CollectPreparedItemAlterationsOp
        : IRuleOp<IReadOnlyList<PreparedItemAlterationSpec>>
    {
        /// <summary>Creates an item-alteration request for one typed item property.</summary>
        public CollectPreparedItemAlterationsOp(
            CreatureId owner,
            string itemType,
            string property,
            PreparedContributionContext contributionContext
        )
        {
            if (owner.IsEmpty)
                throw new ArgumentException("An owner is required.", nameof(owner));
            Owner = owner;
            ItemType = itemType ?? string.Empty;
            Property = property ?? string.Empty;
            Context =
                contributionContext ?? throw new ArgumentNullException(nameof(contributionContext));
        }

        /// <summary>Gets the creature whose active bindings may contribute.</summary>
        public CreatureId Owner { get; }

        /// <summary>Gets the requested item type.</summary>
        public string ItemType { get; }

        /// <summary>Gets the requested immutable item property.</summary>
        public string Property { get; }

        /// <summary>Gets the immutable current-operation facts.</summary>
        public PreparedContributionContext Context { get; }
    }

    /// <summary>Registers the typed prepared-contribution collection operations.</summary>
    public static class PreparedContributionRuntime
    {
        /// <summary>Registers generic prepared modifier, damage-dice, and alteration resolvers.</summary>
        /// <param name="builder">The encounter dispatcher composition root.</param>
        /// <returns>The same builder for fluent composition.</returns>
        public static RuleDispatcherBuilder UsePreparedContributions(
            this RuleDispatcherBuilder builder
        ) =>
            builder
                .RegisterHandler<CollectPreparedModifiersOp, IReadOnlyList<PreparedModifierValue>>(
                    new EmptyPreparedModifierHandler()
                )
                .RegisterHandler<
                    CollectPreparedDamageDiceOp,
                    IReadOnlyList<PreparedDamageDiceSpec>
                >(new EmptyPreparedDamageDiceHandler())
                .RegisterHandler<
                    CollectPreparedItemAlterationsOp,
                    IReadOnlyList<PreparedItemAlterationSpec>
                >(new EmptyPreparedItemAlterationHandler());

        internal static void Configure(
            RuleDefinitionBuilder builder,
            PreparedRuleDefinitionSpec specification
        )
        {
            if (specification.Modifiers.Count > 0)
                builder.Middleware<
                    CollectPreparedModifiersOp,
                    IReadOnlyList<PreparedModifierValue>
                >(
                    RuleLifecyclePhase.Transformation,
                    new PreparedModifierMiddleware(specification.Modifiers)
                );
            if (specification.Adjustments.Count > 0)
                builder.Middleware<
                    CollectPreparedModifiersOp,
                    IReadOnlyList<PreparedModifierValue>
                >(
                    RuleLifecyclePhase.Adjustment,
                    new PreparedAdjustmentMiddleware(specification.Adjustments)
                );
            if (specification.DamageDice.Count > 0)
                builder.Middleware<
                    CollectPreparedDamageDiceOp,
                    IReadOnlyList<PreparedDamageDiceSpec>
                >(
                    RuleLifecyclePhase.Transformation,
                    new PreparedDamageDiceMiddleware(specification.DamageDice)
                );
            if (specification.ItemAlterations.Count > 0)
                builder.Middleware<
                    CollectPreparedItemAlterationsOp,
                    IReadOnlyList<PreparedItemAlterationSpec>
                >(
                    RuleLifecyclePhase.Transformation,
                    new PreparedItemAlterationMiddleware(specification.ItemAlterations)
                );
        }

        private static PreparedPredicateContext PredicateContext(
            CreatureId owner,
            PreparedContributionContext operation,
            RulesSnapshot snapshot
        )
        {
            return new PreparedPredicateContext(snapshot, owner, operation.Options());
        }

        private sealed class EmptyPreparedModifierHandler
            : IOpHandler<CollectPreparedModifiersOp, IReadOnlyList<PreparedModifierValue>>
        {
            public ValueTask<IReadOnlyList<PreparedModifierValue>> Handle(
                OpFrame<CollectPreparedModifiersOp> frame,
                OpHandlerContext context
            ) => new((IReadOnlyList<PreparedModifierValue>)Array.Empty<PreparedModifierValue>());
        }

        private sealed class EmptyPreparedDamageDiceHandler
            : IOpHandler<CollectPreparedDamageDiceOp, IReadOnlyList<PreparedDamageDiceSpec>>
        {
            public ValueTask<IReadOnlyList<PreparedDamageDiceSpec>> Handle(
                OpFrame<CollectPreparedDamageDiceOp> frame,
                OpHandlerContext context
            ) => new((IReadOnlyList<PreparedDamageDiceSpec>)Array.Empty<PreparedDamageDiceSpec>());
        }

        private sealed class EmptyPreparedItemAlterationHandler
            : IOpHandler<
                CollectPreparedItemAlterationsOp,
                IReadOnlyList<PreparedItemAlterationSpec>
            >
        {
            public ValueTask<IReadOnlyList<PreparedItemAlterationSpec>> Handle(
                OpFrame<CollectPreparedItemAlterationsOp> frame,
                OpHandlerContext context
            ) =>
                new(
                    (IReadOnlyList<PreparedItemAlterationSpec>)
                        Array.Empty<PreparedItemAlterationSpec>()
                );
        }

        private sealed class PreparedModifierMiddleware
            : IOpMiddleware<CollectPreparedModifiersOp, IReadOnlyList<PreparedModifierValue>>
        {
            private readonly IReadOnlyList<PreparedModifierSpec> modifiers;

            internal PreparedModifierMiddleware(IReadOnlyList<PreparedModifierSpec> modifiers) =>
                this.modifiers = modifiers;

            public async ValueTask<OpResult<IReadOnlyList<PreparedModifierValue>>> Invoke(
                OpFrame<CollectPreparedModifiersOp> frame,
                OpMiddlewareContext context,
                OpNext<IReadOnlyList<PreparedModifierValue>> next
            )
            {
                OpResult<IReadOnlyList<PreparedModifierValue>> result = await next();
                if (
                    result is not ResolvedOpResult<IReadOnlyList<PreparedModifierValue>> resolved
                    || context.Binding.Owner != frame.Op.Owner
                )
                    return result;
                PreparedPredicateContext predicate = PredicateContext(
                    frame.Op.Owner,
                    frame.Op.Context,
                    context.Snapshot
                );
                List<PreparedModifierValue> values = resolved.Value.ToList();
                foreach (
                    PreparedModifierSpec value in modifiers.Where(value =>
                        string.Equals(
                            value.Selector,
                            frame.Op.Selector,
                            StringComparison.OrdinalIgnoreCase
                        ) && value.Predicate.Evaluate(predicate)
                    )
                )
                {
                    int existing = values.FindLastIndex(candidate =>
                        string.Equals(
                            candidate.Slug,
                            value.Slug,
                            StringComparison.OrdinalIgnoreCase
                        )
                    );
                    PreparedModifierValue added = new(
                        value.Slug,
                        value.Value,
                        value.Type,
                        value.Ability
                    );
                    if (existing >= 0)
                        values[existing] = added;
                    else
                        values.Add(added);
                }
                return OpResult<IReadOnlyList<PreparedModifierValue>>.Resolved(
                    Array.AsReadOnly(values.ToArray())
                );
            }
        }

        private sealed class PreparedAdjustmentMiddleware
            : IOpMiddleware<CollectPreparedModifiersOp, IReadOnlyList<PreparedModifierValue>>
        {
            private readonly IReadOnlyList<PreparedAdjustmentSpec> adjustments;

            internal PreparedAdjustmentMiddleware(
                IReadOnlyList<PreparedAdjustmentSpec> adjustments
            ) => this.adjustments = adjustments;

            public async ValueTask<OpResult<IReadOnlyList<PreparedModifierValue>>> Invoke(
                OpFrame<CollectPreparedModifiersOp> frame,
                OpMiddlewareContext context,
                OpNext<IReadOnlyList<PreparedModifierValue>> next
            )
            {
                OpResult<IReadOnlyList<PreparedModifierValue>> result = await next();
                if (
                    result is not ResolvedOpResult<IReadOnlyList<PreparedModifierValue>> resolved
                    || context.Binding.Owner != frame.Op.Owner
                )
                    return result;
                PreparedPredicateContext predicate = PredicateContext(
                    frame.Op.Owner,
                    frame.Op.Context,
                    context.Snapshot
                );
                List<PreparedModifierValue> values = resolved.Value.ToList();
                foreach (
                    PreparedAdjustmentSpec adjustment in adjustments.Where(value =>
                        string.Equals(
                            value.Selector,
                            frame.Op.Selector,
                            StringComparison.OrdinalIgnoreCase
                        ) && value.Predicate.Evaluate(predicate)
                    )
                )
                {
                    int index = values.FindLastIndex(value =>
                        string.Equals(
                            value.Slug,
                            adjustment.Slug,
                            StringComparison.OrdinalIgnoreCase
                        )
                    );
                    if (index < 0)
                        continue;
                    PreparedModifierValue current = values[index];
                    int amount = current.Value;
                    if (
                        string.Equals(
                            adjustment.Mode,
                            "upgrade",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        amount = Math.Max(
                            amount,
                            (int)Math.Round(adjustment.Value, MidpointRounding.AwayFromZero)
                        );
                    else if (
                        string.Equals(
                            adjustment.Mode,
                            "multiply",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        amount = (int)Math.Floor(amount * adjustment.Value);
                    values[index] = new PreparedModifierValue(
                        current.Slug,
                        amount,
                        current.Type,
                        current.Ability
                    );
                }
                return OpResult<IReadOnlyList<PreparedModifierValue>>.Resolved(
                    Array.AsReadOnly(values.ToArray())
                );
            }
        }

        private sealed class PreparedDamageDiceMiddleware
            : IOpMiddleware<CollectPreparedDamageDiceOp, IReadOnlyList<PreparedDamageDiceSpec>>
        {
            private readonly IReadOnlyList<PreparedDamageDiceSpec> values;

            internal PreparedDamageDiceMiddleware(IReadOnlyList<PreparedDamageDiceSpec> values) =>
                this.values = values;

            public async ValueTask<OpResult<IReadOnlyList<PreparedDamageDiceSpec>>> Invoke(
                OpFrame<CollectPreparedDamageDiceOp> frame,
                OpMiddlewareContext context,
                OpNext<IReadOnlyList<PreparedDamageDiceSpec>> next
            )
            {
                OpResult<IReadOnlyList<PreparedDamageDiceSpec>> result = await next();
                if (
                    result is not ResolvedOpResult<IReadOnlyList<PreparedDamageDiceSpec>> resolved
                    || context.Binding.Owner != frame.Op.Owner
                )
                    return result;
                PreparedPredicateContext predicate = PredicateContext(
                    frame.Op.Owner,
                    frame.Op.Context,
                    context.Snapshot
                );
                PreparedCreatureInputs inputs = context.Snapshot.PreparedInputs[frame.Op.Owner];
                PreparedDamageDiceSpec[] additions = values
                    .Where(value =>
                        string.Equals(
                            value.Selector,
                            frame.Op.Selector,
                            StringComparison.OrdinalIgnoreCase
                        ) && value.Predicate.Evaluate(predicate)
                    )
                    .Select(value => value.Resolve(inputs))
                    .Where(value => value.DiceNumber > 0 && value.DieSize > 0)
                    .ToArray();
                return OpResult<IReadOnlyList<PreparedDamageDiceSpec>>.Resolved(
                    Array.AsReadOnly(resolved.Value.Concat(additions).ToArray())
                );
            }
        }

        private sealed class PreparedItemAlterationMiddleware
            : IOpMiddleware<
                CollectPreparedItemAlterationsOp,
                IReadOnlyList<PreparedItemAlterationSpec>
            >
        {
            private readonly IReadOnlyList<PreparedItemAlterationSpec> values;

            internal PreparedItemAlterationMiddleware(
                IReadOnlyList<PreparedItemAlterationSpec> values
            ) => this.values = values;

            public async ValueTask<OpResult<IReadOnlyList<PreparedItemAlterationSpec>>> Invoke(
                OpFrame<CollectPreparedItemAlterationsOp> frame,
                OpMiddlewareContext context,
                OpNext<IReadOnlyList<PreparedItemAlterationSpec>> next
            )
            {
                OpResult<IReadOnlyList<PreparedItemAlterationSpec>> result = await next();
                if (
                    result
                        is not ResolvedOpResult<IReadOnlyList<PreparedItemAlterationSpec>> resolved
                    || context.Binding.Owner != frame.Op.Owner
                )
                    return result;
                PreparedPredicateContext predicate = PredicateContext(
                    frame.Op.Owner,
                    frame.Op.Context,
                    context.Snapshot
                );
                PreparedItemAlterationSpec[] additions = values
                    .Where(value =>
                        string.Equals(
                            value.ItemType,
                            frame.Op.ItemType,
                            StringComparison.OrdinalIgnoreCase
                        )
                        && string.Equals(
                            value.Property,
                            frame.Op.Property,
                            StringComparison.OrdinalIgnoreCase
                        )
                        && value.Predicate.Evaluate(predicate)
                    )
                    .ToArray();
                return OpResult<IReadOnlyList<PreparedItemAlterationSpec>>.Resolved(
                    Array.AsReadOnly(resolved.Value.Concat(additions).ToArray())
                );
            }
        }
    }
}
