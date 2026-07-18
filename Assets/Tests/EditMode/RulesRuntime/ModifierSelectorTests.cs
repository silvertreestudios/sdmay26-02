using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    /// <summary>
    /// Verifies typed stacking, immutable statistics state, and pure snapshot selectors.
    /// </summary>
    public sealed class ModifierSelectorTests
    {
        private static readonly CreatureId Actor = new CreatureId("selector-actor");
        private static readonly CreatureId Ally = new CreatureId("selector-ally");
        private static readonly CreatureId Enemy = new CreatureId("selector-enemy");
        private static readonly RuleSource Base = RuleSource.FromSlug("base");
        private static readonly RuleSource Cover = RuleSource.FromSlug("cover");
        private static readonly RuleSource GreaterCover = RuleSource.FromSlug("greater-cover");
        private static readonly RuleSource Opening = RuleSource.FromSlug("opening");
        private static readonly RuleSource OffGuard = RuleSource.FromSlug("off-guard");

        [Test]
        public void CollectionPreservesLegacyTypedStackingAndAuditDetails()
        {
            ModifierCollection collection = new ModifierCollection(
                Statistic.ArmorClass,
                new[]
                {
                    Modifier.Untyped(2, Base, Statistic.ArmorClass),
                    new Modifier(1, ModifierType.Circumstance, Cover, Statistic.ArmorClass),
                    new Modifier(2, ModifierType.Circumstance, GreaterCover, Statistic.ArmorClass),
                    new Modifier(-1, ModifierType.Circumstance, Opening, Statistic.ArmorClass),
                    new Modifier(-2, ModifierType.Circumstance, OffGuard, Statistic.ArmorClass),
                    new Modifier(3, ModifierType.Item, RuleSource.FromSlug("armor"), Statistic.ArmorClass),
                    new Modifier(1, ModifierType.Status, RuleSource.FromSlug("ward"), Statistic.ArmorClass),
                    Modifier.Untyped(100, RuleSource.FromSlug("wrong-statistic"), Statistic.AttackRoll)
                });

            Assert.That(collection.Total, Is.EqualTo(6));
            Assert.That(collection.Applied.Select(modifier => modifier.Source), Is.EqualTo(new[]
            {
                Base,
                GreaterCover,
                OffGuard,
                RuleSource.FromSlug("armor"),
                RuleSource.FromSlug("ward")
            }));
            Assert.That(collection.Suppressed.Select(modifier => modifier.Source), Is.EqualTo(new[]
            {
                Cover,
                Opening
            }));
            Assert.That(collection.Candidates, Has.Count.EqualTo(8),
                "Ignored-statistic candidates remain available for a complete collection audit.");
        }

        [Test]
        public void EqualTypedModifiersUseDeterministicInputOrderForTheAppliedValue()
        {
            Modifier first = Modifier.StatusBonus(
                1,
                RuleSource.FromSlug("first"),
                Statistic.AttackRoll);
            Modifier second = Modifier.StatusBonus(
                1,
                RuleSource.FromSlug("second"),
                Statistic.AttackRoll);

            ModifierCollection collection = new ModifierCollection(
                Statistic.AttackRoll,
                new[] { first, second });

            Assert.That(collection.Applied.Single(), Is.EqualTo(first));
            Assert.That(collection.Suppressed.Single(), Is.EqualTo(second));
        }

        [Test]
        public void CollectionRejectsUninitializedModifierValues()
        {
            Assert.That(default(Modifier).IsEmpty, Is.True);
            Assert.Throws<ArgumentException>(() => new ModifierCollection(
                Statistic.AttackRoll,
                new[] { default(Modifier) }));
        }

        [Test]
        public void SkillsProvidePredefinedAndOpenContentIdentities()
        {
            Skill sailingLore = Skill.FromName("Sailing Lore");

            Assert.That(Skill.Acrobatics.Slug, Is.EqualTo("acrobatics"));
            Assert.That(sailingLore.Slug, Is.EqualTo("sailing-lore"));
            Assert.That(sailingLore, Is.EqualTo(Skill.FromSlug("sailing-lore")));
            Assert.That(default(Skill).IsEmpty, Is.True);
            Assert.Throws<ArgumentException>(() => Skill.FromName(" "));
        }

        [Test]
        public void StatisticsCopyCallerCollectionsAndSeedByCreatureIdentity()
        {
            Dictionary<Skill, int> skills = new Dictionary<Skill, int>
            {
                [Skill.Acrobatics] = 7
            };
            List<Modifier> modifiers = new List<Modifier>
            {
                Modifier.StatusBonus(1, RuleSource.FromSlug("initial"), Statistic.SkillCheck)
            };
            CreatureStatisticsState statistics = new CreatureStatisticsState(
                Actor,
                8,
                18,
                6,
                7,
                5,
                skills,
                modifiers);

            skills[Skill.Acrobatics] = 100;
            modifiers.Clear();
            RulesSnapshot snapshot = new InMemoryRulesStore(
                new RulesStateSeed().SeedStatistics(statistics)).Snapshot;

            Assert.That(statistics.GetSkillModifier(Skill.Acrobatics), Is.EqualTo(7));
            Assert.That(statistics.Modifiers, Has.Count.EqualTo(1));
            Assert.That(snapshot.Statistics[Actor], Is.SameAs(statistics));
            Assert.Throws<ArgumentException>(() => new CreatureStatisticsState(
                Actor,
                8,
                18,
                6,
                7,
                5,
                skills,
                new[] { default(Modifier) }));
        }

        [Test]
        public void StandardSelectorsResolveDefensesTeamsDistanceAndCurrentModifiers()
        {
            RuleSource status = RuleSource.FromSlug("status-effect");
            RuleSource item = RuleSource.FromSlug("item-bonus");
            RuleSource circumstance = RuleSource.FromSlug("circumstance-effect");
            Modifier[] actorModifiers =
            {
                Modifier.StatusBonus(1, status, Statistic.ArmorClass),
                Modifier.StatusBonus(2, status, Statistic.SkillCheck),
                new Modifier(1, ModifierType.Circumstance, circumstance, Statistic.ReflexSave),
                new Modifier(1, ModifierType.Item, item, Statistic.AttackRoll)
            };
            PlayerId players = new PlayerId("players");
            PlayerId enemies = new PlayerId("enemies");
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Actor, players, Array.Empty<Trait>()))
                .SeedCreature(new CreatureState(Ally, players, Array.Empty<Trait>()))
                .SeedCreature(new CreatureState(Enemy, enemies, Array.Empty<Trait>()))
                .SeedStatistics(new CreatureStatisticsState(
                    Actor,
                    7,
                    18,
                    6,
                    8,
                    5,
                    new Dictionary<Skill, int> { [Skill.Acrobatics] = 7 },
                    actorModifiers))
                .SeedMultipleAttackPenalty(Actor, new MultipleAttackPenaltyState(1))
                .SeedPosition(Actor, new GridPosition(0, 0, 0))
                .SeedPosition(Enemy, new GridPosition(2, 4, 2));
            RulesSnapshot snapshot = new InMemoryRulesStore(seed).Snapshot;
            RulesSelectors selectors = new RulesSelectors();

            Assert.That(selectors.GetArmorClass(snapshot, Actor), Is.EqualTo(19));
            Assert.That(selectors.GetSaveDifficultyClass(snapshot, Actor, SaveKind.Reflex),
                Is.EqualTo(19));
            Assert.That(selectors.GetAttackModifiers(snapshot, Actor).Total, Is.EqualTo(8));
            Assert.That(selectors.GetSkillCheckModifiers(snapshot, Actor, Skill.Acrobatics).Total,
                Is.EqualTo(9));
            Assert.That(selectors.GetCurrentModifiers(snapshot, Actor, Statistic.AttackRoll).Total,
                Is.EqualTo(1));
            Assert.That(selectors.GetMultipleAttackPenalty(snapshot, Actor, false), Is.EqualTo(-5));
            Assert.That(selectors.GetMultipleAttackPenalty(snapshot, Actor, true), Is.EqualTo(-4));
            Assert.That(selectors.IsEnemy(snapshot, Actor, Enemy), Is.True);
            Assert.That(selectors.IsEnemy(snapshot, Actor, Ally), Is.False);
            Assert.That(selectors.Distance(snapshot, Actor, Enemy), Is.EqualTo(new GridDistance(15)),
                "Current grid rules measure the horizontal X/Z plane and ignore presentation height.");
        }

        [Test]
        public void MultipleAttackPenaltyPreservesNormalAndAgileProgression()
        {
            Assert.That(MultipleAttackPenaltyResolver.Resolve(0, false), Is.Zero);
            Assert.That(MultipleAttackPenaltyResolver.Resolve(1, false), Is.EqualTo(-5));
            Assert.That(MultipleAttackPenaltyResolver.Resolve(2, false), Is.EqualTo(-10));
            Assert.That(MultipleAttackPenaltyResolver.Resolve(1, true), Is.EqualTo(-4));
            Assert.That(MultipleAttackPenaltyResolver.Resolve(2, true), Is.EqualTo(-8));
            Assert.That(MultipleAttackPenaltyResolver.Resolve(5, true), Is.EqualTo(-8));
        }

        [Test]
        public void SaveDifficultyClassRejectsAnUnrepresentableTotal()
        {
            CreatureStatisticsState statistics = new CreatureStatisticsState(
                Actor,
                0,
                0,
                int.MaxValue,
                0,
                0,
                new Dictionary<Skill, int>(),
                Array.Empty<Modifier>());
            RulesSnapshot snapshot = new InMemoryRulesStore(
                new RulesStateSeed().SeedStatistics(statistics)).Snapshot;

            Assert.Throws<OverflowException>(() =>
                new RulesSelectors().GetSaveDifficultyClass(
                    snapshot,
                    Actor,
                    SaveKind.Fortitude));
        }

        [Test]
        public void SelectorsRejectMissingRequiredSeedDataInsteadOfInventingValues()
        {
            PlayerId player = new PlayerId("player");
            RulesSnapshot snapshot = new InMemoryRulesStore(
                new RulesStateSeed().SeedCreature(
                    new CreatureState(Actor, player, Array.Empty<Trait>()))).Snapshot;
            RulesSelectors selectors = new RulesSelectors();

            Assert.Throws<KeyNotFoundException>(() =>
                selectors.GetAttackModifiers(snapshot, Actor));
            Assert.Throws<KeyNotFoundException>(() =>
                selectors.Distance(snapshot, Actor, Enemy));
        }
    }
}
