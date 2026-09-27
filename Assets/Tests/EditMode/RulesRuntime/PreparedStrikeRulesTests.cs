using System;
using System.Collections.Generic;
using System.Linq;
using Game.Rules.Runtime;
using NUnit.Framework;

namespace Game.Tests.EditMode.RulesRuntime
{
    public sealed class PreparedStrikeRulesTests
    {
        private static readonly CreatureId Actor = new("actor");
        private static readonly CreatureId Target = new("target");
        private static readonly PreparedPredicate Always = PreparedPredicate.All(
            Array.Empty<PreparedPredicate>()
        );

        [Test]
        public void AdjustmentsAreStableAcrossRepeatedAndChangingContexts()
        {
            var baseModifier = new PreparedStrikeModifier("strike-damage", "bonus", 5, "", Always);
            var definition = Definition(
                new[] { baseModifier },
                new[]
                {
                    new PreparedStrikeAdjustment(
                        "strike-damage",
                        "bonus",
                        "multiply",
                        .5f,
                        20,
                        PreparedPredicate.Option("item:trait:agile")
                    ),
                    new PreparedStrikeAdjustment(
                        "strike-damage",
                        "bonus",
                        "upgrade",
                        9,
                        10,
                        PreparedPredicate.Option("target:condition:off-guard")
                    ),
                }
            );
            var snapshot = Snapshot();
            for (int i = 0; i < 3; i++)
            {
                Assert.That(
                    Evaluate(definition, Item(true), snapshot).FlatDamage.Single().Amount,
                    Is.EqualTo(2)
                );
                Assert.That(
                    Evaluate(definition, Item(false), snapshot).FlatDamage.Single().Amount,
                    Is.EqualTo(5)
                );
                Assert.That(
                    Evaluate(definition, Item(true), snapshot, true).FlatDamage.Single().Amount,
                    Is.EqualTo(4)
                );
                Assert.That(baseModifier.Value, Is.EqualTo(5));
            }
        }

        [Test]
        public void LastMatchingSlugWinsAndAdjustmentPriorityIsStable()
        {
            var definition = Definition(
                new[]
                {
                    new PreparedStrikeModifier("strike-damage", "bonus", 1, "", Always),
                    new PreparedStrikeModifier(
                        "strike-damage",
                        "BONUS",
                        7,
                        "",
                        PreparedPredicate.Option("item:trait:agile")
                    ),
                },
                new[]
                {
                    new PreparedStrikeAdjustment(
                        "strike-damage",
                        "bonus",
                        "multiply",
                        .5f,
                        1,
                        Always
                    ),
                    new PreparedStrikeAdjustment(
                        "strike-damage",
                        "bonus",
                        "upgrade",
                        2.5f,
                        1,
                        Always
                    ),
                }
            );
            Assert.That(
                Evaluate(definition, Item(true), Snapshot()).FlatDamage.Single().Amount,
                Is.EqualTo(3)
            );
            Assert.That(
                Evaluate(definition, Item(false), Snapshot()).FlatDamage.Single().Amount,
                Is.EqualTo(2)
            );
        }

        [Test]
        public void AbilitySubstitutionAndConditionalDiceUseCurrentAuthoritativeTarget()
        {
            var definition = Definition(
                new[]
                {
                    new PreparedStrikeModifier(
                        "melee-strike-damage",
                        "ability",
                        0,
                        "dex",
                        PreparedPredicate.Option("item:trait:agile")
                    ),
                },
                Array.Empty<PreparedStrikeAdjustment>(),
                new[]
                {
                    new PreparedStrikeDice(
                        "strike-damage",
                        "precision",
                        1,
                        6,
                        PreparedPredicate.All(
                            new[]
                            {
                                PreparedPredicate.Option("item:tag:eligible"),
                                PreparedPredicate.Option("target:condition:off-guard"),
                            }
                        )
                    ),
                },
                new[]
                {
                    new PreparedStrikeAlteration(
                        "weapon",
                        "other-tags",
                        "add",
                        "eligible",
                        PreparedPredicate.Option("item:trait:agile")
                    ),
                }
            );
            var active = Evaluate(definition, Item(true), Snapshot("Off-Guard"));
            Assert.That(
                active.FlatDamage.Single().Amount,
                Is.EqualTo(3),
                "Dexterity 4 replaces base Strength 1."
            );
            Assert.That(active.DamageDice.Single().Dice, Is.EqualTo(new DiceExpression(1, 6)));
            Assert.That(Evaluate(definition, Item(true), Snapshot()).DamageDice, Is.Empty);
            Assert.That(
                Evaluate(definition, Item(false), Snapshot("Off-Guard")).DamageDice,
                Is.Empty
            );
            Assert.That(
                Evaluate(definition, Item(true, true), Snapshot("Off-Guard")).FlatDamage,
                Is.Empty
            );
            Assert.That(
                Evaluate(definition, Item(true), Snapshot("Flat-Footed")).DamageDice,
                Has.Count.EqualTo(1)
            );
        }

        [Test]
        public void DefinitionCopiesCollectionsAndIgnoresStaleConditionOptions()
        {
            List<string> options = new() { "target:condition:off-guard", "self:effect:rage" };
            Dictionary<string, int> abilities = new() { ["dex"] = 4 };
            List<PreparedStrikeModifier> modifiers = new()
            {
                new PreparedStrikeModifier(
                    "strike-damage",
                    "stale",
                    99,
                    "",
                    PreparedPredicate.Any(
                        new[]
                        {
                            PreparedPredicate.Option("target:condition:off-guard"),
                            PreparedPredicate.Option("self:effect:rage"),
                        }
                    )
                ),
                new PreparedStrikeModifier("melee-strike-damage", "ability", 0, "dex", Always),
            };
            var definition = new PreparedStrikeDefinition(
                options,
                abilities,
                modifiers,
                Array.Empty<PreparedStrikeAdjustment>(),
                Array.Empty<PreparedStrikeDice>(),
                Array.Empty<PreparedStrikeAlteration>()
            );
            options.Clear();
            abilities["dex"] = 99;
            modifiers.Clear();
            var result = Evaluate(definition, Item(false), Snapshot());
            Assert.That(result.FlatDamage.Single().Amount, Is.EqualTo(3));
        }

        [TestCase(0, 0)]
        [TestCase(60, 0)]
        [TestCase(61, -2)]
        [TestCase(120, -2)]
        [TestCase(121, -4)]
        [TestCase(360, -10)]
        public void RangePenaltyUsesInclusiveIncrementBoundaries(int distance, int penalty)
        {
            Assert.That(StrikeTargetingRules.RangePenalty(distance, 60), Is.EqualTo(penalty));
            Assert.That(StrikeTargetingRules.IsWithinRange(distance, true, 5, 60), Is.True);
        }

        [Test]
        public void RangeLimitsCoverAllPreviewModes()
        {
            Assert.That(StrikeTargetingRules.MaximumRangeFeet(true, 5, 60), Is.EqualTo(360));
            Assert.That(StrikeTargetingRules.IsWithinRange(361, true, 5, 60), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                StrikeTargetingRules.RangePenalty(361, 60)
            );
            Assert.That(StrikeTargetingRules.IsWithinRange(5, true, 5, 0), Is.False);
            Assert.That(StrikeTargetingRules.IsWithinRange(30, true, 5, 60, 30), Is.True);
            Assert.That(StrikeTargetingRules.IsWithinRange(31, true, 5, 60, 30), Is.False);
            Assert.That(StrikeTargetingRules.IsWithinRange(10, false, 10, 0), Is.True);
            Assert.That(StrikeTargetingRules.IsWithinRange(11, false, 10, 0), Is.False);
        }

        [TestCase(StrikeCover.None, 0)]
        [TestCase(StrikeCover.Lesser, 1)]
        [TestCase(StrikeCover.Standard, 2)]
        [TestCase(StrikeCover.Greater, 4)]
        public void CoverProducesTypedArmorClassCandidates(StrikeCover cover, int bonus)
        {
            Assert.That(StrikeTargetingRules.CoverArmorClassBonus(cover), Is.EqualTo(bonus));
            var candidates = StrikeTargetingRules.ArmorClassModifiers(
                StrikeTargetingOutcome.Legal(5, 0, bonus, true)
            );
            Assert.That(candidates.All(value => value.Type == ModifierType.Circumstance), Is.True);
            Assert.That(candidates.Sum(value => value.Value), Is.EqualTo(bonus - 2));
        }

        [TestCase(true, 0, StrikeCover.None)]
        [TestCase(true, 1, StrikeCover.Standard)]
        [TestCase(true, 15, StrikeCover.Standard)]
        [TestCase(true, 16, StrikeCover.None)]
        [TestCase(false, 8, StrikeCover.None)]
        public void CoverInterpretsSceneSamples(bool ranged, int clearRays, StrikeCover cover) =>
            Assert.That(StrikeTargetingRules.CoverFromRays(ranged, clearRays), Is.EqualTo(cover));

        [Test]
        public void ImportedStrikeProfilesKeepUnarmedReachThrownAndReloadPolicy()
        {
            var unarmed = PreparedStrikeRules.CreateUnarmed(
                new ItemId("unarmed"),
                new[] { "zombie-fist" },
                5,
                3
            );
            Assert.That(unarmed.DamageDice.Single().Dice.Sides, Is.EqualTo(6));
            Assert.That(
                PreparedStrikeRules
                    .CreateUnarmed(new ItemId("fist"), Array.Empty<string>(), 5, 3)
                    .DamageDice.Single()
                    .Dice.Sides,
                Is.EqualTo(3)
            );
            var damage = new TypedDamageDice(new DiceExpression(1, 6), "piercing", "weapon");
            StrikeItemDefinition Weapon(int range, StrikeAmmunitionRequirement ammo) =>
                PreparedStrikeRules.CreateWeapon(
                    new ItemId("weapon"),
                    new ItemDefinitionId("weapon"),
                    "Weapon",
                    "",
                    "martial",
                    new[] { Trait.FromSlug("reach") },
                    5,
                    damage,
                    3,
                    range,
                    0,
                    ammo
                );
            Assert.That(Weapon(0, StrikeAmmunitionRequirement.None).ReachFeet, Is.EqualTo(10));
            Assert.That(
                Weapon(20, StrikeAmmunitionRequirement.None).FlatDamage.Single().Amount,
                Is.EqualTo(3)
            );
            Assert.That(
                Weapon(60, StrikeAmmunitionRequirement.Required(new ItemId("arrows"))).FlatDamage,
                Is.Empty
            );
            Assert.That(
                PreparedStrikeRules.ReloadActions("1", new[] { "reload-2" }),
                Is.EqualTo(1)
            );
            Assert.That(
                PreparedStrikeRules.ReloadActions("-", new[] { "reload-2" }),
                Is.EqualTo(2)
            );
            Assert.That(PreparedStrikeRules.ReloadActions("-1", Array.Empty<string>()), Is.Zero);
        }

        [Test]
        public void InitiativeUsesCapturedPerceptionAndOneTypedStackingPass()
        {
            var source = RuleSource.FromSlug("fixture");
            var statistics = new CreatureStatisticsState(
                Actor,
                0,
                10,
                0,
                0,
                0,
                new Dictionary<Skill, int> { [Skill.FromName("perception")] = 7 },
                new[]
                {
                    new Modifier(2, ModifierType.Status, source, Statistic.Initiative),
                    new Modifier(99, ModifierType.Status, source, Statistic.AttackRoll),
                }
            );
            var effect = new[]
            {
                new Modifier(
                    1,
                    ModifierType.Status,
                    RuleSource.FromSlug("guidance"),
                    Statistic.Initiative
                ),
            };
            Assert.That(InitiativeRules.ResolveModifier(3, statistics, effect), Is.EqualTo(9));
            Assert.That(InitiativeRules.ResolveModifier(10, statistics, effect), Is.EqualTo(12));
        }

        [Test]
        public void SpellAreaPolicyUsesLivingFriendlyMembershipAndDirection()
        {
            CreatureId ally = new("ally");
            CreatureId defeated = new("defeated");
            RulesStateSeed seed = new();
            seed.SeedCreature(new CreatureState(Actor, new PlayerId("heroes")))
                .SeedHealth(Actor, new HealthState(10, 10));
            seed.SeedCreature(new CreatureState(ally, new PlayerId("heroes")))
                .SeedHealth(ally, new HealthState(10, 10));
            seed.SeedCreature(new CreatureState(defeated, new PlayerId("heroes")))
                .SeedHealth(defeated, new HealthState(0, 10));
            seed.SeedCreature(new CreatureState(Target, new PlayerId("enemies")))
                .SeedHealth(Target, new HealthState(10, 10));
            var snapshot = new InMemoryRulesStore(seed).Snapshot;
            var profile = new SpellSelectionProfile(
                SpellSelectionKind.Emanation,
                areaFeet: 15,
                includeCaster: true,
                friendlyOnly: true
            );
            var reachable = new[] { ally, defeated, Target };
            Assert.That(
                SpellTargetingRules.ValidateSelection(
                    snapshot,
                    Actor,
                    profile,
                    new SpellCastSelection(new[] { Actor, ally }),
                    reachable,
                    SamePlayerCombatantFriendshipProvider.Instance
                ),
                Is.SameAs(ActionValidationResult.Valid)
            );
            Assert.That(
                SpellTargetingRules.ValidateSelection(
                    snapshot,
                    Actor,
                    profile,
                    new SpellCastSelection(new[] { Actor, ally, Target }),
                    reachable,
                    SamePlayerCombatantFriendshipProvider.Instance
                ),
                Is.TypeOf<ActionValidationResult.InvalidActionValidationResult>()
            );
            var cone = new SpellSelectionProfile(SpellSelectionKind.Cone, areaFeet: 15);
            Assert.That(
                SpellTargetingRules.ValidateSelection(
                    snapshot,
                    Actor,
                    cone,
                    new SpellCastSelection(new[] { ally, Target }),
                    reachable,
                    SamePlayerCombatantFriendshipProvider.Instance
                ),
                Is.TypeOf<ActionValidationResult.InvalidActionValidationResult>()
            );
            Assert.That(
                SpellTargetingRules.ValidateSelection(
                    snapshot,
                    Actor,
                    cone,
                    new SpellCastSelection(new[] { ally, Target }, SpellAreaDirection.East),
                    reachable,
                    SamePlayerCombatantFriendshipProvider.Instance
                ),
                Is.SameAs(ActionValidationResult.Valid)
            );
            var single = new SpellSelectionProfile(
                SpellSelectionKind.SingleCreature,
                rangeFeet: 30,
                friendlyOnly: true
            );
            Assert.That(
                SpellTargetingRules.ValidateSelection(
                    snapshot,
                    Actor,
                    single,
                    new SpellCastSelection(new[] { Target }),
                    reachable,
                    SamePlayerCombatantFriendshipProvider.Instance
                ),
                Is.TypeOf<ActionValidationResult.InvalidActionValidationResult>()
            );
            Assert.That(
                SpellTargetingRules.ValidateSelection(
                    snapshot,
                    Actor,
                    single,
                    new SpellCastSelection(new[] { ally }),
                    Array.Empty<CreatureId>(),
                    SamePlayerCombatantFriendshipProvider.Instance
                ),
                Is.TypeOf<ActionValidationResult.InvalidActionValidationResult>()
            );
            Assert.That(
                StrikeTargetingRules.IsEnemy(
                    snapshot,
                    Actor,
                    ally,
                    SamePlayerCombatantFriendshipProvider.Instance
                ),
                Is.False
            );
            Assert.That(
                StrikeTargetingRules.IsEnemy(
                    snapshot,
                    Actor,
                    Target,
                    SamePlayerCombatantFriendshipProvider.Instance
                ),
                Is.True
            );
        }

        private static PreparedStrikeContributions Evaluate(
            PreparedStrikeDefinition definition,
            StrikeItemDefinition item,
            RulesSnapshot snapshot,
            bool offGuard = false
        ) =>
            PreparedStrikeRules.Evaluate(
                definition,
                item,
                StrikeTargetingOutcome.Legal(5, 0, 0, offGuard),
                snapshot,
                Actor,
                Target
            );

        private static PreparedStrikeDefinition Definition(
            IEnumerable<PreparedStrikeModifier> modifiers,
            IEnumerable<PreparedStrikeAdjustment> adjustments,
            IEnumerable<PreparedStrikeDice> dice = null,
            IEnumerable<PreparedStrikeAlteration> alterations = null
        ) =>
            new(
                Array.Empty<string>(),
                new Dictionary<string, int> { ["dex"] = 4 },
                modifiers,
                adjustments,
                dice ?? Array.Empty<PreparedStrikeDice>(),
                alterations ?? Array.Empty<PreparedStrikeAlteration>()
            );

        private static StrikeItemDefinition Item(bool agile, bool ranged = false) =>
            new(
                new ItemId("weapon"),
                new ItemDefinitionId("weapon"),
                "Weapon",
                "",
                "martial",
                agile ? new[] { Trait.FromSlug("agile") } : Array.Empty<Trait>(),
                7,
                new[] { new TypedDamageDice(new DiceExpression(1, 6), "slashing", "weapon") },
                new[] { new TypedFlatDamage(1, "slashing", "Strength") },
                5,
                ranged ? 60 : 0,
                0,
                StrikeAmmunitionRequirement.None
            );

        private static RulesSnapshot Snapshot(string condition = "")
        {
            RulesStateSeed seed = new();
            seed.SeedCreature(new CreatureState(Actor, new PlayerId("heroes")));
            seed.SeedCreature(new CreatureState(Target, new PlayerId("enemies")));
            if (condition.Length > 0)
            {
                var effect = new ActiveEffectInstance(
                    new ActiveEffectId("condition"),
                    ConditionRules.DefinitionId,
                    Actor,
                    RuleSource.FromSlug("fixture"),
                    EffectDuration.Indefinite,
                    new ConditionState(new ConditionId(condition), 1)
                );
                seed.SeedActiveEffect(effect);
                seed.SeedRuleBinding(
                    new ActiveRuleBinding(
                        new BindingId("condition"),
                        ConditionRules.DefinitionId,
                        Target,
                        effect.Id,
                        effect.Source,
                        0
                    )
                );
            }
            return new InMemoryRulesStore(seed).Snapshot;
        }
    }
}
