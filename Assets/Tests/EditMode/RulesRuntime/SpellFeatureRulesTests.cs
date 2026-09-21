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
        public async Task UnusedGuidanceExpiresAtCasterTurnStartAndCreatesImmunity()
        {
            TestRuntime runtime = CreateRuntime(new ScriptedRollService());
            RequireResolved(await runtime.Dispatcher.Dispatch(Cast("guidance", 1, Ally)));

            await runtime.Dispatcher.Dispatch(
                new EmitTurnBeganOp(
                    new TurnIdentity(
                        new EncounterId("spell-feature-encounter"),
                        new TurnId(1),
                        Caster,
                        RoundNumber.First,
                        1
                    )
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
            Assert.That(immunity.Duration, Is.EqualTo(EffectDuration.Minutes(60)));
            Assert.That(immunity.GetState<SpellEffectState>().Target, Is.EqualTo(Ally));
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
        public async Task HealThreeActionEmanationHealsAlliesDamagesUndeadAndSkipsLivingEnemies()
        {
            ScriptedRollService rolls = new(10, 5);
            TestRuntime runtime = CreateRuntime(rolls);

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
                Is.Zero
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
        ) =>
            new(
                Caster,
                new SpellReference(new SpellId(slug), rank),
                new SpellActionVariant(actions),
                targets.Length == 0 ? SpellCastSelection.Empty : new SpellCastSelection(targets)
            );

        private static TestRuntime CreateRuntime(IRollService rolls, int enemyX = 2)
        {
            return CreateRuntime(rolls, new TestCreatureData(), enemyX);
        }

        private static TestRuntime CreateRuntime(
            IRollService rolls,
            TestCreatureData creatureData,
            int enemyX = 2
        )
        {
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
                .SeedSpellSlot(new SpellSlotState(Pool, Caster, 1, 1));
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
                .UseSpellcastingRules(catalog)
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
                .RegisterHandler<EmitTurnBeganOp, bool>(new EmitTurnBeganHandler())
                .RegisterReducer<CommitTurnBeganOp, bool>(new CommitTurnBeganReducer(), TestSource);
            SpellFeatureRules.ConfigureDispatcher(builder);
            ConditionRules.ConfigureDispatcher(builder);
            return new TestRuntime(store, builder.Build());
        }

        private static ResolvedOpResult<T> RequireResolved<T>(OpResult<T> result)
        {
            Assert.That(result, Is.TypeOf<ResolvedOpResult<T>>());
            return (ResolvedOpResult<T>)result;
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
            private readonly ISpellBook book = new TestBook();

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
                if (creature != Caster)
                    throw new KeyNotFoundException(
                        $"No test spellbook is registered for '{creature.Value}'."
                    );
                return book;
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
                new[] { new SpellSlotState(Pool, owner, 1, 1) };

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
                    slots.TryGet(Pool, out SpellSlotState state)
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
                    : SpellCastAuthorization.FromPool(Pool);
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

        private sealed class EmitTurnBeganOp : IRuleOp<bool>
        {
            public EmitTurnBeganOp(TurnIdentity turn) => Turn = turn;

            public TurnIdentity Turn { get; }
        }

        private sealed class CommitTurnBeganOp : IRuleOp<bool>, IRuleSourcedOp
        {
            public CommitTurnBeganOp(TurnIdentity turn) => Turn = turn;

            public TurnIdentity Turn { get; }
            public RuleSource Source => TestSource;
        }

        private sealed class EmitTurnBeganHandler : IOpHandler<EmitTurnBeganOp, bool>
        {
            public async ValueTask<bool> Handle(
                OpFrame<EmitTurnBeganOp> frame,
                OpHandlerContext context
            ) =>
                RequireResolved(await context.Dispatch(new CommitTurnBeganOp(frame.Op.Turn))).Value;
        }

        private sealed class CommitTurnBeganReducer : IOpReducer<CommitTurnBeganOp, bool>
        {
            public ReductionResult<bool> Reduce(
                ReductionContext<CommitTurnBeganOp> context,
                RulesStateDraft state,
                FactSink facts
            )
            {
                facts.Stage(new TurnBeganFact(context.Op.Turn));
                return ReductionResult<bool>.Accept(true);
            }
        }
    }
}
