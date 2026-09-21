using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    /// <summary>Verifies pure, snapshot-based Flanking eligibility and invalid inputs.</summary>
    public sealed class FlankingRulesTests
    {
        private static readonly CreatureId Attacker = new("flanking-attacker");
        private static readonly CreatureId Target = new("flanking-target");
        private static readonly CreatureId Ally = new("flanking-ally");
        private static readonly CreatureId Other = new("flanking-other");

        [Test]
        public void OppositeLivingAllyWhoThreatensGrantsFlanking()
        {
            RulesSnapshot snapshot = CreateSnapshot(
                new GridPosition(-1, 0, 0),
                new GridPosition(0, 0, 0),
                new GridPosition(1, 0, 0)
            );

            bool result = FlankingRules.IsFlanking(
                snapshot,
                Attacker,
                Target,
                Context(Participant(Ally))
            );

            Assert.That(result, Is.True);
            Assert.That(snapshot.Version, Is.Zero);
        }

        [Test]
        public void OppositeCornersGrantFlanking()
        {
            RulesSnapshot snapshot = CreateSnapshot(
                new GridPosition(-1, 0, -1),
                new GridPosition(0, 0, 0),
                new GridPosition(1, 0, 1)
            );

            Assert.That(
                FlankingRules.IsFlanking(snapshot, Attacker, Target, Context(Participant(Ally))),
                Is.True
            );
        }

        [TestCase(1, 1)]
        [TestCase(-1, 1)]
        [TestCase(-1, 0)]
        public void AllyWhoIsNotOnOppositeSideOrCornerDoesNotGrantFlanking(int allyX, int allyZ)
        {
            RulesSnapshot snapshot = CreateSnapshot(
                new GridPosition(-1, 0, 0),
                new GridPosition(0, 0, 0),
                new GridPosition(allyX, 0, allyZ)
            );

            Assert.That(
                FlankingRules.IsFlanking(snapshot, Attacker, Target, Context(Participant(Ally))),
                Is.False
            );
            Assert.That(snapshot.Version, Is.Zero);
        }

        [Test]
        public void DefeatedUnavailableOrNonThreateningParticipantsDoNotGrantFlanking()
        {
            RulesSnapshot defeated = CreateSnapshot(
                new GridPosition(-1, 0, 0),
                new GridPosition(0, 0, 0),
                new GridPosition(1, 0, 0),
                allyHitPoints: 0
            );

            Assert.That(
                FlankingRules.IsFlanking(defeated, Attacker, Target, Context(Participant(Ally))),
                Is.False
            );
            Assert.That(
                FlankingRules.IsFlanking(
                    CreateSnapshot(
                        new GridPosition(-1, 0, 0),
                        new GridPosition(0, 0, 0),
                        new GridPosition(1, 0, 0)
                    ),
                    Attacker,
                    Target,
                    Context(Participant(Ally, canFlank: false))
                ),
                Is.False
            );
            Assert.That(
                FlankingRules.IsFlanking(
                    CreateSnapshot(
                        new GridPosition(-1, 0, 0),
                        new GridPosition(0, 0, 0),
                        new GridPosition(1, 0, 0)
                    ),
                    Attacker,
                    Target,
                    Context(Participant(Ally, threatensTarget: false))
                ),
                Is.False
            );
        }

        [Test]
        public void NonCooperatingOrTargetFriendlyParticipantsDoNotGrantFlanking()
        {
            RulesSnapshot snapshot = CreateSnapshot(
                new GridPosition(-1, 0, 0),
                new GridPosition(0, 0, 0),
                new GridPosition(1, 0, 0)
            );

            Assert.That(
                FlankingRules.IsFlanking(
                    snapshot,
                    Attacker,
                    Target,
                    Context(Participant(Ally, friendlyToAttacker: false))
                ),
                Is.False
            );
            Assert.That(
                FlankingRules.IsFlanking(
                    snapshot,
                    Attacker,
                    Target,
                    Context(Participant(Ally, friendlyToTarget: true))
                ),
                Is.False
            );
        }

        [Test]
        public void MissingRosterPositionOrAttackerThreatLeavesSnapshotUnchanged()
        {
            RulesStateSeed seed = Seed(Attacker, new GridPosition(-1, 0, 0), 10)
                .SeedCreature(new CreatureState(Target, new PlayerId("target-side")))
                .SeedHealth(Target, new HealthState(10, 10))
                .SeedCreature(new CreatureState(Ally, new PlayerId("ally-side")))
                .SeedHealth(Ally, new HealthState(10, 10))
                .SeedPosition(Ally, new GridPosition(1, 0, 0));
            RulesSnapshot missingTargetPosition = new InMemoryRulesStore(seed).Snapshot;

            Assert.That(
                FlankingRules.IsFlanking(
                    missingTargetPosition,
                    Attacker,
                    Target,
                    Context(Participant(Ally))
                ),
                Is.False
            );
            Assert.That(
                FlankingRules.IsFlanking(
                    CreateSnapshot(
                        new GridPosition(-1, 0, 0),
                        new GridPosition(0, 0, 0),
                        new GridPosition(1, 0, 0)
                    ),
                    Attacker,
                    Target,
                    new FlankingContext(true, true, false, new[] { Participant(Ally) })
                ),
                Is.False
            );
            Assert.That(missingTargetPosition.Version, Is.Zero);
        }

        [Test]
        public void ContextCopiesAndOrdersParticipantsAndRejectsDuplicateIdentity()
        {
            List<FlankingParticipant> source = new() { Participant(Other), Participant(Ally) };
            FlankingContext context = Context(source.ToArray());
            source.Clear();

            Assert.That(context.Participants, Has.Count.EqualTo(2));
            Assert.That(context.Participants[0].Creature, Is.EqualTo(Ally));
            Assert.That(context.Participants[1].Creature, Is.EqualTo(Other));
            Assert.Throws<ArgumentException>(() => Context(Participant(Ally), Participant(Ally)));
        }

        private static RulesSnapshot CreateSnapshot(
            GridPosition attackerPosition,
            GridPosition targetPosition,
            GridPosition allyPosition,
            int allyHitPoints = 10
        )
        {
            RulesStateSeed seed = Seed(Attacker, attackerPosition, 10);
            Seed(Target, targetPosition, 10, seed);
            Seed(Ally, allyPosition, allyHitPoints, seed);
            return new InMemoryRulesStore(seed).Snapshot;
        }

        private static RulesStateSeed Seed(
            CreatureId creature,
            GridPosition position,
            int hitPoints,
            RulesStateSeed seed = null
        )
        {
            seed ??= new RulesStateSeed();
            int maximum = Math.Max(10, hitPoints);
            return seed.SeedCreature(
                    new CreatureState(creature, new PlayerId($"{creature.Value}-side"))
                )
                .SeedHealth(creature, new HealthState(hitPoints, maximum))
                .SeedPosition(creature, position);
        }

        private static FlankingContext Context(params FlankingParticipant[] participants) =>
            new(true, true, true, participants);

        private static FlankingParticipant Participant(
            CreatureId creature,
            bool canFlank = true,
            bool friendlyToAttacker = true,
            bool friendlyToTarget = false,
            bool threatensTarget = true
        ) => new(creature, canFlank, friendlyToAttacker, friendlyToTarget, threatensTarget);
    }
}
