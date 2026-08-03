using System;
using System.Threading.Tasks;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using NUnit.Framework;

/// <summary>Verifies condition-owned permissions reject otherwise-payable typed actions.</summary>
public sealed class ConditionActionPermissionTests
{
    private static readonly CreatureId Actor = new("condition-permission-actor");
    private static readonly PlayerId Player = new("condition-permission-player");
    private static readonly ActionDefinitionId Definition = new("condition-permission-action");
    private static readonly RuleSource Source = RuleSource.FromSlug("condition-permission-test");

    [Test]
    public async Task StunnedPermissionRejectsOtherwisePayableFreeAction()
    {
        ActiveEffectId effectId = new("condition-permission-stunned-effect");
        BindingId bindingId = new("condition-permission-stunned-binding");
        ActiveEffectInstance effect = new(
            effectId,
            ConditionRuleDefinitions.Stunned,
            Actor,
            Source,
            EffectDuration.Indefinite,
            new ValuedStunnedConditionState(1)
        );
        ActiveRuleBinding binding = new(bindingId, effect.DefinitionId, Actor, effectId, Source, 0);
        ActionProfile profile = ActionProfile.Create(ActionCost.FreeAction, Array.Empty<Trait>());
        RecordingHandler handler = new();
        RuleDispatcher dispatcher = CreateDispatcher(
            profile,
            ConditionTurnResourceComposition.CreateActionPermission(),
            handler,
            new RulesStateSeed()
                .SeedCreature(new CreatureState(Actor, Player))
                .SeedPreparedInputs(Actor, PreparedCreatureInputs.Empty)
                .SeedActionEconomy(Actor, new ActionEconomyState(0, ActionAllowance.None, true))
                .SeedActiveEffect(effect)
                .SeedRuleBinding(binding)
        );
        Assert.That(
            ActionResourcePayment.CanPay(
                dispatcher.Snapshot.ActionEconomy[Actor],
                Definition,
                profile
            ),
            Is.True
        );

        OpResult<bool> result = await dispatcher.Dispatch(new PermissionTestActionOp());

        Assert.That(result, Is.TypeOf<InvalidOpResult<bool>>());
        Assert.That(
            ((InvalidOpResult<bool>)result).Reason,
            Is.EqualTo("A Stunned actor cannot take actions.")
        );
        Assert.That(handler.WasCalled, Is.False);
    }

    [Test]
    public async Task NoReactionsPermissionRejectsOtherwisePayableReaction()
    {
        ActiveRuleBinding binding = new(
            new BindingId("condition-permission-no-reactions-binding"),
            SlowedEncounterModule.NoReactionsDefinitionId,
            Actor,
            default,
            Source,
            0
        );
        ActionProfile profile = ActionProfile.Create(ActionCost.Reaction, Array.Empty<Trait>());
        RecordingHandler handler = new();
        RuleDispatcher dispatcher = CreateDispatcher(
            profile,
            SlowedEncounterModule.CreateNoReactionsActionPermission(),
            handler,
            new RulesStateSeed()
                .SeedCreature(new CreatureState(Actor, Player))
                .SeedPreparedInputs(Actor, PreparedCreatureInputs.Empty)
                .SeedActionEconomy(Actor, new ActionEconomyState(0, ActionAllowance.None, true))
                .SeedRuleBinding(binding)
        );
        Assert.That(
            ActionResourcePayment.CanPay(
                dispatcher.Snapshot.ActionEconomy[Actor],
                Definition,
                profile
            ),
            Is.True
        );

        OpResult<bool> result = await dispatcher.Dispatch(new PermissionTestActionOp());

        Assert.That(result, Is.TypeOf<InvalidOpResult<bool>>());
        Assert.That(
            ((InvalidOpResult<bool>)result).Reason,
            Is.EqualTo("The actor cannot use reactions.")
        );
        Assert.That(handler.WasCalled, Is.False);
        Assert.That(dispatcher.Snapshot.ActionEconomy[Actor].ReactionAvailable, Is.True);
    }

    private static RuleDispatcher CreateDispatcher(
        ActionProfile profile,
        IActionPermission permission,
        RecordingHandler handler,
        RulesStateSeed seed
    )
    {
        RuleRegistryBuilder registryBuilder = new();
        registryBuilder.Define(ConditionRuleDefinitions.Stunned);
        registryBuilder.Define(SlowedEncounterModule.NoReactionsDefinitionId);
        RuleRegistry registry = registryBuilder.Build();
        return new RuleDispatcherBuilder(new InMemoryRulesStore(seed))
            .RegisterHandler<PermissionTestActionOp, bool>(handler)
            .RegisterActionPermission(permission)
            .UseRuleRegistry(registry)
            .UseActionLifecycle(new FixedCatalog(profile))
            .Build();
    }

    private sealed class PermissionTestActionOp : ActionOp<bool>
    {
        internal PermissionTestActionOp()
            : base(ConditionActionPermissionTests.Actor, Definition) { }
    }

    private sealed class RecordingHandler : IOpHandler<PermissionTestActionOp, bool>
    {
        internal bool WasCalled { get; private set; }

        public ValueTask<bool> Handle(
            OpFrame<PermissionTestActionOp> frame,
            OpHandlerContext context
        )
        {
            WasCalled = true;
            return new ValueTask<bool>(true);
        }
    }

    private sealed class FixedCatalog : IActionCatalog
    {
        private readonly ActionProfile profile;

        internal FixedCatalog(ActionProfile profile) =>
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));

        public ActionProfile GetBaseProfile(ActionDefinitionId definitionId)
        {
            if (definitionId != Definition)
                throw new InvalidOperationException("The test action definition is unknown.");
            return profile;
        }
    }
}
