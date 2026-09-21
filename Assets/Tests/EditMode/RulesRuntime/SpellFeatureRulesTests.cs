using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class SpellFeatureRulesTests
    {
        private static readonly CreatureId Caster = new("spell-feature-caster");
        private static readonly CreatureId Ally = new("spell-feature-ally");
        private static readonly CreatureId AllyTwo = new("spell-feature-ally-two");
        private static readonly CreatureId Enemy = new("spell-feature-enemy");
        private static readonly CreatureId Undead = new("spell-feature-undead");
        private static readonly PlayerId Heroes = new("spell-feature-heroes");
        private static readonly PlayerId Enemies = new("spell-feature-enemies");
        private static readonly SpellSlotPoolId Pool = new("spell-feature-rank-1");
        private static readonly SpellSlotPoolId SecondaryPool = new(
            "spell-feature-secondary-rank-1"
        );
        private static readonly EncounterId Encounter = new("spell-feature-encounter");
        private static readonly RuleSource TestSource = RuleSource.FromSlug("spell-feature-test");

        [Test]
        public void VariableActionSpellsExposeExactSelectionContracts()
        {
            TestCatalog catalog = new(new TestCreatureData());

            Assert.That(
                catalog
                    .GetRule("infuse-vitality")
                    .GetSelection(new SpellActionVariant(1))
                    .ExactCreatureCount,
                Is.EqualTo(1)
            );
            Assert.That(
                catalog
                    .GetRule("infuse-vitality")
                    .GetSelection(new SpellActionVariant(3))
                    .ExactCreatureCount,
                Is.EqualTo(3)
            );
            Assert.That(
                catalog.GetRule("heal").GetSelection(new SpellActionVariant(1)).RangeFeet,
                Is.EqualTo(5)
            );
            Assert.That(
                catalog.GetRule("heal").GetSelection(new SpellActionVariant(2)).RangeFeet,
                Is.EqualTo(30)
            );
            Assert.That(
                catalog.GetRule("heal").GetSelection(new SpellActionVariant(3)).Kind,
                Is.EqualTo(SpellSelectionKind.Emanation)
            );
        }

        [Test]
        public async Task InvalidInfuseTargetCountRejectsBeforeActionsOrSlot()
        {
            TestRuntime runtime = CreateRuntime(new ScriptedRollService());

            OpResult<CastSpellOutcome> result = await runtime.Dispatcher.Dispatch(
                Cast("infuse-vitality", 2, Ally)
            );

            Assert.That(result, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(
                runtime.Store.Snapshot.ActionEconomy[Caster].ActionsRemaining,
                Is.EqualTo(3)
            );
            Assert.That(runtime.Store.Snapshot.SpellSlots[Pool].Remaining, Is.EqualTo(1));
            Assert.That(runtime.Store.Snapshot.ActiveEffects, Is.Empty);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public async Task InfuseVitalityTargetsExactlyOneCreaturePerAction(int actions)
        {
            TestRuntime runtime = CreateRuntime(new ScriptedRollService());
            CreatureId[] availableTargets = { Caster, Ally, AllyTwo };

            CastSpellOutcome cast = RequireResolved(
                await runtime.Dispatcher.Dispatch(
                    Cast("infuse-vitality", actions, availableTargets.Take(actions).ToArray())
                )
            ).Value;

            Assert.That(cast.CreatedEffects, Has.Count.EqualTo(actions));
            Assert.That(
                runtime.Store.Snapshot.ActionEconomy[Caster].ActionsRemaining,
                Is.EqualTo(3 - actions)
            );
            Assert.That(runtime.Store.Snapshot.SpellSlots[Pool].Remaining, Is.Zero);
        }

        [Test]
        public async Task ShieldCreatesRoundEffectAndRaisesArmorClass()
        {
            TestRuntime runtime = CreateRuntime(new ScriptedRollService());

            CastSpellOutcome cast = RequireResolved(
                await runtime.Dispatcher.Dispatch(Cast("shield", 1))
            ).Value;
            int armorClass = RequireResolved(
                await runtime.Dispatcher.Dispatch(new ProbeArmorClassOp(Caster, 20))
            ).Value;
            int armorClassWithStrongerCircumstanceBonus = RequireResolved(
                await runtime.Dispatcher.Dispatch(
                    new ProbeArmorClassOp(
                        Caster,
                        20,
                        new Modifier(
                            2,
                            ModifierType.Circumstance,
                            RuleSource.FromSlug("raised-shield"),
                            Statistic.ArmorClass
                        )
                    )
                )
            ).Value;

            ActiveEffectInstance effect = runtime.Store.Snapshot.ActiveEffects[
                cast.CreatedEffects.Single()
            ];
            Assert.That(effect.DefinitionId, Is.EqualTo(SpellFeatureRules.ShieldEffect));
            Assert.That(effect.Duration, Is.EqualTo(EffectDuration.Rounds(1)));
            Assert.That(effect.SourceCreature, Is.EqualTo(Caster));
            Assert.That(effect.GetState<SpellEffectState>().Target, Is.EqualTo(Caster));
            Assert.That(armorClass, Is.EqualTo(21));
            Assert.That(armorClassWithStrongerCircumstanceBonus, Is.EqualTo(22));
            Assert.That(
                runtime.Store.Snapshot.ActionEconomy[Caster].ActionsRemaining,
                Is.EqualTo(2)
            );
        }

        [Test]
        public async Task GuidanceConsumesOnEligibleCheckAndCreatesPersistentImmunity()
        {
            TestRuntime runtime = CreateRuntime(new ScriptedRollService());
            RequireResolved(await runtime.Dispatcher.Dispatch(Cast("guidance", 1, Ally)));

            ModifierCollection modifiers = RequireResolved(
                await runtime.Dispatcher.Dispatch(new ProbeAttackModifiersOp(Ally, Enemy))
            ).Value;

            Assert.That(modifiers.Total, Is.EqualTo(1));
            Assert.That(
                runtime
                    .Store.Snapshot.ActiveEffects.Select(pair => pair.Value)
                    .Any(effect => effect.DefinitionId == SpellFeatureRules.GuidanceEffect),
                Is.False
            );
            ActiveEffectInstance immunity = runtime
                .Store.Snapshot.ActiveEffects.Select(pair => pair.Value)
                .Single(effect => effect.DefinitionId == SpellFeatureRules.GuidanceImmunity);
            Assert.That(immunity.Duration, Is.EqualTo(EffectDuration.Minutes(60)));
            Assert.That(immunity.GetState<SpellEffectState>().Target, Is.EqualTo(Ally));

            int actionsBeforeRetry = runtime.Store.Snapshot.ActionEconomy[Caster].ActionsRemaining;
            OpResult<CastSpellOutcome> retry = await runtime.Dispatcher.Dispatch(
                Cast("guidance", 1, Ally)
            );
            Assert.That(retry, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(
                runtime.Store.Snapshot.ActionEconomy[Caster].ActionsRemaining,
                Is.EqualTo(actionsBeforeRetry)
            );
        }

        [Test]
        public async Task ExternalSourceGuidanceConsumesAndUsesItsEnrolledOwnerClock()
        {
            TestRuntime runtime = CreateRuntime(
                new ScriptedRollService(),
                new TestCreatureData(),
                2,
                new TestTargetingDataProvider(ActionValidationResult.Valid),
                false
            );
            RequireResolved(await runtime.Dispatcher.Dispatch(Cast("guidance", 1, Ally)));

            ModifierCollection modifiers = RequireResolved(
                await runtime.Dispatcher.Dispatch(new ProbeAttackModifiersOp(Ally, Enemy))
            ).Value;

            Assert.That(modifiers.Total, Is.EqualTo(1));
            Assert.That(
                runtime
                    .Store.Snapshot.ActiveEffects.Select(pair => pair.Value)
                    .Any(effect => effect.DefinitionId == SpellFeatureRules.GuidanceEffect),
                Is.False
            );
            ActiveEffectInstance immunity = runtime
                .Store.Snapshot.ActiveEffects.Select(pair => pair.Value)
                .Single(effect => effect.DefinitionId == SpellFeatureRules.GuidanceImmunity);
            Assert.That(immunity.SourceCreature, Is.EqualTo(Caster));
            Assert.That(immunity.Duration, Is.EqualTo(EffectDuration.Minutes(60)));
            Assert.That(immunity.GetState<SpellEffectState>().Target, Is.EqualTo(Ally));
            Assert.That(
                runtime.Store.Snapshot.ActiveEffectTimings[immunity.Id].SourceCreature,
                Is.EqualTo(Ally)
            );
        }

        [Test]
        public async Task GuidanceRejectsRecastWhileActiveBeforeCosts()
        {
            TestRuntime runtime = CreateRuntime(new ScriptedRollService());
            CastSpellOutcome first = RequireResolved(
                await runtime.Dispatcher.Dispatch(Cast("guidance", 1, Ally))
            ).Value;
            int actionsBeforeRetry = runtime.Store.Snapshot.ActionEconomy[Caster].ActionsRemaining;

            OpResult<CastSpellOutcome> retry = await runtime.Dispatcher.Dispatch(
                Cast("guidance", 1, Ally)
            );

            Assert.That(retry, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(
                ((InvalidOpResult<CastSpellOutcome>)retry).Reason,
                Does.Contain("already has active Guidance")
            );
            Assert.That(
                runtime.Store.Snapshot.ActionEconomy[Caster].ActionsRemaining,
                Is.EqualTo(actionsBeforeRetry)
            );
            Assert.That(
                runtime
                    .Store.Snapshot.ActiveEffects.Select(pair => pair.Value)
                    .Single(effect => effect.DefinitionId == SpellFeatureRules.GuidanceEffect)
                    .Id,
                Is.EqualTo(first.CreatedEffects.Single())
            );
            Assert.That(
                runtime
                    .Store.Snapshot.ActiveEffects.Select(pair => pair.Value)
                    .Any(effect => effect.DefinitionId == SpellFeatureRules.GuidanceImmunity),
                Is.False
            );
        }

        [Test]
        public async Task GuidanceContributesToSkillAndSavingThrowCollections()
        {
            TestRuntime skillRuntime = CreateRuntime(new ScriptedRollService());
            RequireResolved(await skillRuntime.Dispatcher.Dispatch(Cast("guidance", 1, Ally)));

            ModifierCollection skill = RequireResolved(
                await skillRuntime.Dispatcher.Dispatch(new ProbeSkillModifiersOp(Ally))
            ).Value;

            TestRuntime saveRuntime = CreateRuntime(new ScriptedRollService());
            RequireResolved(await saveRuntime.Dispatcher.Dispatch(Cast("guidance", 1, Ally)));
            ModifierCollection save = RequireResolved(
                await saveRuntime.Dispatcher.Dispatch(new ProbeSaveModifiersOp(Ally))
            ).Value;

            Assert.That(skill.Total, Is.EqualTo(1));
            Assert.That(skill.Applied.Single().Type, Is.EqualTo(ModifierType.Status));
            Assert.That(save.Total, Is.EqualTo(1));
            Assert.That(save.Applied.Single().Type, Is.EqualTo(ModifierType.Status));
        }

        [Test]
        public async Task UnusedGuidanceExpiresAtZeroHpCasterInitiativeBoundaryAndCreatesImmunity()
        {
            TestRuntime runtime = CreateRuntime(new ScriptedRollService());
            RequireResolved(await runtime.Dispatcher.Dispatch(Cast("guidance", 1, Ally)));
            RequireResolved(
                await runtime.Dispatcher.Dispatch(
                    new ApplyDamageOp(
                        Caster,
                        20,
                        new HealthChangeOriginId("guidance-zero-hp-caster"),
                        TestSource
                    )
                )
            );

            await runtime.Dispatcher.Dispatch(
                new EmitInitiativeBoundaryOp(Encounter, RoundNumber.First, Caster)
            );

            Assert.That(
                runtime
                    .Store.Snapshot.ActiveEffects.Select(pair => pair.Value)
                    .Any(effect => effect.DefinitionId == SpellFeatureRules.GuidanceEffect),
                Is.False
            );
            ActiveEffectInstance immunity = runtime
                .Store.Snapshot.ActiveEffects.Select(pair => pair.Value)
                .Single(effect => effect.DefinitionId == SpellFeatureRules.GuidanceImmunity);
            Assert.That(immunity.Duration, Is.EqualTo(EffectDuration.Minutes(60)));
            Assert.That(immunity.GetState<SpellEffectState>().Target, Is.EqualTo(Ally));
        }

        [Test]
        public async Task RestoredGuidanceWithExternalSourceExpiresAtNextRoundBoundary()
        {
            TestRuntime runtime = CreateRuntime(
                new ScriptedRollService(),
                new TestCreatureData(),
                2,
                new TestTargetingDataProvider(ActionValidationResult.Valid),
                false
            );
            RequireResolved(await runtime.Dispatcher.Dispatch(Cast("guidance", 1, Ally)));

            RequireResolved(
                await runtime.Dispatcher.Dispatch(
                    new EmitInitiativeBoundaryOp(Encounter, RoundNumber.First, Ally)
                )
            );

            Assert.That(
                runtime
                    .Store.Snapshot.ActiveEffects.Select(pair => pair.Value)
                    .Any(effect => effect.DefinitionId == SpellFeatureRules.GuidanceEffect),
                Is.False
            );
            ActiveEffectInstance immunity = runtime
                .Store.Snapshot.ActiveEffects.Select(pair => pair.Value)
                .Single(effect => effect.DefinitionId == SpellFeatureRules.GuidanceImmunity);
            Assert.That(immunity.SourceCreature, Is.EqualTo(Caster));
            Assert.That(immunity.Duration, Is.EqualTo(EffectDuration.Minutes(60)));
            Assert.That(immunity.GetState<SpellEffectState>().Target, Is.EqualTo(Ally));
            Assert.That(
                runtime.Store.Snapshot.ActiveEffectTimings[immunity.Id].SourceCreature,
                Is.EqualTo(Ally)
            );
        }

        [Test]
        public async Task BlessSnapshotsAlliesAndInfuseContributesHeightenedStrikeDice()
        {
            TestRuntime bless = CreateRuntime(new ScriptedRollService());
            CastSpellOutcome blessed = RequireResolved(
                await bless.Dispatcher.Dispatch(Cast("bless", 2, Caster, Ally))
            ).Value;
            ModifierCollection attack = RequireResolved(
                await bless.Dispatcher.Dispatch(new ProbeAttackModifiersOp(Ally, Enemy))
            ).Value;

            Assert.That(blessed.CreatedEffects, Has.Count.EqualTo(2));
            Assert.That(attack.Total, Is.EqualTo(1));
            Assert.That(
                bless
                    .Store.Snapshot.ActiveEffects.Select(pair => pair.Value)
                    .All(effect => effect.Duration == EffectDuration.OneMinute),
                Is.True
            );

            TestRuntime infuse = CreateRuntime(new ScriptedRollService());
            CastSpellOutcome infused = RequireResolved(
                await infuse.Dispatcher.Dispatch(CastAtRank("infuse-vitality", 3, 2, Caster, Ally))
            ).Value;
            IReadOnlyList<TypedDamageDice> dice = RequireResolved(
                await infuse.Dispatcher.Dispatch(new ProbeStrikeDamageOp(Ally, Undead))
            ).Value;
            IReadOnlyList<TypedDamageDice> livingTargetDice = RequireResolved(
                await infuse.Dispatcher.Dispatch(new ProbeStrikeDamageOp(Ally, Enemy))
            ).Value;

            Assert.That(infused.CreatedEffects, Has.Count.EqualTo(2));
            Assert.That(dice.Count, Is.EqualTo(1));
            Assert.That(dice[0].Dice, Is.EqualTo(new DiceExpression(2, 4)));
            Assert.That(dice[0].DamageType, Is.EqualTo("vitality"));
            Assert.That(livingTargetDice, Is.Empty);
            Assert.That(infuse.Store.Snapshot.SpellSlots[Pool].Remaining, Is.Zero);
            Assert.That(
                infuse.Store.Snapshot.ActionEconomy[Caster].ActionsRemaining,
                Is.EqualTo(1)
            );
        }

        [Test]
        public async Task InfuseVitalityFixedHeighteningCapsAtThreeDiceAboveRankFive()
        {
            TestRuntime runtime = CreateRuntime(new ScriptedRollService());
            RequireResolved(
                await runtime.Dispatcher.Dispatch(CastAtRank("infuse-vitality", 9, 1, Ally))
            );

            IReadOnlyList<TypedDamageDice> dice = RequireResolved(
                await runtime.Dispatcher.Dispatch(new ProbeStrikeDamageOp(Ally, Undead))
            ).Value;

            Assert.That(dice.Single().Dice, Is.EqualTo(new DiceExpression(3, 4)));
        }

        [Test]
        public async Task InfuseVitalityNewStrongerRecastWinsWithoutStacking()
        {
            TestRuntime runtime = CreateMultiCastRuntime(new ScriptedRollService());
            CastSpellOutcome weaker = RequireResolved(
                await runtime.Dispatcher.Dispatch(CastAtRank("infuse-vitality", 1, 1, Ally))
            ).Value;
            CastSpellOutcome stronger = RequireResolved(
                await runtime.Dispatcher.Dispatch(CastAtRank("infuse-vitality", 5, 1, Ally))
            ).Value;

            IReadOnlyList<TypedDamageDice> dice = RequireResolved(
                await runtime.Dispatcher.Dispatch(new ProbeStrikeDamageOp(Ally, Undead))
            ).Value;
            ActiveRuleBinding weakerBinding = BindingFor(
                runtime.Store.Snapshot,
                weaker.CreatedEffects.Single()
            );
            ActiveRuleBinding strongerBinding = BindingFor(
                runtime.Store.Snapshot,
                stronger.CreatedEffects.Single()
            );

            Assert.That(runtime.Store.Snapshot.ActiveEffects.Count(), Is.EqualTo(2));
            Assert.That(dice.Count, Is.EqualTo(1));
            Assert.That(dice.Single().Dice, Is.EqualTo(new DiceExpression(3, 4)));
            Assert.That(
                SpellFeatureRules.IsApplicableInfuseVitalityBinding(
                    runtime.Store.Snapshot,
                    weakerBinding
                ),
                Is.False
            );
            Assert.That(
                SpellFeatureRules.IsApplicableInfuseVitalityBinding(
                    runtime.Store.Snapshot,
                    strongerBinding
                ),
                Is.True
            );
        }

        [Test]
        public async Task InfuseVitalityOlderStrongerRecastWinsUntilItsOwnExpiration()
        {
            TestRuntime runtime = CreateMultiCastRuntime(new ScriptedRollService());
            CastSpellOutcome stronger = RequireResolved(
                await runtime.Dispatcher.Dispatch(CastAtRank("infuse-vitality", 5, 1, Ally))
            ).Value;
            CastSpellOutcome weaker = RequireResolved(
                await runtime.Dispatcher.Dispatch(CastAtRank("infuse-vitality", 1, 1, Ally))
            ).Value;

            IReadOnlyList<TypedDamageDice> beforeExpiration = RequireResolved(
                await runtime.Dispatcher.Dispatch(new ProbeStrikeDamageOp(Ally, Undead))
            ).Value;
            ActiveEffectId strongerEffect = stronger.CreatedEffects.Single();
            ActiveRuleBinding strongerBinding = BindingFor(runtime.Store.Snapshot, strongerEffect);
            await ExpireEffect(runtime, strongerEffect, strongerBinding);
            IReadOnlyList<TypedDamageDice> afterExpiration = RequireResolved(
                await runtime.Dispatcher.Dispatch(new ProbeStrikeDamageOp(Ally, Undead))
            ).Value;

            Assert.That(beforeExpiration.Count, Is.EqualTo(1));
            Assert.That(beforeExpiration.Single().Dice, Is.EqualTo(new DiceExpression(3, 4)));
            Assert.That(runtime.Store.Snapshot.ActiveEffects.Contains(strongerEffect), Is.False);
            Assert.That(
                runtime.Store.Snapshot.ActiveEffects.Contains(weaker.CreatedEffects.Single()),
                Is.True
            );
            Assert.That(afterExpiration.Count, Is.EqualTo(1));
            Assert.That(afterExpiration.Single().Dice, Is.EqualTo(new DiceExpression(1, 4)));
        }

        [Test]
        public async Task InfuseVitalityEqualRankUsesNewestSourceAndKeepsIndependentDuration()
        {
            TestRuntime runtime = CreateMultiCastRuntime(
                new ScriptedRollService(),
                includeSecondaryCaster: true
            );
            CastSpellOutcome first = RequireResolved(
                await runtime.Dispatcher.Dispatch(CastAtRank("infuse-vitality", 3, 1, Ally))
            ).Value;
            CastSpellOutcome second = RequireResolved(
                await runtime.Dispatcher.Dispatch(
                    CastAtRank(AllyTwo, "infuse-vitality", 3, 1, Ally)
                )
            ).Value;
            ActiveRuleBinding firstBinding = BindingFor(
                runtime.Store.Snapshot,
                first.CreatedEffects.Single()
            );
            ActiveRuleBinding secondBinding = BindingFor(
                runtime.Store.Snapshot,
                second.CreatedEffects.Single()
            );

            Assert.That(
                runtime.Store.Snapshot.ActiveEffects[first.CreatedEffects.Single()].SourceCreature,
                Is.EqualTo(Caster)
            );
            Assert.That(
                runtime.Store.Snapshot.ActiveEffects[second.CreatedEffects.Single()].SourceCreature,
                Is.EqualTo(AllyTwo)
            );
            Assert.That(
                SpellFeatureRules.IsApplicableInfuseVitalityBinding(
                    runtime.Store.Snapshot,
                    firstBinding
                ),
                Is.False
            );
            Assert.That(
                SpellFeatureRules.IsApplicableInfuseVitalityBinding(
                    runtime.Store.Snapshot,
                    secondBinding
                ),
                Is.True
            );

            Assert.That(
                runtime
                    .Store
                    .Snapshot
                    .ActiveEffectTimings[first.CreatedEffects.Single()]
                    .SourceCreature,
                Is.EqualTo(Caster)
            );
            Assert.That(
                runtime
                    .Store
                    .Snapshot
                    .ActiveEffectTimings[second.CreatedEffects.Single()]
                    .SourceCreature,
                Is.EqualTo(AllyTwo)
            );
            await ExpireEffect(runtime, second.CreatedEffects.Single(), secondBinding);

            Assert.That(
                runtime.Store.Snapshot.ActiveEffects.Contains(second.CreatedEffects.Single()),
                Is.False
            );
            Assert.That(
                runtime.Store.Snapshot.ActiveEffects.Contains(first.CreatedEffects.Single()),
                Is.True
            );
            Assert.That(
                SpellFeatureRules.IsApplicableInfuseVitalityBinding(
                    runtime.Store.Snapshot,
                    firstBinding
                ),
                Is.True
            );
        }

        [TestCase(20, 4, DegreeOfSuccess.CriticalSuccess, 0, false)]
        [TestCase(15, 4, DegreeOfSuccess.Success, 2, false)]
        [TestCase(14, 4, DegreeOfSuccess.Failure, 4, false)]
        [TestCase(1, 4, DegreeOfSuccess.CriticalFailure, 8, true)]
        public async Task HauntingHymnAppliesEveryBasicFortitudeDegree(
            int saveRoll,
            int damageRoll,
            DegreeOfSuccess expectedDegree,
            int expectedDamage,
            bool expectedDeafened
        )
        {
            TestRuntime runtime = CreateRuntime(new ScriptedRollService(saveRoll, damageRoll));

            CastSpellOutcome cast = RequireResolved(
                await runtime.Dispatcher.Dispatch(Cast("haunting-hymn", 2, Enemy))
            ).Value;

            SpellTargetResolution target = cast.TargetResolutions.Single();
            Assert.That(target.Degree, Is.EqualTo(expectedDegree));
            Assert.That(target.Damage.Single().DamageType, Is.EqualTo("sonic"));
            Assert.That(target.Damage.Single().Amount, Is.EqualTo(expectedDamage));
            Assert.That(target.ConditionApplied, Is.EqualTo(expectedDeafened));
            Assert.That(
                runtime.Store.Snapshot.Health[Enemy].Current,
                Is.EqualTo(20 - expectedDamage)
            );
            Assert.That(
                ConditionRules.GetValue(runtime.Store.Snapshot, Enemy, new ConditionId("Deafened")),
                Is.EqualTo(expectedDeafened ? 1 : 0)
            );
        }

        [Test]
        public async Task HauntingHymnAppliesSonicWeaknessAndResistanceAfterBasicSave()
        {
            TestRuntime runtime = CreateRuntime(
                new ScriptedRollService(14, 4),
                new TestCreatureData(
                    new[] { new TypedDefenseAdjustment("sonic", 2) },
                    new[] { new TypedDefenseAdjustment("sonic", 1) }
                )
            );

            CastSpellOutcome cast = RequireResolved(
                await runtime.Dispatcher.Dispatch(Cast("haunting-hymn", 2, Enemy))
            ).Value;

            Assert.That(cast.TargetResolutions.Single().Damage.Single().Amount, Is.EqualTo(5));
            Assert.That(runtime.Store.Snapshot.Health[Enemy].Current, Is.EqualTo(15));
        }

        [Test]
        public async Task HauntingHymnRollsDamageOnceAndAppliesItThroughEachBasicSave()
        {
            ScriptedRollService rolls = new(14, 15, 6);
            TestRuntime runtime = CreateRuntime(rolls);

            CastSpellOutcome cast = RequireResolved(
                await runtime.Dispatcher.Dispatch(Cast("haunting-hymn", 2, Enemy, Undead))
            ).Value;

            Assert.That(
                cast.TargetResolutions.Single(result => result.Target == Enemy)
                    .Damage.Single()
                    .Amount,
                Is.EqualTo(6)
            );
            Assert.That(
                cast.TargetResolutions.Single(result => result.Target == Undead)
                    .Damage.Single()
                    .Amount,
                Is.EqualTo(3)
            );
            Assert.That(rolls.Remaining, Is.Zero);
        }

        [Test]
        public async Task HeightenedHauntingHymnAndHealScaleTheirDiceAndFlatHealing()
        {
            TestRuntime hymn = CreateRuntime(new ScriptedRollService(14, 3, 4));

            CastSpellOutcome hymnCast = RequireResolved(
                await hymn.Dispatcher.Dispatch(CastAtRank("haunting-hymn", 3, 2, Enemy))
            ).Value;

            Assert.That(hymnCast.TargetResolutions.Single().Damage.Single().Amount, Is.EqualTo(7));
            Assert.That(hymn.Store.Snapshot.Health[Enemy].Current, Is.EqualTo(13));

            TestRuntime heal = CreateRuntime(new ScriptedRollService(3, 4));
            CastSpellOutcome healCast = RequireResolved(
                await heal.Dispatcher.Dispatch(CastAtRank("heal", 2, 2, Ally))
            ).Value;

            Assert.That(healCast.TargetResolutions.Single().Healing, Is.EqualTo(23));
            Assert.That(heal.Store.Snapshot.Health[Ally].Current, Is.EqualTo(28));
        }

        [Test]
        public async Task HealTwoActionRestoresLivingAndOneActionDamagesUndead()
        {
            TestRuntime living = CreateRuntime(new ScriptedRollService(5));
            CastSpellOutcome healed = RequireResolved(
                await living.Dispatcher.Dispatch(Cast("heal", 2, Ally))
            ).Value;

            Assert.That(healed.TargetResolutions.Single().Healing, Is.EqualTo(13));
            Assert.That(living.Store.Snapshot.Health[Ally].Current, Is.EqualTo(18));
            Assert.That(living.Store.Snapshot.SpellSlots[Pool].Remaining, Is.Zero);
            Assert.That(
                living.Store.Snapshot.ActionEconomy[Caster].ActionsRemaining,
                Is.EqualTo(1)
            );

            TestRuntime undead = CreateRuntime(new ScriptedRollService(10, 4));
            CastSpellOutcome damaged = RequireResolved(
                await undead.Dispatcher.Dispatch(Cast("heal", 1, Undead))
            ).Value;

            Assert.That(
                damaged.TargetResolutions.Single().Degree,
                Is.EqualTo(DegreeOfSuccess.Failure)
            );
            Assert.That(damaged.TargetResolutions.Single().Damage.Single().Amount, Is.EqualTo(4));
            Assert.That(undead.Store.Snapshot.Health[Undead].Current, Is.EqualTo(16));
            Assert.That(
                undead.Store.Snapshot.ActionEconomy[Caster].ActionsRemaining,
                Is.EqualTo(2)
            );
        }

        [Test]
        public async Task HealThreeActionEmanationHealsAllLivingAndDamagesUndead()
        {
            ScriptedRollService rolls = new(10, 5);
            TestRuntime runtime = CreateRuntime(rolls);
            RequireResolved(
                await runtime.Dispatcher.Dispatch(
                    new ApplyDamageOp(
                        Enemy,
                        5,
                        new HealthChangeOriginId("heal-area-living-enemy"),
                        TestSource
                    )
                )
            );

            CastSpellOutcome cast = RequireResolved(
                await runtime.Dispatcher.Dispatch(Cast("heal", 3, Caster, Ally, Enemy, Undead))
            ).Value;

            Assert.That(cast.TargetResolutions, Has.Count.EqualTo(4));
            Assert.That(
                cast.TargetResolutions.Single(value => value.Target == Caster).Healing,
                Is.Zero
            );
            Assert.That(
                cast.TargetResolutions.Single(value => value.Target == Ally).Healing,
                Is.EqualTo(5)
            );
            Assert.That(runtime.Store.Snapshot.Health[Ally].Current, Is.EqualTo(10));
            Assert.That(
                cast.TargetResolutions.Single(value => value.Target == Enemy).Healing,
                Is.EqualTo(5)
            );
            Assert.That(runtime.Store.Snapshot.Health[Enemy].Current, Is.EqualTo(20));
            Assert.That(
                cast.TargetResolutions.Single(value => value.Target == Undead)
                    .Damage.Single()
                    .Amount,
                Is.EqualTo(5)
            );
            Assert.That(runtime.Store.Snapshot.Health[Undead].Current, Is.EqualTo(15));
            Assert.That(runtime.Store.Snapshot.ActionEconomy[Caster].ActionsRemaining, Is.Zero);
            Assert.That(runtime.Store.Snapshot.SpellSlots[Pool].Remaining, Is.Zero);
            Assert.That(rolls.Remaining, Is.Zero);
        }

        [Test]
        public async Task OutOfRangeAndUnwillingLivingTargetsRejectAtomically()
        {
            TestRuntime runtime = CreateRuntime(new ScriptedRollService(), enemyX: 7);

            OpResult<CastSpellOutcome> guidance = await runtime.Dispatcher.Dispatch(
                Cast("guidance", 1, Enemy)
            );
            OpResult<CastSpellOutcome> heal = await runtime.Dispatcher.Dispatch(
                Cast("heal", 2, Enemy)
            );

            Assert.That(guidance, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(heal, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(
                runtime.Store.Snapshot.ActionEconomy[Caster].ActionsRemaining,
                Is.EqualTo(3)
            );
            Assert.That(runtime.Store.Snapshot.SpellSlots[Pool].Remaining, Is.EqualTo(1));
        }

        [Test]
        public async Task AuthoritativeTargetingRejectsBeforeActionsOrSlotSpend()
        {
            TestRuntime runtime = CreateRuntime(
                new ScriptedRollService(),
                new TestCreatureData(),
                2,
                new TestTargetingDataProvider(
                    ActionValidationResult.Invalid(
                        "The live area no longer contains the submitted targets."
                    )
                ),
                true
            );

            OpResult<CastSpellOutcome> result = await runtime.Dispatcher.Dispatch(
                Cast("bless", 2, Caster, Ally)
            );

            Assert.That(result, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(
                runtime.Store.Snapshot.ActionEconomy[Caster].ActionsRemaining,
                Is.EqualTo(3)
            );
            Assert.That(runtime.Store.Snapshot.SpellSlots[Pool].Remaining, Is.EqualTo(1));
            Assert.That(runtime.Store.Snapshot.ActiveEffects, Is.Empty);
        }

        private static CastSpellActionOp Cast(
            string slug,
            int actions,
            params CreatureId[] targets
        ) => CastAtRank(slug, 1, actions, targets);

        private static CastSpellActionOp CastAtRank(
            string slug,
            int rank,
            int actions,
            params CreatureId[] targets
        ) => CastAtRank(Caster, slug, rank, actions, targets);

        private static CastSpellActionOp CastAtRank(
            CreatureId caster,
            string slug,
            int rank,
            int actions,
            params CreatureId[] targets
        ) =>
            new(
                caster,
                new SpellReference(new SpellId(slug), rank),
                new SpellActionVariant(actions),
                targets.Length == 0 ? SpellCastSelection.Empty : new SpellCastSelection(targets)
            );

        private static TestRuntime CreateMultiCastRuntime(
            IRollService rolls,
            bool includeSecondaryCaster = false
        ) =>
            CreateRuntime(
                rolls,
                new TestCreatureData(),
                2,
                new TestTargetingDataProvider(ActionValidationResult.Valid),
                true,
                2,
                includeSecondaryCaster
            );

        private static TestRuntime CreateRuntime(IRollService rolls, int enemyX = 2)
        {
            return CreateRuntime(
                rolls,
                new TestCreatureData(),
                enemyX,
                new TestTargetingDataProvider(ActionValidationResult.Valid),
                true
            );
        }

        private static TestRuntime CreateRuntime(
            IRollService rolls,
            TestCreatureData creatureData,
            int enemyX = 2
        ) =>
            CreateRuntime(
                rolls,
                creatureData,
                enemyX,
                new TestTargetingDataProvider(ActionValidationResult.Valid),
                true
            );

        private static TestRuntime CreateRuntime(
            IRollService rolls,
            TestCreatureData creatureData,
            int enemyX,
            ISpellTargetingDataProvider targetingData,
            bool includeCasterInEncounter,
            int rankedSlotUses = 1,
            bool includeSecondaryCaster = false
        )
        {
            CreatureId initiativeCreature = includeCasterInEncounter ? Caster : Ally;
            List<InitiativeEntry> initiative = new()
            {
                new InitiativeEntry(initiativeCreature, Heroes, 10, 0, 0, RoundNumber.First),
            };
            if (includeSecondaryCaster)
            {
                initiative.Add(new InitiativeEntry(AllyTwo, Heroes, 9, 1, 0, RoundNumber.First));
            }
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Caster, Heroes))
                .SeedCreature(new CreatureState(Ally, Heroes))
                .SeedCreature(new CreatureState(AllyTwo, Heroes))
                .SeedCreature(new CreatureState(Enemy, Enemies))
                .SeedCreature(
                    new CreatureState(Undead, Enemies, new[] { Trait.FromSlug("undead") })
                )
                .SeedHealth(Caster, new HealthState(20, 20))
                .SeedHealth(Ally, new HealthState(5, 50))
                .SeedHealth(AllyTwo, new HealthState(20, 20))
                .SeedHealth(Enemy, new HealthState(20, 20))
                .SeedHealth(Undead, new HealthState(20, 20))
                .SeedPosition(Caster, new GridPosition(0, 0, 0))
                .SeedPosition(Ally, new GridPosition(1, 0, 0))
                .SeedPosition(AllyTwo, new GridPosition(0, 0, 1))
                .SeedPosition(Enemy, new GridPosition(enemyX, 0, 0))
                .SeedPosition(Undead, new GridPosition(1, 0, 1))
                .SeedActionEconomy(Caster, new ActionEconomyState(3, true))
                .SeedSpellSlot(new SpellSlotState(Pool, Caster, rankedSlotUses, rankedSlotUses))
                .SeedEncounter(
                    new EncounterState(
                        Encounter,
                        EncounterPhase.Active,
                        Heroes,
                        RoundNumber.First,
                        initiative,
                        0,
                        null,
                        1,
                        null
                    )
                );
            if (includeSecondaryCaster)
            {
                seed.SeedActionEconomy(AllyTwo, new ActionEconomyState(3, true))
                    .SeedSpellSlot(
                        new SpellSlotState(SecondaryPool, AllyTwo, rankedSlotUses, rankedSlotUses)
                    );
            }
            foreach (CreatureId creature in new[] { Caster, Ally, AllyTwo, Enemy, Undead })
            {
                seed.SeedStatistics(
                    new CreatureStatisticsState(
                        creature,
                        0,
                        10,
                        0,
                        0,
                        0,
                        new Dictionary<Skill, int>(),
                        Array.Empty<Modifier>()
                    )
                );
            }
            InMemoryRulesStore store = new(seed);
            TestCatalog catalog = new(creatureData);
            RuleRegistryBuilder registry = new();
            registry.Define(ConditionRules.DefinitionId).EffectState<ConditionState>();
            SpellFeatureRules.DefineRuleBindings(registry);
            RuleDispatcherBuilder builder = new RuleDispatcherBuilder(store, rolls)
                .UseHealthRules()
                .UseCheckResolution()
                .UseActiveEffectRules(registry.Build())
                .UseActionLifecycle(catalog)
                .UseSpellcastingRules(
                    catalog,
                    UnsupportedSpellAttackResolutionDataProvider.Instance,
                    targetingData
                )
                .RegisterHandler<ProbeArmorClassOp, int>(new ProbeArmorClassHandler())
                .RegisterHandler<ProbeAttackModifiersOp, ModifierCollection>(
                    new ProbeAttackModifiersHandler()
                )
                .RegisterHandler<ProbeSkillModifiersOp, ModifierCollection>(
                    new ProbeSkillModifiersHandler()
                )
                .RegisterHandler<ProbeSaveModifiersOp, ModifierCollection>(
                    new ProbeSaveModifiersHandler()
                )
                .RegisterHandler<ProbeStrikeDamageOp, IReadOnlyList<TypedDamageDice>>(
                    new ProbeStrikeDamageHandler()
                )
                .RegisterHandler<EmitInitiativeBoundaryOp, bool>(
                    new EmitInitiativeBoundaryHandler()
                )
                .RegisterHandler<RemoveEffectWorkflowOp, OpResult<ActiveEffectRemovalOutcome>>(
                    new RemoveEffectWorkflowHandler()
                )
                .RegisterReducer<CommitInitiativeBoundaryFactOp, bool>(
                    new CommitInitiativeBoundaryFactReducer(),
                    TestSource
                );
            SpellFeatureRules.ConfigureDispatcher(builder);
            ConditionRules.ConfigureDispatcher(builder);
            return new TestRuntime(store, builder.Build());
        }

        private static ResolvedOpResult<T> RequireResolved<T>(OpResult<T> result)
        {
            Assert.That(result, Is.TypeOf<ResolvedOpResult<T>>());
            return (ResolvedOpResult<T>)result;
        }

        private static ActiveRuleBinding BindingFor(
            RulesSnapshot snapshot,
            ActiveEffectId effect
        ) =>
            snapshot
                .RuleBindings.Select(pair => pair.Value)
                .Single(binding => binding.EffectId == effect);

        private static async Task ExpireEffect(
            TestRuntime runtime,
            ActiveEffectId effect,
            ActiveRuleBinding binding
        )
        {
            OpResult<ActiveEffectRemovalOutcome> removal = RequireResolved(
                await runtime.Dispatcher.Dispatch(
                    new RemoveEffectWorkflowOp(
                        new RemoveActiveEffectOp(
                            effect,
                            binding.Id,
                            runtime.Store.Snapshot.ActiveEffects[effect].EffectStateVersion,
                            ActiveEffectRemovalReason.Expired,
                            binding.Source
                        )
                    )
                )
            ).Value;
            RequireResolved(removal);
        }

        private sealed class TestRuntime
        {
            public TestRuntime(InMemoryRulesStore store, RuleDispatcher dispatcher)
            {
                Store = store;
                Dispatcher = dispatcher;
            }

            public InMemoryRulesStore Store { get; }
            public RuleDispatcher Dispatcher { get; }
        }

        private sealed class TestCatalog : ISpellActionCatalog
        {
            private readonly IReadOnlyDictionary<SpellId, SpellDefinition> definitions;
            private readonly IReadOnlyDictionary<SpellId, ISpellCastRule> rules;
            private readonly ISpellBook primaryBook = new TestBook(Pool);
            private readonly ISpellBook secondaryBook = new TestBook(SecondaryPool);

            public TestCatalog(ISpellCreatureDataProvider creatureData)
            {
                rules = SpellFeatureRules.CreateCatalog(creatureData);
                definitions = new[]
                {
                    Definition("shield", 1),
                    Definition("guidance", 1),
                    Definition("haunting-hymn", 2),
                    Definition("bless", 2),
                    Definition("infuse-vitality", 1, 2, 3),
                    Definition("heal", 1, 2, 3),
                }.ToDictionary(value => value.Id);
            }

            public ISpellCastRule GetRule(string slug) => rules[new SpellId(slug)];

            public ActionProfile GetBaseProfile(ActionDefinitionId definitionId) =>
                throw new KeyNotFoundException();

            public bool TryGetSpell(SpellReference reference, out SpellDefinition definition) =>
                definitions.TryGetValue(reference.Spell, out definition)
                && reference.Rank >= definition.MinimumRank;

            public ISpellBook GetSpellBook(CreatureId creature)
            {
                if (creature == Caster)
                    return primaryBook;
                if (creature == AllyTwo)
                    return secondaryBook;
                throw new KeyNotFoundException(
                    $"No test spellbook is registered for '{creature.Value}'."
                );
            }

            public bool TryGetCastRule(SpellId spell, out ISpellCastRule rule) =>
                rules.TryGetValue(spell, out rule);

            private static SpellDefinition Definition(string slug, params int[] actions) =>
                new(
                    new SpellId(slug),
                    slug,
                    1,
                    actions.Select(value => new SpellActionVariant(value)),
                    new[] { Trait.FromSlug("concentrate") },
                    Array.Empty<SpellEffectDirective>(),
                    Array.Empty<SpellAttackDefinition>()
                );
        }

        private sealed class TestBook : ISpellBook
        {
            private static readonly SpellId Shield = new("shield");
            private static readonly SpellId Guidance = new("guidance");
            private static readonly SpellId Hymn = new("haunting-hymn");
            private readonly SpellSlotPoolId pool;

            public TestBook(SpellSlotPoolId pool) => this.pool = pool;

            public IReadOnlyList<SpellReference> CastableSpells { get; } =
                new[]
                {
                    new SpellReference(Shield, 1),
                    new SpellReference(Guidance, 1),
                    new SpellReference(Hymn, 1),
                    new SpellReference(new SpellId("bless"), 1),
                    new SpellReference(new SpellId("infuse-vitality"), 1),
                    new SpellReference(new SpellId("heal"), 1),
                };

            public int SpellAttackModifier => 5;
            public int SpellDc => 15;

            public IReadOnlyList<SpellSlotState> CreateInitialSlotStates(CreatureId owner) =>
                new[] { new SpellSlotState(pool, owner, 1, 1) };

            public SpellCastAuthorization Authorize(
                CreatureId owner,
                SpellReference spell,
                ISpellSlotStateReader slots
            )
            {
                SpellCastAuthorization binding = BindResource(owner, spell);
                if (binding.Kind != SpellCastResourceKind.SpellSlot)
                    return binding;
                return
                    slots.TryGet(pool, out SpellSlotState state)
                    && state.Owner == owner
                    && state.Remaining > 0
                    ? binding
                    : SpellCastAuthorization.Unavailable("The ranked slot is unavailable.");
            }

            public SpellCastAuthorization BindResource(CreatureId owner, SpellReference spell)
            {
                if (!CastableSpells.Any(prepared => prepared.Spell == spell.Spell))
                    return SpellCastAuthorization.Unavailable("The exact spell is not prepared.");
                return spell.Spell == Shield || spell.Spell == Guidance || spell.Spell == Hymn
                    ? SpellCastAuthorization.Cantrip
                    : SpellCastAuthorization.FromPool(pool);
            }
        }

        private sealed class TestCreatureData : ISpellCreatureDataProvider
        {
            private readonly IReadOnlyList<TypedDefenseAdjustment> weaknesses;
            private readonly IReadOnlyList<TypedDefenseAdjustment> resistances;

            public TestCreatureData()
                : this(Array.Empty<TypedDefenseAdjustment>(), Array.Empty<TypedDefenseAdjustment>())
            { }

            public TestCreatureData(
                IReadOnlyList<TypedDefenseAdjustment> weaknesses,
                IReadOnlyList<TypedDefenseAdjustment> resistances
            )
            {
                this.weaknesses = weaknesses;
                this.resistances = resistances;
            }

            public bool IsUndead(CreatureId creature) => creature == Undead;

            public IReadOnlyList<TypedDefenseAdjustment> GetWeaknesses(CreatureId creature) =>
                creature == Enemy ? weaknesses : Array.Empty<TypedDefenseAdjustment>();

            public IReadOnlyList<TypedDefenseAdjustment> GetResistances(CreatureId creature) =>
                creature == Enemy ? resistances : Array.Empty<TypedDefenseAdjustment>();
        }

        private sealed class TestTargetingDataProvider : ISpellTargetingDataProvider
        {
            private readonly ActionValidationResult result;

            public TestTargetingDataProvider(ActionValidationResult result) => this.result = result;

            public ActionValidationResult ValidateSelection(
                RulesSnapshot snapshot,
                CreatureId actor,
                SpellSelectionProfile profile,
                SpellCastSelection selection
            ) => result;
        }

        private sealed class ProbeArmorClassOp : IRuleOp<int>
        {
            public ProbeArmorClassOp(CreatureId target, int armorClass, params Modifier[] modifiers)
            {
                Target = target;
                ArmorClass = armorClass;
                Modifiers = modifiers;
            }

            public CreatureId Target { get; }
            public int ArmorClass { get; }
            public IReadOnlyList<Modifier> Modifiers { get; }
        }

        private sealed class ProbeArmorClassHandler : IOpHandler<ProbeArmorClassOp, int>
        {
            public async ValueTask<int> Handle(
                OpFrame<ProbeArmorClassOp> frame,
                OpHandlerContext context
            ) =>
                RequireResolved(
                    await context.Dispatch(
                        new AdjustArmorClassOp(
                            frame.Op.Target,
                            frame.Op.ArmorClass,
                            frame.Op.Modifiers
                        )
                    )
                ).Value.Total;
        }

        private sealed class ProbeAttackModifiersOp : IRuleOp<ModifierCollection>
        {
            public ProbeAttackModifiersOp(CreatureId attacker, CreatureId target)
            {
                Attacker = attacker;
                Target = target;
            }

            public CreatureId Attacker { get; }
            public CreatureId Target { get; }
        }

        private sealed class ProbeAttackModifiersHandler
            : IOpHandler<ProbeAttackModifiersOp, ModifierCollection>
        {
            public async ValueTask<ModifierCollection> Handle(
                OpFrame<ProbeAttackModifiersOp> frame,
                OpHandlerContext context
            ) =>
                RequireResolved(
                    await context.Dispatch(
                        new CollectAttackModifiersOp(
                            frame.Op.Attacker,
                            frame.Op.Target,
                            CheckSource.From(frame.Id)
                        )
                    )
                ).Value;
        }

        private sealed class ProbeStrikeDamageOp : IRuleOp<IReadOnlyList<TypedDamageDice>>
        {
            public ProbeStrikeDamageOp(CreatureId attacker, CreatureId target)
            {
                Attacker = attacker;
                Target = target;
            }

            public CreatureId Attacker { get; }
            public CreatureId Target { get; }
        }

        private sealed class ProbeStrikeDamageHandler
            : IOpHandler<ProbeStrikeDamageOp, IReadOnlyList<TypedDamageDice>>
        {
            public async ValueTask<IReadOnlyList<TypedDamageDice>> Handle(
                OpFrame<ProbeStrikeDamageOp> frame,
                OpHandlerContext context
            ) =>
                RequireResolved(
                    await context.Dispatch(
                        new CollectStrikeDamageDiceOp(frame.Op.Attacker, frame.Op.Target)
                    )
                ).Value;
        }

        private sealed class ProbeSkillModifiersOp : IRuleOp<ModifierCollection>
        {
            public ProbeSkillModifiersOp(CreatureId actor) => Actor = actor;

            public CreatureId Actor { get; }
        }

        private sealed class ProbeSkillModifiersHandler
            : IOpHandler<ProbeSkillModifiersOp, ModifierCollection>
        {
            public async ValueTask<ModifierCollection> Handle(
                OpFrame<ProbeSkillModifiersOp> frame,
                OpHandlerContext context
            ) =>
                RequireResolved(
                    await context.Dispatch(
                        new CollectSkillCheckModifiersOp(
                            frame.Op.Actor,
                            Skill.FromSlug("perception"),
                            CheckSource.From(frame.Id)
                        )
                    )
                ).Value;
        }

        private sealed class ProbeSaveModifiersOp : IRuleOp<ModifierCollection>
        {
            public ProbeSaveModifiersOp(CreatureId actor) => Actor = actor;

            public CreatureId Actor { get; }
        }

        private sealed class ProbeSaveModifiersHandler
            : IOpHandler<ProbeSaveModifiersOp, ModifierCollection>
        {
            public async ValueTask<ModifierCollection> Handle(
                OpFrame<ProbeSaveModifiersOp> frame,
                OpHandlerContext context
            ) =>
                RequireResolved(
                    await context.Dispatch(
                        new CollectSavingThrowModifiersOp(
                            frame.Op.Actor,
                            SaveKind.Will,
                            CheckSource.From(frame.Id)
                        )
                    )
                ).Value;
        }

        private sealed class EmitInitiativeBoundaryOp : IRuleOp<bool>
        {
            public EmitInitiativeBoundaryOp(
                EncounterId encounter,
                RoundNumber round,
                CreatureId creature
            )
            {
                Encounter = encounter;
                Round = round;
                Creature = creature;
            }

            public EncounterId Encounter { get; }
            public RoundNumber Round { get; }
            public CreatureId Creature { get; }
        }

        private sealed class RemoveEffectWorkflowOp : IRuleOp<OpResult<ActiveEffectRemovalOutcome>>
        {
            public RemoveEffectWorkflowOp(RemoveActiveEffectOp removal) => Removal = removal;

            public RemoveActiveEffectOp Removal { get; }
        }

        private sealed class RemoveEffectWorkflowHandler
            : IOpHandler<RemoveEffectWorkflowOp, OpResult<ActiveEffectRemovalOutcome>>
        {
            public async ValueTask<OpResult<ActiveEffectRemovalOutcome>> Handle(
                OpFrame<RemoveEffectWorkflowOp> frame,
                OpHandlerContext context
            ) => await context.Dispatch(frame.Op.Removal);
        }

        private sealed class CommitInitiativeBoundaryFactOp : IRuleOp<bool>, IRuleSourcedOp
        {
            public CommitInitiativeBoundaryFactOp(
                EncounterId encounter,
                RoundNumber round,
                CreatureId creature
            )
            {
                Encounter = encounter;
                Round = round;
                Creature = creature;
            }

            public EncounterId Encounter { get; }
            public RoundNumber Round { get; }
            public CreatureId Creature { get; }
            public RuleSource Source => TestSource;
        }

        private sealed class EmitInitiativeBoundaryHandler
            : IOpHandler<EmitInitiativeBoundaryOp, bool>
        {
            public async ValueTask<bool> Handle(
                OpFrame<EmitInitiativeBoundaryOp> frame,
                OpHandlerContext context
            ) =>
                RequireResolved(
                    await context.Dispatch(
                        new CommitInitiativeBoundaryFactOp(
                            frame.Op.Encounter,
                            frame.Op.Round,
                            frame.Op.Creature
                        )
                    )
                ).Value;
        }

        private sealed class CommitInitiativeBoundaryFactReducer
            : IOpReducer<CommitInitiativeBoundaryFactOp, bool>
        {
            public ReductionResult<bool> Reduce(
                ReductionContext<CommitInitiativeBoundaryFactOp> context,
                RulesStateDraft state,
                FactSink facts
            )
            {
                facts.Stage(
                    new InitiativeBoundaryReachedFact(
                        context.Op.Encounter,
                        context.Op.Round,
                        context.Op.Creature
                    )
                );
                return ReductionResult<bool>.Accept(true);
            }
        }
    }
}
