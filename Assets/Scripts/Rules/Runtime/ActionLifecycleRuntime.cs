using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Rules.Runtime
{
    internal abstract class FrameActionState
    {
        public static FrameActionState NonAction { get; } = new NonActionFrameState();

        public abstract bool IsAction { get; }

        public abstract ActionOpInfo RequireInfo();

        public abstract ActionProfile RequireProfile();

        public static FrameActionState Frozen(ActionOpInfo info, ActionProfile profile) =>
            new FrozenActionFrameState(info, profile);

        private sealed class NonActionFrameState : FrameActionState
        {
            public override bool IsAction => false;

            public override ActionOpInfo RequireInfo() =>
                throw new InvalidOperationException(
                    "This operation frame does not represent an action."
                );

            public override ActionProfile RequireProfile() =>
                throw new InvalidOperationException(
                    "This operation frame does not represent an action."
                );
        }

        private sealed class FrozenActionFrameState : FrameActionState
        {
            private readonly ActionOpInfo info;
            private readonly ActionProfile profile;

            public FrozenActionFrameState(ActionOpInfo info, ActionProfile profile)
            {
                this.info = info ?? throw new ArgumentNullException(nameof(info));
                this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            }

            public override bool IsAction => true;

            public override ActionOpInfo RequireInfo() => info;

            public override ActionProfile RequireProfile() => profile;
        }
    }

    internal interface IActionValidatorRegistration
    {
        Type OpType { get; }
        ActionValidationResult Validate(IFrameInvocation invocation);
    }

    internal sealed class ActionValidatorRegistration<TOp> : IActionValidatorRegistration
        where TOp : IRuleOp
    {
        private readonly IActionValidator<TOp> validator;

        public ActionValidatorRegistration(IActionValidator<TOp> validator) =>
            this.validator = validator ?? throw new ArgumentNullException(nameof(validator));

        public Type OpType => typeof(TOp);

        public ActionValidationResult Validate(IFrameInvocation invocation)
        {
            if (!(invocation is FrameInvocation<TOp> typed))
                throw new InvalidOperationException(
                    "An action validator received an impossible frame type."
                );

            ActionValidationResult result = validator.Validate(
                typed.Frame,
                typed.Frame.StartSnapshot
            );
            return result
                ?? throw new InvalidOperationException(
                    $"Action validator {validator.GetType().Name} returned null."
                );
        }
    }

    internal abstract class ActionRuntime
    {
        public static ActionRuntime Disabled { get; } = new DisabledActionRuntime();

        public abstract FrameActionState CreateFrameState(
            OpId id,
            OpId rootId,
            OpId? parentId,
            OpId? causeId,
            InvocationPolicy invocationPolicy,
            IRuleOp op,
            RulesSnapshot snapshot
        );

        public abstract FrameActionState CreateRetriedFrameState(
            OpId id,
            OpId rootId,
            OpId? parentId,
            OpId? causeId,
            InvocationPolicy invocationPolicy,
            IRuleOp op,
            ActionProfile frozenProfile
        );

        public abstract ActionValidationResult Validate(IFrameInvocation invocation);

        public static ActionRuntime Create(
            IActionCatalog catalog,
            IActionProfileResolver resolver,
            IDictionary<Type, List<IActionValidatorRegistration>> validators
        ) => new ConfiguredActionRuntime(catalog, resolver, validators);

        private sealed class DisabledActionRuntime : ActionRuntime
        {
            public override FrameActionState CreateFrameState(
                OpId id,
                OpId rootId,
                OpId? parentId,
                OpId? causeId,
                InvocationPolicy invocationPolicy,
                IRuleOp op,
                RulesSnapshot snapshot
            )
            {
                if (op is IActionOpMetadata)
                {
                    throw new InvalidOperationException(
                        $"Action lifecycle services are not configured for {op.GetType().Name}."
                    );
                }
                return FrameActionState.NonAction;
            }

            public override ActionValidationResult Validate(IFrameInvocation invocation) =>
                throw new InvalidOperationException(
                    "A disabled action runtime cannot validate an action frame."
                );

            public override FrameActionState CreateRetriedFrameState(
                OpId id,
                OpId rootId,
                OpId? parentId,
                OpId? causeId,
                InvocationPolicy invocationPolicy,
                IRuleOp op,
                ActionProfile frozenProfile
            ) =>
                throw new InvalidOperationException(
                    $"Action lifecycle services are not configured for {op.GetType().Name}."
                );
        }

        private sealed class ConfiguredActionRuntime : ActionRuntime
        {
            private readonly IActionCatalog catalog;
            private readonly IActionProfileResolver resolver;
            private readonly IReadOnlyDictionary<
                Type,
                IReadOnlyList<IActionValidatorRegistration>
            > validators;

            public ConfiguredActionRuntime(
                IActionCatalog catalog,
                IActionProfileResolver resolver,
                IDictionary<Type, List<IActionValidatorRegistration>> validators
            )
            {
                this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
                this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

                Dictionary<Type, IReadOnlyList<IActionValidatorRegistration>> copied =
                    new Dictionary<Type, IReadOnlyList<IActionValidatorRegistration>>();
                foreach (KeyValuePair<Type, List<IActionValidatorRegistration>> pair in validators)
                    copied.Add(pair.Key, Array.AsReadOnly(pair.Value.ToArray()));
                this.validators = new ReadOnlyDictionary<
                    Type,
                    IReadOnlyList<IActionValidatorRegistration>
                >(copied);
            }

            public override FrameActionState CreateFrameState(
                OpId id,
                OpId rootId,
                OpId? parentId,
                OpId? causeId,
                InvocationPolicy invocationPolicy,
                IRuleOp op,
                RulesSnapshot snapshot
            )
            {
                if (!(op is IActionOpMetadata action))
                    return FrameActionState.NonAction;

                ActionOpInfo info = CreateInfo(
                    id,
                    rootId,
                    parentId,
                    causeId,
                    invocationPolicy,
                    op,
                    action
                );
                ActionProfile baseProfile =
                    action.GetBaseProfile(catalog, snapshot)
                    ?? throw new InvalidOperationException(
                        $"Action {op.GetType().Name} returned a null base profile."
                    );
                ActionProfile effective =
                    resolver.Resolve(info, baseProfile, snapshot)
                    ?? throw new InvalidOperationException(
                        $"Action profile resolver {resolver.GetType().Name} returned null."
                    );
                return FrameActionState.Frozen(info, effective);
            }

            public override FrameActionState CreateRetriedFrameState(
                OpId id,
                OpId rootId,
                OpId? parentId,
                OpId? causeId,
                InvocationPolicy invocationPolicy,
                IRuleOp op,
                ActionProfile frozenProfile
            )
            {
                if (!(op is IActionOpMetadata action))
                    throw new InvalidOperationException(
                        $"Receipted operation {op.GetType().Name} is not an action."
                    );
                return FrameActionState.Frozen(
                    CreateInfo(id, rootId, parentId, causeId, invocationPolicy, op, action),
                    frozenProfile ?? throw new ArgumentNullException(nameof(frozenProfile))
                );
            }

            private static ActionOpInfo CreateInfo(
                OpId id,
                OpId rootId,
                OpId? parentId,
                OpId? causeId,
                InvocationPolicy invocationPolicy,
                IRuleOp op,
                IActionOpMetadata action
            ) =>
                new ActionOpInfo(
                    id,
                    rootId,
                    parentId,
                    causeId,
                    invocationPolicy,
                    action.Actor,
                    action.DefinitionId,
                    op.GetType()
                );

            public override ActionValidationResult Validate(IFrameInvocation invocation)
            {
                if (
                    !validators.TryGetValue(
                        invocation.FrameView.OpType,
                        out IReadOnlyList<IActionValidatorRegistration> selected
                    )
                )
                {
                    return ActionValidationResult.Valid;
                }

                foreach (IActionValidatorRegistration registration in selected)
                {
                    ActionValidationResult result = registration.Validate(invocation);
                    if (result is ActionValidationResult.InvalidActionValidationResult)
                        return result;
                }
                return ActionValidationResult.Valid;
            }
        }
    }

    /// <summary>
    /// Represents whether a dispatcher builder has complete action lifecycle dependencies.
    /// </summary>
    /// <remarks>
    /// The structural cases keep an unconfigured builder from carrying placeholder catalog or
    /// resolver implementations. Building without action registrations produces a disabled runtime;
    /// configuring the lifecycle produces a runtime that owns the required dependencies.
    /// </remarks>
    internal abstract class ActionRuntimeConfiguration
    {
        public static ActionRuntimeConfiguration Unconfigured { get; } =
            new UnconfiguredActionRuntimeConfiguration();

        public abstract bool IsConfigured { get; }

        public abstract ActionRuntime CreateRuntime(
            IDictionary<Type, List<IActionValidatorRegistration>> validators
        );

        public static ActionRuntimeConfiguration Configure(
            IActionCatalog catalog,
            IActionProfileResolver resolver
        ) => new ConfiguredActionRuntimeConfiguration(catalog, resolver);

        private sealed class UnconfiguredActionRuntimeConfiguration : ActionRuntimeConfiguration
        {
            public override bool IsConfigured => false;

            public override ActionRuntime CreateRuntime(
                IDictionary<Type, List<IActionValidatorRegistration>> validators
            ) => ActionRuntime.Disabled;
        }

        private sealed class ConfiguredActionRuntimeConfiguration : ActionRuntimeConfiguration
        {
            private readonly IActionCatalog catalog;
            private readonly IActionProfileResolver resolver;

            public ConfiguredActionRuntimeConfiguration(
                IActionCatalog catalog,
                IActionProfileResolver resolver
            )
            {
                this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
                this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            }

            public override bool IsConfigured => true;

            public override ActionRuntime CreateRuntime(
                IDictionary<Type, List<IActionValidatorRegistration>> validators
            ) => ActionRuntime.Create(catalog, resolver, validators);
        }
    }

    internal sealed class IdentityActionProfileResolver : IActionProfileResolver
    {
        public static IdentityActionProfileResolver Instance { get; } =
            new IdentityActionProfileResolver();

        private IdentityActionProfileResolver() { }

        public ActionProfile Resolve(
            ActionOpInfo action,
            ActionProfile baseProfile,
            RulesSnapshot snapshot
        ) => baseProfile;
    }
}
