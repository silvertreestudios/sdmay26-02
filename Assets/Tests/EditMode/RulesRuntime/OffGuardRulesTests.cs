using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    /// <summary>Verifies authoritative Off-Guard membership and its retained data alias.</summary>
    public sealed class OffGuardRulesTests
    {
        private static readonly CreatureId Target = new("off-guard-target");
        private static readonly PlayerId Player = new("off-guard-player");
        private static readonly RuleSource Source = RuleSource.FromSlug("off-guard-test");

        [TestCase("Off-Guard")]
        [TestCase("Flat-Footed")]
        public void EnabledApplicationMakesCreatureOffGuard(string condition)
        {
            RulesSnapshot snapshot = CreateSnapshot(new ConditionId(condition), isEnabled: true);

            Assert.That(OffGuardRules.IsOffGuard(snapshot, Target), Is.True);
            Assert.That(snapshot.Version, Is.Zero);
        }

        [Test]
        public void DisabledMissingOrWrongConditionDoesNotMakeCreatureOffGuard()
        {
            RulesSnapshot disabled = CreateSnapshot(OffGuardRules.ConditionId, isEnabled: false);
            RulesSnapshot wrong = CreateSnapshot(new ConditionId("Slowed"), isEnabled: true);
            RulesSnapshot empty = new InMemoryRulesStore(
                new RulesStateSeed().SeedCreature(new CreatureState(Target, Player))
            ).Snapshot;

            Assert.That(OffGuardRules.IsOffGuard(disabled, Target), Is.False);
            Assert.That(OffGuardRules.IsOffGuard(wrong, Target), Is.False);
            Assert.That(OffGuardRules.IsOffGuard(empty, Target), Is.False);
            Assert.That(OffGuardRules.IsOffGuard(empty, new CreatureId("missing")), Is.False);
            Assert.That(empty.Version, Is.Zero);
        }

        private static RulesSnapshot CreateSnapshot(ConditionId condition, bool isEnabled)
        {
            ActiveEffectId effectId = new("off-guard-effect");
            BindingId bindingId = new("off-guard-binding");
            ActiveEffectInstance effect = new(
                effectId,
                ConditionRules.DefinitionId,
                Target,
                Source,
                EffectDuration.Indefinite,
                new ConditionState(condition, 1)
            );
            ActiveRuleBinding binding = new(
                bindingId,
                ConditionRules.DefinitionId,
                Target,
                effectId,
                Source,
                0,
                isEnabled
            );
            return new InMemoryRulesStore(
                new RulesStateSeed()
                    .SeedCreature(new CreatureState(Target, Player))
                    .SeedActiveEffect(effect)
                    .SeedRuleBinding(binding)
            ).Snapshot;
        }
    }
}
