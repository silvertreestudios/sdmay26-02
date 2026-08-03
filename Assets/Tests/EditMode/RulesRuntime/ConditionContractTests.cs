using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class ConditionContractTests
    {
        [TestCase("Off-Guard", "condition-off-guard")]
        [TestCase("off guard", "condition-off-guard")]
        [TestCase("Flat-Footed", "condition-off-guard")]
        [TestCase("Deafened", "condition-deafened")]
        [TestCase("Fatigued", "condition-fatigued")]
        [TestCase("Encumbered", "condition-encumbered")]
        [TestCase("Slowed", "condition-slowed")]
        [TestCase("Stunned", "condition-stunned")]
        [TestCase("Quickened", "condition-quickened")]
        public void InputNamesNormalizeToCanonicalDefinitions(string input, string expected)
        {
            bool recognized = ConditionInputNormalizer.TryNormalize(
                input,
                out RuleDefinitionId definitionId
            );

            Assert.That(recognized, Is.True);
            Assert.That(definitionId.Value, Is.EqualTo(expected));
            Assert.That(definitionId.Value, Does.Not.Contain("flat-footed"));
        }

        [Test]
        public void UnknownInputDoesNotCreateARuntimeDefinition()
        {
            bool recognized = ConditionInputNormalizer.TryNormalize(
                "not-a-condition",
                out RuleDefinitionId definitionId
            );

            Assert.That(recognized, Is.False);
            Assert.That(definitionId.IsEmpty, Is.True);
        }

        [Test]
        public void MarkerConditionsShareOneImmutableStateType()
        {
            ConditionMarkerState marker = ConditionMarkerState.Instance;

            Assert.That(ConditionMarkerState.Instance, Is.SameAs(marker));
            Assert.That(
                new[]
                {
                    ConditionRuleDefinitions.OffGuard,
                    ConditionRuleDefinitions.Deafened,
                    ConditionRuleDefinitions.Fatigued,
                    ConditionRuleDefinitions.Encumbered,
                }.All(definition => ConditionRuleDefinitions.Accepts(definition, marker)),
                Is.True
            );
            Assert.That(
                ConditionRuleDefinitions.Accepts(ConditionRuleDefinitions.Slowed, marker),
                Is.False
            );
        }

        [Test]
        public void SlowedRequiresAPositiveValue()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SlowedConditionState(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SlowedConditionState(-1));

            SlowedConditionState state = new SlowedConditionState(2);
            Assert.That(state.Value, Is.EqualTo(2));
            Assert.That(state, Is.EqualTo(new SlowedConditionState(2)));
        }

        [Test]
        public void StunnedUsesStructuralValuedOrDurationOnlyAlternatives()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ValuedStunnedConditionState(0));

            StunnedConditionState valued = new ValuedStunnedConditionState(3);
            StunnedConditionState durationOnly = DurationOnlyStunnedConditionState.Instance;

            Assert.That(valued, Is.TypeOf<ValuedStunnedConditionState>());
            Assert.That(durationOnly, Is.TypeOf<DurationOnlyStunnedConditionState>());
            Assert.That(
                valued
                    .GetType()
                    .GetProperties()
                    .Any(property => property.Name.Contains("Duration")),
                Is.False
            );
            Assert.That(
                durationOnly
                    .GetType()
                    .GetProperties()
                    .Any(property => property.Name.Contains("Value")),
                Is.False
            );
            Assert.That(
                ConditionRuleDefinitions.Accepts(ConditionRuleDefinitions.Stunned, valued),
                Is.True
            );
            Assert.That(
                ConditionRuleDefinitions.Accepts(ConditionRuleDefinitions.Stunned, durationOnly),
                Is.True
            );
        }

        [Test]
        public void QuickenedCopiesCanonicalizesAndRequiresAllowedActions()
        {
            ActionDefinitionId stride = new ActionDefinitionId("stride");
            ActionDefinitionId strike = new ActionDefinitionId("strike");
            List<ActionDefinitionId> input = new List<ActionDefinitionId>
            {
                strike,
                stride,
                strike,
            };

            QuickenedConditionState state = new QuickenedConditionState(input);
            input.Clear();

            Assert.That(state.AllowedActions, Is.EqualTo(new[] { stride, strike }));
            Assert.That(state.Allows(stride), Is.True);
            Assert.That(state.Allows(new ActionDefinitionId("cast-spell")), Is.False);
            Assert.That(state, Is.EqualTo(new QuickenedConditionState(new[] { strike, stride })));
            Assert.Throws<ArgumentException>(() =>
                new QuickenedConditionState(Array.Empty<ActionDefinitionId>())
            );
            Assert.Throws<NotSupportedException>(() =>
                ((IList<ActionDefinitionId>)state.AllowedActions).Add(
                    new ActionDefinitionId("step")
                )
            );
        }
    }
}
