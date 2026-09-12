using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class ConditionRulesTests
    {
        private static readonly CreatureId Target = new("target");
        private static readonly CreatureId Caster = new("caster");
        private static readonly ConditionId Slowed = SlowedRules.ConditionId;
        private static readonly RuleSource Source = RuleSource.FromSlug("test-condition");

        [Test]
        public void SlowedCreatesOneStableEffectIndependentBindingPerOwner()
        {
            ActiveRuleBinding binding = SlowedRules.CreateBinding(Target);

            Assert.That(binding.Id, Is.EqualTo(new BindingId("slowed-binding:target")));
            Assert.That(binding.DefinitionId, Is.EqualTo(SlowedRules.DefinitionId));
            Assert.That(binding.Owner, Is.EqualTo(Target));
            Assert.That(binding.EffectId, Is.Null);
            Assert.That(binding.Source, Is.EqualTo(SlowedRules.Source));
            Assert.That(binding.CreationOrder, Is.Zero);
            Assert.That(binding.IsEnabled, Is.True);
        }

        [Test]
        public async Task ApplicationKeepsSourceTargetAndIndependentIdentity()
        {
            RuleDispatcher dispatcher = CreateDispatcher();
            ActiveEffectCreationOutcome first = await Apply(dispatcher, 1);
            ActiveEffectCreationOutcome second = await Apply(dispatcher, 2);

            Assert.That(first.EffectId, Is.Not.EqualTo(second.EffectId));
            Assert.That(
                dispatcher.Snapshot.ActiveEffects[first.EffectId].SourceCreature,
                Is.EqualTo(Caster)
            );
            Assert.That(
                dispatcher.Snapshot.RuleBindings[first.BindingId].Owner,
                Is.EqualTo(Target)
            );
            Assert.That(
                ConditionRules.GetApplications(dispatcher.Snapshot, Target).Count(),
                Is.EqualTo(2)
            );
            Assert.That(
                ConditionRules.GetValue(dispatcher.Snapshot, Target, Slowed),
                Is.EqualTo(2)
            );
            Assert.That(ConditionRules.GetValue(dispatcher.Snapshot, Caster, Slowed), Is.Zero);
        }

        [TestCase(1, 2)]
        [TestCase(2, 1)]
        [TestCase(2, 2)]
        public async Task RemovingOneApplicationLeavesTheOther(int removedValue, int remainingValue)
        {
            RuleDispatcher dispatcher = CreateDispatcher();
            ActiveEffectCreationOutcome removed = await Apply(dispatcher, removedValue);
            ActiveEffectCreationOutcome remaining = await Apply(dispatcher, remainingValue);
            RulesSnapshot before = dispatcher.Snapshot;

            await Remove(dispatcher, removed);

            Assert.That(
                ConditionRules.GetValue(dispatcher.Snapshot, Target, Slowed),
                Is.EqualTo(remainingValue)
            );
            Assert.That(dispatcher.Snapshot.ActiveEffects.Contains(removed.EffectId), Is.False);
            Assert.That(dispatcher.Snapshot.ActiveEffects.Contains(remaining.EffectId), Is.True);
            Assert.That(
                ConditionRules.GetValue(before, Target, Slowed),
                Is.EqualTo(Math.Max(removedValue, remainingValue)),
                "The prior immutable snapshot retains both applications."
            );
            await Remove(dispatcher, remaining);
            Assert.That(ConditionRules.GetValue(dispatcher.Snapshot, Target, Slowed), Is.Zero);
        }

        [Test]
        public async Task SameRootCanApplySeveralConditionsWithoutIdentityCollisions()
        {
            RuleDispatcher dispatcher = CreateDispatcher();
            await dispatcher.Dispatch(new PairOp());
            Assert.That(
                ConditionRules.GetApplications(dispatcher.Snapshot, Target).Count(),
                Is.EqualTo(2)
            );
            Assert.That(
                ConditionRules.GetValue(dispatcher.Snapshot, Target, Slowed),
                Is.EqualTo(2)
            );
        }

        [Test]
        public async Task QueryDoesNotMixDifferentConditionKinds()
        {
            RuleDispatcher dispatcher = CreateDispatcher();
            await Apply(dispatcher, 2);
            ConditionId other = new("test-other-condition");
            await dispatcher.Dispatch(
                new ApplyConditionOp(Target, other, 5, Caster, Source, EffectDuration.Indefinite)
            );
            Assert.That(
                ConditionRules.GetValue(dispatcher.Snapshot, Target, Slowed),
                Is.EqualTo(2)
            );
            Assert.That(ConditionRules.GetValue(dispatcher.Snapshot, Target, other), Is.EqualTo(5));
        }

        [Test]
        public async Task EqualValuesWithDifferentDurationsKeepSeparateApplications()
        {
            RuleDispatcher dispatcher = CreateDispatcher();
            await Apply(dispatcher, 2);
            var result = await dispatcher.Dispatch(
                new ApplyConditionOp(Target, Slowed, 2, Caster, Source, EffectDuration.OneMinute)
            );
            var timed = ((ResolvedOpResult<ActiveEffectCreationOutcome>)result).Value;
            Assert.That(
                dispatcher.Snapshot.ActiveEffectTimings[timed.EffectId].RemainingBoundaries,
                Is.EqualTo(10)
            );
            await Remove(dispatcher, timed);
            Assert.That(
                ConditionRules.GetValue(dispatcher.Snapshot, Target, Slowed),
                Is.EqualTo(2)
            );
            Assert.That(dispatcher.Snapshot.ActiveEffectTimings.Contains(timed.EffectId), Is.False);
        }

        [Test]
        public void ValuesHaveNoThreeActionCapButMustBePositive()
        {
            Assert.That(new ConditionState(Slowed, int.MaxValue).Value, Is.EqualTo(int.MaxValue));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConditionState(Slowed, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConditionState(Slowed, -1));
        }

        private static RuleDispatcher CreateDispatcher()
        {
            RuleRegistryBuilder registry = new();
            registry.Define(ConditionRules.DefinitionId);
            PlayerId player = new("test-player");
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Target, player))
                .SeedCreature(new CreatureState(Caster, player))
                .SeedEncounter(
                    new EncounterState(
                        new EncounterId("conditions-test"),
                        EncounterPhase.Active,
                        player,
                        RoundNumber.First,
                        new[] { new InitiativeEntry(Caster, player, 10, 0, 0, RoundNumber.First) },
                        -1,
                        null,
                        1,
                        null
                    )
                );
            RuleDispatcherBuilder builder = new RuleDispatcherBuilder(new InMemoryRulesStore(seed))
                .UseActiveEffectRules(registry.Build())
                .RegisterHandler<RemoveOp, OpResult<ActiveEffectRemovalOutcome>>(
                    new RemoveHandler()
                )
                .RegisterHandler<PairOp, int>(new PairHandler());
            ConditionRules.ConfigureDispatcher(builder);
            return builder.Build();
        }

        private static async Task<ActiveEffectCreationOutcome> Apply(
            RuleDispatcher dispatcher,
            int value
        )
        {
            var result = await dispatcher.Dispatch(
                new ApplyConditionOp(
                    Target,
                    Slowed,
                    value,
                    Caster,
                    Source,
                    EffectDuration.Indefinite
                )
            );
            return ((ResolvedOpResult<ActiveEffectCreationOutcome>)result).Value;
        }

        private static async Task Remove(
            RuleDispatcher dispatcher,
            ActiveEffectCreationOutcome effect
        )
        {
            var result = await dispatcher.Dispatch(
                new RemoveOp(
                    new RemoveActiveEffectOp(
                        effect.EffectId,
                        effect.BindingId,
                        dispatcher.Snapshot.ActiveEffects[effect.EffectId].EffectStateVersion,
                        ActiveEffectRemovalReason.Ended,
                        Source
                    )
                )
            );
            Assert.That(
                ((ResolvedOpResult<OpResult<ActiveEffectRemovalOutcome>>)result).Value,
                Is.TypeOf<ResolvedOpResult<ActiveEffectRemovalOutcome>>()
            );
        }

        private sealed class RemoveOp : IRuleOp<OpResult<ActiveEffectRemovalOutcome>>
        {
            internal RemoveOp(RemoveActiveEffectOp removal) => Removal = removal;

            internal RemoveActiveEffectOp Removal { get; }
        }

        private sealed class RemoveHandler
            : IOpHandler<RemoveOp, OpResult<ActiveEffectRemovalOutcome>>
        {
            public async ValueTask<OpResult<ActiveEffectRemovalOutcome>> Handle(
                OpFrame<RemoveOp> frame,
                OpHandlerContext context
            ) => await context.Dispatch(frame.Op.Removal);
        }

        private sealed class PairOp : IRuleOp<int> { }

        private sealed class PairHandler : IOpHandler<PairOp, int>
        {
            public async ValueTask<int> Handle(OpFrame<PairOp> frame, OpHandlerContext context)
            {
                await context.Dispatch(
                    new ApplyConditionOp(
                        Target,
                        Slowed,
                        1,
                        Caster,
                        Source,
                        EffectDuration.Indefinite
                    )
                );
                await context.Dispatch(
                    new ApplyConditionOp(
                        Target,
                        Slowed,
                        2,
                        Caster,
                        Source,
                        EffectDuration.Indefinite
                    )
                );
                return 2;
            }
        }
    }
}
