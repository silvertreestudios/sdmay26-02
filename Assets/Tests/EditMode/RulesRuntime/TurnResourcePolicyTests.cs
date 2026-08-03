using System;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    /// <summary>Verifies pure turn refresh and exact action-resource payment policy.</summary>
    public sealed class TurnResourcePolicyTests
    {
        private static readonly ActionDefinitionId Stride = new("stride");
        private static readonly ActionDefinitionId Strike = new("strike");
        private static readonly ActionProfile OneAction = ActionProfile.OneAction(
            Array.Empty<Trait>()
        );

        [TestCase(1, 2)]
        [TestCase(2, 1)]
        [TestCase(3, 0)]
        [TestCase(4, 0)]
        public void SlowedUsesHighestValueAndClampsStandardActions(int slowed, int expected)
        {
            TurnResourcePlan plan = Resolve(
                TurnResourceContribution.StartTurnLoss(slowed),
                TurnResourceContribution.StartTurnLoss(Math.Max(0, slowed - 1))
            );

            Assert.That(plan.Economy.StandardActionsRemaining, Is.EqualTo(expected));
            Assert.That(plan.Economy.OptionalAction, Is.SameAs(ActionAllowance.None));
            Assert.That(plan.Economy.ReactionAvailable, Is.True);
        }

        [Test]
        public void StunnedAndSlowedUseMaximumWhileStunnedConsumesOnlyItsValue()
        {
            TurnResourcePlan plan = Resolve(
                TurnResourceContribution.StartTurnLoss(2),
                TurnResourceContribution.StunnedBy(StunnedResourcePolicy.Valued(1))
            );

            Assert.That(plan.Economy.StandardActionsRemaining, Is.EqualTo(1));
            Assert.That(plan.StunnedConsumed, Is.EqualTo(1));
            Assert.That(plan.StunnedRemaining, Is.Zero);
            Assert.That(plan.Economy.ReactionAvailable, Is.True);
        }

        [Test]
        public void StunnedFourLeavesOneAndSuppressesReactionWithoutQuickened()
        {
            TurnResourcePlan plan = Resolve(
                TurnResourceContribution.StunnedBy(StunnedResourcePolicy.Valued(4))
            );

            Assert.That(plan.Economy.StandardActionsRemaining, Is.Zero);
            Assert.That(plan.StunnedConsumed, Is.EqualTo(3));
            Assert.That(plan.StunnedRemaining, Is.EqualTo(1));
            Assert.That(plan.Economy.ReactionAvailable, Is.False);
        }

        [Test]
        public void DurationOnlyStunnedLosesEverythingAndSuppressesReactionWithoutConsumption()
        {
            TurnResourcePlan plan = Resolve(
                TurnResourceContribution.OptionalAction(ActionAllowance.Unrestricted),
                TurnResourceContribution.StunnedBy(StunnedResourcePolicy.DurationOnly)
            );

            Assert.That(plan.Economy.StandardActionsRemaining, Is.Zero);
            Assert.That(plan.Economy.OptionalAction.IsNone, Is.True);
            Assert.That(plan.Loss.OptionalAction, Is.True);
            Assert.That(plan.Loss.StandardActions, Is.EqualTo(3));
            Assert.That(plan.StunnedConsumed, Is.Zero);
            Assert.That(plan.Economy.ReactionAvailable, Is.False);
        }

        [Test]
        public void QuickenedSourcesUnionOnceAndUnrestrictedDominates()
        {
            TurnResourcePlan restricted = Resolve(
                TurnResourceContribution.OptionalAction(
                    ActionAllowance.Restricted(new[] { Stride })
                ),
                TurnResourceContribution.OptionalAction(
                    ActionAllowance.Restricted(new[] { Strike })
                )
            );
            TurnResourcePlan unrestricted = Resolve(
                TurnResourceContribution.OptionalAction(restricted.Economy.OptionalAction),
                TurnResourceContribution.OptionalAction(ActionAllowance.Unrestricted)
            );

            Assert.That(restricted.Economy.OptionalAction.Allows(Stride), Is.True);
            Assert.That(restricted.Economy.OptionalAction.Allows(Strike), Is.True);
            Assert.That(
                unrestricted.Economy.OptionalAction,
                Is.SameAs(ActionAllowance.Unrestricted)
            );
        }

        [Test]
        public void StartTurnLossRemovesOptionalBeforeStandard()
        {
            TurnResourcePlan plan = Resolve(
                TurnResourceContribution.OptionalAction(ActionAllowance.Unrestricted),
                TurnResourceContribution.StartTurnLoss(1)
            );

            Assert.That(plan.Loss.OptionalAction, Is.True);
            Assert.That(plan.Loss.StandardActions, Is.Zero);
            Assert.That(plan.Economy.StandardActionsRemaining, Is.EqualTo(3));
            Assert.That(plan.Economy.OptionalAction.IsNone, Is.True);
        }

        [Test]
        public void EligibleOneActionSpendsOptionalBeforeStandard()
        {
            ActionEconomyState economy = new(3, ActionAllowance.Restricted(new[] { Stride }), true);

            bool paid = ActionResourcePayment.TryPay(
                economy,
                Stride,
                OneAction,
                out ActionEconomyState remaining,
                out ActionResourceKind? resource
            );

            Assert.That(paid, Is.True);
            Assert.That(resource, Is.EqualTo(ActionResourceKind.Optional));
            Assert.That(remaining.StandardActionsRemaining, Is.EqualTo(3));
            Assert.That(remaining.OptionalAction.IsNone, Is.True);
        }

        [Test]
        public void IneligibleAndMultiActionCostsUseOnlyStandardActions()
        {
            ActionEconomyState economy = new(3, ActionAllowance.Restricted(new[] { Stride }), true);

            Assert.That(
                ActionResourcePayment.TryPay(
                    economy,
                    Strike,
                    OneAction,
                    out ActionEconomyState ineligible,
                    out ActionResourceKind? ineligibleResource
                ),
                Is.True
            );
            Assert.That(ineligibleResource, Is.EqualTo(ActionResourceKind.Standard));
            Assert.That(ineligible.StandardActionsRemaining, Is.EqualTo(2));
            Assert.That(ineligible.OptionalAction.IsNone, Is.False);

            Assert.That(
                ActionResourcePayment.TryPay(
                    economy,
                    Stride,
                    ActionProfile.Create(ActionCost.Two, Array.Empty<Trait>()),
                    out ActionEconomyState multi,
                    out ActionResourceKind? multiResource
                ),
                Is.True
            );
            Assert.That(multiResource, Is.EqualTo(ActionResourceKind.Standard));
            Assert.That(multi.StandardActionsRemaining, Is.EqualTo(1));
            Assert.That(multi.OptionalAction.IsNone, Is.False);
        }

        [Test]
        public void IndependentReactionSuppressionDoesNotChangeActionResources()
        {
            TurnResourcePlan plan = Resolve(TurnResourceContribution.SuppressReaction());

            Assert.That(plan.Economy.StandardActionsRemaining, Is.EqualTo(3));
            Assert.That(plan.Economy.OptionalAction.IsNone, Is.True);
            Assert.That(plan.Economy.ReactionAvailable, Is.False);
        }

        [Test]
        public void DefaultActionEconomyProjectsNoOptionalActionWithStableValueEquality()
        {
            ActionEconomyState projected = default;
            ActionEconomyState explicitNone = new(0, ActionAllowance.None, false);

            Assert.That(projected.OptionalAction, Is.SameAs(ActionAllowance.None));
            Assert.That(projected, Is.EqualTo(explicitNone));
            Assert.That(projected.GetHashCode(), Is.EqualTo(explicitNone.GetHashCode()));
        }

        private static TurnResourcePlan Resolve(params TurnResourceContribution[] contributions) =>
            TurnResourcePlanner.Resolve(contributions);
    }
}
