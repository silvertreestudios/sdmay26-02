using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class ConditionContractTests
    {
        [Test]
        public void ActionAllowanceRequiresValidRestrictedDefinitionsAndCopiesInput()
        {
            ActionDefinitionId stride = new ActionDefinitionId("stride");
            List<ActionDefinitionId> input = new List<ActionDefinitionId> { stride };

            ActionAllowance allowance = ActionAllowance.Restricted(input);
            input.Clear();

            Assert.That(allowance.AllowedActions, Is.EqualTo(new[] { stride }));
            Assert.That(allowance.IsRestricted, Is.True);
            Assert.That(allowance.Allows(stride), Is.True);
            Assert.That(allowance.Allows(new ActionDefinitionId("strike")), Is.False);
            Assert.That(ActionAllowance.None.IsNone, Is.True);
            Assert.That(ActionAllowance.None.Allows(stride), Is.False);
            Assert.That(ActionAllowance.Unrestricted.Allows(stride), Is.True);
            Assert.Throws<ArgumentNullException>(() => ActionAllowance.Restricted(null));
            Assert.Throws<ArgumentException>(() =>
                ActionAllowance.Restricted(Array.Empty<ActionDefinitionId>())
            );
            Assert.Throws<ArgumentException>(() =>
                ActionAllowance.Restricted(new[] { default(ActionDefinitionId) })
            );
            Assert.Throws<ArgumentException>(() => allowance.Allows(default));
            Assert.Throws<ArgumentNullException>(() => allowance.Allows(stride, null));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<ActionDefinitionId>)allowance.AllowedActions).Add(
                    new ActionDefinitionId("step")
                )
            );
        }

        [Test]
        public void RestrictedActionAllowancePaysOnlyAnAllowedSingleAction()
        {
            ActionDefinitionId stride = new ActionDefinitionId("stride");
            ActionAllowance allowance = ActionAllowance.Restricted(new[] { stride });

            Assert.That(
                allowance.Allows(stride, ActionProfile.OneAction(Array.Empty<Trait>())),
                Is.True
            );
            Assert.That(
                allowance.Allows(
                    stride,
                    ActionProfile.Create(ActionCost.Two, Array.Empty<Trait>())
                ),
                Is.False
            );
            Assert.That(
                allowance.Allows(
                    stride,
                    ActionProfile.Create(ActionCost.Three, Array.Empty<Trait>())
                ),
                Is.False
            );
            Assert.That(
                allowance.Allows(
                    new ActionDefinitionId("activity-containing-stride"),
                    ActionProfile.OneAction(Array.Empty<Trait>())
                ),
                Is.False
            );
        }

        [Test]
        public void ActionAllowanceCanonicalOrderingDefinesEqualityAndHashing()
        {
            ActionDefinitionId stride = new ActionDefinitionId("stride");
            ActionDefinitionId strike = new ActionDefinitionId("strike");
            ActionAllowance first = ActionAllowance.Restricted(new[] { strike, stride, strike });
            ActionAllowance second = ActionAllowance.Restricted(new[] { stride, strike });

            Assert.That(first.AllowedActions, Is.EqualTo(new[] { stride, strike }));
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
            Assert.That(first == second, Is.True);
            Assert.That(first, Is.Not.EqualTo(ActionAllowance.None));
            Assert.That(ActionAllowance.None, Is.Not.EqualTo(ActionAllowance.Unrestricted));
        }

        [Test]
        public void ActionAllowanceUnionCombinesRestrictionsAndUnrestrictedDominates()
        {
            ActionAllowance stride = ActionAllowance.Restricted(
                new[] { new ActionDefinitionId("stride") }
            );
            ActionAllowance strike = ActionAllowance.Restricted(
                new[] { new ActionDefinitionId("strike") }
            );
            ActionAllowance combined = stride.Union(strike);

            Assert.That(
                combined.AllowedActions,
                Is.EqualTo(
                    new[] { new ActionDefinitionId("stride"), new ActionDefinitionId("strike") }
                )
            );
            Assert.That(ActionAllowance.None.Union(stride), Is.SameAs(stride));
            Assert.That(stride.Union(ActionAllowance.None), Is.SameAs(stride));
            Assert.That(
                combined.Union(ActionAllowance.Unrestricted),
                Is.SameAs(ActionAllowance.Unrestricted)
            );
            Assert.Throws<ArgumentNullException>(() => stride.Union(null));
        }

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

            Assert.That(
                state.Allowance,
                Is.EqualTo(ActionAllowance.Restricted(new[] { strike, stride }))
            );
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
