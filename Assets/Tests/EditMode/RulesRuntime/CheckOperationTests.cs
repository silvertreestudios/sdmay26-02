using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    /// <summary>
    /// Verifies typed skill/save operations, trusted provenance, and interceptable modifier collection.
    /// </summary>
    public sealed class CheckOperationTests
    {
        private static readonly CreatureId Actor = new CreatureId("check-actor");
        private static readonly CreatureId Target = new CreatureId("check-target");
        private static readonly RuleSource ExistingStatus = RuleSource.FromSlug("existing-status");
        private static readonly RuleSource MiddlewareSource = RuleSource.FromSlug(
            "middleware-status"
        );
        private static readonly RuleDefinitionId MiddlewareDefinition = new RuleDefinitionId(
            "modifier-middleware"
        );

        [TestCase(10, 30, 20, DegreeOfSuccess.CriticalSuccess)]
        [TestCase(10, 20, 20, DegreeOfSuccess.Success)]
        [TestCase(10, 19, 20, DegreeOfSuccess.Failure)]
        [TestCase(10, 10, 20, DegreeOfSuccess.CriticalFailure)]
        [TestCase(20, 19, 20, DegreeOfSuccess.Success)]
        [TestCase(20, 30, 20, DegreeOfSuccess.CriticalSuccess)]
        [TestCase(1, 20, 20, DegreeOfSuccess.Failure)]
        [TestCase(1, 9, 20, DegreeOfSuccess.CriticalFailure)]
        public void DegreeResolverUsesThresholdsThenNaturalAdjustment(
            int naturalRoll,
            int total,
            int difficultyClass,
            DegreeOfSuccess expected
        )
        {
            Assert.That(
                DegreeOfSuccessResolver.Resolve(naturalRoll, total, difficultyClass),
                Is.EqualTo(expected)
            );
        }

        [Test]
        public void CheckOutcomeRejectsAnUnrepresentableTotal()
        {
            ModifierCollection modifiers = new ModifierCollection(
                Statistic.SkillCheck,
                new[] { Modifier.Untyped(int.MaxValue, ExistingStatus, Statistic.SkillCheck) }
            );

            Assert.Throws<OverflowException>(() =>
                new CheckOutcome(
                    Actor,
                    CheckSource.From(new OpId(1)),
                    new RollResult(DiceExpressions.D20, new[] { 1 }),
                    modifiers,
                    20
                )
            );
        }

        [Test]
        public async Task SkillCheckUsesScriptedRollAndRecordsTypedSourceProvenance()
        {
            ScriptedRollService rolls = new ScriptedRollService(14);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                new InMemoryRulesStore(CreateSeed(Array.Empty<Modifier>())),
                rolls,
                new SequentialOpIdProvider(100)
            )
                .RegisterHandler<SkillWorkflowOp, CheckOutcome>(new SkillWorkflowHandler())
                .UseCheckResolution()
                .Build();

            OpResult<CheckOutcome> result = await dispatcher.Dispatch(
                new SkillWorkflowOp(Skill.Acrobatics, 20)
            );

            ResolvedOpResult<CheckOutcome> resolved = RequireResolved(result);
            Assert.That(resolved.Value.Roll.Values.Single(), Is.EqualTo(14));
            Assert.That(resolved.Value.Modifiers.Total, Is.EqualTo(6));
            Assert.That(resolved.Value.Total, Is.EqualTo(20));
            Assert.That(resolved.Value.Degree, Is.EqualTo(DegreeOfSuccess.Success));
            Assert.That(resolved.Value.Source, Is.EqualTo(CheckSource.From(new OpId(100))));

            OpFrame<SkillCheckOp> check = dispatcher.Trace.Get<SkillCheckOp>(new OpId(101));
            Assert.That(check.ParentId, Is.EqualTo(new OpId(100)));
            Assert.That(check.Op.Source.OperationId, Is.EqualTo(new OpId(100)));
            Assert.That(
                dispatcher.Trace.GetRolls(check.Id).Single().Result.Values,
                Is.EqualTo(new[] { 14 })
            );
            Assert.That(rolls.Remaining, Is.Zero);
        }

        [Test]
        public async Task OrdinaryFailedSkillCheckRemainsAResolvedOperation()
        {
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                new InMemoryRulesStore(CreateSeed(Array.Empty<Modifier>())),
                new ScriptedRollService(7)
            )
                .RegisterHandler<SkillWorkflowOp, CheckOutcome>(new SkillWorkflowHandler())
                .UseCheckResolution()
                .Build();

            OpResult<CheckOutcome> result = await dispatcher.Dispatch(
                new SkillWorkflowOp(Skill.Acrobatics, 20)
            );

            Assert.That(result, Is.TypeOf<ResolvedOpResult<CheckOutcome>>());
            Assert.That(RequireResolved(result).Value.Total, Is.EqualTo(13));
            Assert.That(RequireResolved(result).Value.Degree, Is.EqualTo(DegreeOfSuccess.Failure));
        }

        [Test]
        public async Task SavingThrowUsesSelectedSaveAndNaturalOneAdjustment()
        {
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                new InMemoryRulesStore(CreateSeed(Array.Empty<Modifier>())),
                new ScriptedRollService(1)
            )
                .RegisterHandler<SaveWorkflowOp, CheckOutcome>(new SaveWorkflowHandler())
                .UseCheckResolution()
                .Build();

            OpResult<CheckOutcome> result = await dispatcher.Dispatch(
                new SaveWorkflowOp(SaveKind.Reflex, 15)
            );

            CheckOutcome outcome = RequireResolved(result).Value;
            Assert.That(outcome.Modifiers.Statistic, Is.EqualTo(Statistic.ReflexSave));
            Assert.That(outcome.Modifiers.Total, Is.EqualTo(8));
            Assert.That(outcome.Total, Is.EqualTo(9));
            Assert.That(outcome.Degree, Is.EqualTo(DegreeOfSuccess.CriticalFailure));
        }

        [Test]
        public async Task ActiveMiddlewareAddsSourcedModifierAndRecomputesSuppression()
        {
            Modifier existing = Modifier.StatusBonus(2, ExistingStatus, Statistic.AttackRoll);
            ActiveRuleBinding binding = new ActiveRuleBinding(
                new BindingId("modifier-binding"),
                MiddlewareDefinition,
                Actor,
                new ActiveEffectId("modifier-effect"),
                MiddlewareSource,
                0
            );
            RuleRegistryBuilder registry = new RuleRegistryBuilder();
            registry
                .Define(MiddlewareDefinition)
                .Middleware(RuleLifecyclePhase.Transformation, new AttackStatusMiddleware());
            RulesStateSeed seed = CreateSeed(new[] { existing }).SeedRuleBinding(binding);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                new InMemoryRulesStore(seed),
                new ScriptedRollService()
            )
                .RegisterHandler<AttackModifierWorkflowOp, ModifierCollection>(
                    new AttackModifierWorkflowHandler()
                )
                .UseCheckResolution()
                .UseRuleRegistry(registry.Build())
                .Build();

            OpResult<ModifierCollection> result = await dispatcher.Dispatch(
                new AttackModifierWorkflowOp()
            );

            ModifierCollection modifiers = RequireResolved(result).Value;
            Assert.That(modifiers.Total, Is.EqualTo(2));
            Assert.That(
                modifiers.Applied.Select(modifier => modifier.Source),
                Does.Contain(ExistingStatus)
            );
            Assert.That(
                modifiers.Suppressed.Select(modifier => modifier.Source),
                Is.EqualTo(new[] { MiddlewareSource })
            );
            Assert.That(modifiers.Suppressed.Single().Value, Is.EqualTo(1));
        }

        [Test]
        public async Task AttackCheckCombinesCopiedInitialCurrentAndMiddlewareCandidatesOnce()
        {
            List<Modifier> initialModifiers = new()
            {
                Modifier.Untyped(7, RuleSource.FromSlug("feature-base"), Statistic.AttackRoll),
                Modifier.Untyped(
                    -5,
                    RuleSource.FromSlug("multiple-attack-penalty"),
                    Statistic.AttackRoll
                ),
                new Modifier(
                    2,
                    ModifierType.Circumstance,
                    RuleSource.FromSlug("unity-captured"),
                    Statistic.AttackRoll
                ),
            };
            ActiveRuleBinding binding = new(
                new BindingId("attack-check-modifier-binding"),
                MiddlewareDefinition,
                Actor,
                new ActiveEffectId("attack-check-modifier-effect"),
                MiddlewareSource,
                0
            );
            RuleRegistryBuilder registry = new();
            registry
                .Define(MiddlewareDefinition)
                .Middleware(RuleLifecyclePhase.Transformation, new AttackStatusMiddleware());
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                new InMemoryRulesStore(
                    CreateSeed(
                            new[] { Modifier.StatusBonus(2, ExistingStatus, Statistic.AttackRoll) }
                        )
                        .SeedRuleBinding(binding)
                ),
                new ScriptedRollService(10)
            )
                .RegisterHandler<AttackCheckWorkflowOp, CheckOutcome>(
                    new AttackCheckWorkflowHandler(initialModifiers)
                )
                .UseCheckResolution()
                .UseRuleRegistry(registry.Build())
                .Build();

            CheckOutcome outcome = RequireResolved(
                await dispatcher.Dispatch(new AttackCheckWorkflowOp())
            ).Value;

            Assert.That(outcome.Modifiers.Total, Is.EqualTo(6));
            Assert.That(outcome.Total, Is.EqualTo(16));
            Assert.That(outcome.Degree, Is.EqualTo(DegreeOfSuccess.Success));
            Assert.That(outcome.Modifiers.Candidates, Has.Count.EqualTo(5));
            Assert.That(
                outcome
                    .Modifiers.Candidates.GroupBy(modifier => modifier.Source)
                    .All(group => group.Count() == 1),
                Is.True
            );
            Assert.That(
                outcome.Modifiers.Suppressed.Select(modifier => modifier.Source),
                Is.EqualTo(new[] { MiddlewareSource })
            );
        }

        [Test]
        public void AttackCheckCopiesInitialModifierCandidates()
        {
            List<Modifier> candidates = new()
            {
                Modifier.Untyped(3, RuleSource.FromSlug("initial"), Statistic.AttackRoll),
            };
            AttackCheckOp operation = new(
                Actor,
                Target,
                candidates,
                15,
                CheckSource.From(new OpId(1))
            );

            candidates.Add(
                Modifier.Untyped(
                    100,
                    RuleSource.FromSlug("late-caller-mutation"),
                    Statistic.AttackRoll
                )
            );

            Assert.That(operation.InitialModifiers, Has.Count.EqualTo(1));
            Assert.That(operation.InitialModifiers.Single().Value, Is.EqualTo(3));
        }

        [TestCase(20, 21, DegreeOfSuccess.Success)]
        [TestCase(1, 10, DegreeOfSuccess.CriticalFailure)]
        public async Task AttackCheckAppliesNaturalRollDegreeAdjustment(
            int naturalRoll,
            int difficultyClass,
            DegreeOfSuccess expected
        )
        {
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                new InMemoryRulesStore(CreateSeed(Array.Empty<Modifier>())),
                new ScriptedRollService(naturalRoll)
            )
                .RegisterHandler<AttackCheckWorkflowOp, CheckOutcome>(
                    new AttackCheckWorkflowHandler(Array.Empty<Modifier>(), difficultyClass)
                )
                .UseCheckResolution()
                .Build();

            CheckOutcome outcome = RequireResolved(
                await dispatcher.Dispatch(new AttackCheckWorkflowOp())
            ).Value;

            Assert.That(outcome.Degree, Is.EqualTo(expected));
        }

        [Test]
        public void CheckOperationsRejectExternalDispatchAndUntrustedSourceIdsBeforeRolling()
        {
            ScriptedRollService externalRolls = new ScriptedRollService(10);
            RuleDispatcher externalDispatcher = new RuleDispatcherBuilder(
                new InMemoryRulesStore(CreateSeed(Array.Empty<Modifier>())),
                externalRolls
            )
                .UseCheckResolution()
                .Build();
            SkillCheckOp external = new SkillCheckOp(
                Actor,
                Skill.Acrobatics,
                15,
                CheckSource.From(new OpId(1))
            );

            InvalidOperationException externalError = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await externalDispatcher.Dispatch(external)
            );
            Assert.That(externalError.Message, Does.Contain("nested-only"));
            Assert.That(externalRolls.Remaining, Is.EqualTo(1));

            AttackCheckOp externalAttack = new(
                Actor,
                Target,
                Array.Empty<Modifier>(),
                15,
                CheckSource.From(new OpId(1))
            );
            InvalidOperationException externalAttackError =
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await externalDispatcher.Dispatch(externalAttack)
                );
            Assert.That(externalAttackError.Message, Does.Contain("nested-only"));
            Assert.That(externalRolls.Remaining, Is.EqualTo(1));

            ScriptedRollService untrustedRolls = new ScriptedRollService(10);
            RuleDispatcher untrustedDispatcher = new RuleDispatcherBuilder(
                new InMemoryRulesStore(CreateSeed(Array.Empty<Modifier>())),
                untrustedRolls,
                new SequentialOpIdProvider(20)
            )
                .RegisterHandler<UntrustedCheckWorkflowOp, CheckOutcome>(
                    new UntrustedCheckWorkflowHandler()
                )
                .UseCheckResolution()
                .Build();

            InvalidOperationException sourceError = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await untrustedDispatcher.Dispatch(new UntrustedCheckWorkflowOp())
            );
            Assert.That(sourceError.Message, Does.Contain("is not an ancestor"));
            Assert.That(untrustedRolls.Remaining, Is.EqualTo(1));

            ScriptedRollService untrustedAttackRolls = new(10);
            RuleDispatcher untrustedAttackDispatcher = new RuleDispatcherBuilder(
                new InMemoryRulesStore(CreateSeed(Array.Empty<Modifier>())),
                untrustedAttackRolls,
                new SequentialOpIdProvider(30)
            )
                .RegisterHandler<UntrustedAttackCheckWorkflowOp, CheckOutcome>(
                    new UntrustedAttackCheckWorkflowHandler()
                )
                .UseCheckResolution()
                .Build();

            InvalidOperationException attackSourceError =
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await untrustedAttackDispatcher.Dispatch(new UntrustedAttackCheckWorkflowOp())
                );
            Assert.That(attackSourceError.Message, Does.Contain("is not an ancestor"));
            Assert.That(untrustedAttackRolls.Remaining, Is.EqualTo(1));
        }

        [Test]
        public async Task DamageCalculationAndNestedCheckConsumeOneInjectedSourceAndTrace()
        {
            ScriptedRollService rolls = new ScriptedRollService(3, 5, 12);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                new InMemoryRulesStore(CreateSeed(Array.Empty<Modifier>())),
                rolls,
                new SequentialOpIdProvider(200)
            )
                .RegisterHandler<DamageThenCheckWorkflowOp, DamageThenCheckOutcome>(
                    new DamageThenCheckWorkflowHandler()
                )
                .UseCheckResolution()
                .Build();

            DamageThenCheckOutcome outcome = RequireResolved(
                await dispatcher.Dispatch(new DamageThenCheckWorkflowOp())
            ).Value;

            Assert.That(outcome.Damage.DiceRoll.Values, Is.EqualTo(new[] { 3, 5 }));
            Assert.That(outcome.Damage.BaseDamage, Is.EqualTo(10));
            Assert.That(outcome.Damage.TotalDamage, Is.EqualTo(20));
            Assert.That(outcome.Check.Roll.Values.Single(), Is.EqualTo(12));
            Assert.That(
                dispatcher.Trace.GetRolls(new OpId(200)).Single().Dice,
                Is.EqualTo(new DiceExpression(2, 6))
            );
            Assert.That(
                dispatcher.Trace.GetRolls(new OpId(201)).Single().Dice,
                Is.EqualTo(DiceExpressions.D20)
            );
            Assert.That(rolls.Remaining, Is.Zero);
        }

        private static RulesStateSeed CreateSeed(IEnumerable<Modifier> modifiers)
        {
            PlayerId players = new PlayerId("players");
            PlayerId enemies = new PlayerId("enemies");
            return new RulesStateSeed()
                .SeedCreature(new CreatureState(Actor, players, Array.Empty<Trait>()))
                .SeedCreature(new CreatureState(Target, enemies, Array.Empty<Trait>()))
                .SeedStatistics(
                    new CreatureStatisticsState(
                        Actor,
                        7,
                        18,
                        6,
                        8,
                        5,
                        new Dictionary<Skill, int> { [Skill.Acrobatics] = 6 },
                        modifiers
                    )
                );
        }

        private static ResolvedOpResult<T> RequireResolved<T>(OpResult<T> result)
        {
            Assert.That(result, Is.TypeOf<ResolvedOpResult<T>>());
            return (ResolvedOpResult<T>)result;
        }

        private sealed class SkillWorkflowOp : IRuleOp<CheckOutcome>
        {
            public Skill Skill { get; }
            public int DifficultyClass { get; }

            public SkillWorkflowOp(Skill skill, int difficultyClass)
            {
                Skill = skill;
                DifficultyClass = difficultyClass;
            }
        }

        private sealed class SkillWorkflowHandler : IOpHandler<SkillWorkflowOp, CheckOutcome>
        {
            public async ValueTask<CheckOutcome> Handle(
                OpFrame<SkillWorkflowOp> frame,
                OpHandlerContext context
            ) =>
                RequireResolved(
                    await context.Dispatch(
                        new SkillCheckOp(
                            Actor,
                            frame.Op.Skill,
                            frame.Op.DifficultyClass,
                            CheckSource.From(frame.Id)
                        )
                    )
                ).Value;
        }

        private sealed class SaveWorkflowOp : IRuleOp<CheckOutcome>
        {
            public SaveKind Save { get; }
            public int DifficultyClass { get; }

            public SaveWorkflowOp(SaveKind save, int difficultyClass)
            {
                Save = save;
                DifficultyClass = difficultyClass;
            }
        }

        private sealed class SaveWorkflowHandler : IOpHandler<SaveWorkflowOp, CheckOutcome>
        {
            public async ValueTask<CheckOutcome> Handle(
                OpFrame<SaveWorkflowOp> frame,
                OpHandlerContext context
            ) =>
                RequireResolved(
                    await context.Dispatch(
                        new SavingThrowOp(
                            Actor,
                            frame.Op.Save,
                            frame.Op.DifficultyClass,
                            CheckSource.From(frame.Id)
                        )
                    )
                ).Value;
        }

        private sealed class AttackModifierWorkflowOp : IRuleOp<ModifierCollection> { }

        private sealed class AttackModifierWorkflowHandler
            : IOpHandler<AttackModifierWorkflowOp, ModifierCollection>
        {
            public async ValueTask<ModifierCollection> Handle(
                OpFrame<AttackModifierWorkflowOp> frame,
                OpHandlerContext context
            ) =>
                RequireResolved(
                    await context.Dispatch(
                        new CollectAttackModifiersOp(Actor, Target, CheckSource.From(frame.Id))
                    )
                ).Value;
        }

        private sealed class AttackCheckWorkflowOp : IRuleOp<CheckOutcome> { }

        private sealed class AttackCheckWorkflowHandler
            : IOpHandler<AttackCheckWorkflowOp, CheckOutcome>
        {
            private readonly IReadOnlyList<Modifier> initialModifiers;
            private readonly int difficultyClass;

            public AttackCheckWorkflowHandler(
                IEnumerable<Modifier> initialModifiers,
                int difficultyClass = 15
            )
            {
                this.initialModifiers = initialModifiers.ToArray();
                this.difficultyClass = difficultyClass;
            }

            public async ValueTask<CheckOutcome> Handle(
                OpFrame<AttackCheckWorkflowOp> frame,
                OpHandlerContext context
            ) =>
                RequireResolved(
                    await context.Dispatch(
                        new AttackCheckOp(
                            Actor,
                            Target,
                            initialModifiers,
                            difficultyClass,
                            CheckSource.From(frame.Id)
                        )
                    )
                ).Value;
        }

        private sealed class AttackStatusMiddleware
            : IOpMiddleware<CollectAttackModifiersOp, ModifierCollection>
        {
            public async ValueTask<OpResult<ModifierCollection>> Invoke(
                OpFrame<CollectAttackModifiersOp> frame,
                OpMiddlewareContext context,
                OpNext<ModifierCollection> next
            )
            {
                OpResult<ModifierCollection> result = await next();
                if (result is ResolvedOpResult<ModifierCollection> resolved)
                {
                    return OpResult<ModifierCollection>.Resolved(
                        resolved.Value.Add(
                            Modifier.StatusBonus(1, context.Source, Statistic.AttackRoll)
                        )
                    );
                }
                return result;
            }
        }

        private sealed class UntrustedCheckWorkflowOp : IRuleOp<CheckOutcome> { }

        private sealed class UntrustedCheckWorkflowHandler
            : IOpHandler<UntrustedCheckWorkflowOp, CheckOutcome>
        {
            public async ValueTask<CheckOutcome> Handle(
                OpFrame<UntrustedCheckWorkflowOp> frame,
                OpHandlerContext context
            ) =>
                RequireResolved(
                    await context.Dispatch(
                        new SkillCheckOp(
                            Actor,
                            Skill.Acrobatics,
                            15,
                            CheckSource.From(new OpId(999))
                        )
                    )
                ).Value;
        }

        private sealed class UntrustedAttackCheckWorkflowOp : IRuleOp<CheckOutcome> { }

        private sealed class UntrustedAttackCheckWorkflowHandler
            : IOpHandler<UntrustedAttackCheckWorkflowOp, CheckOutcome>
        {
            public async ValueTask<CheckOutcome> Handle(
                OpFrame<UntrustedAttackCheckWorkflowOp> frame,
                OpHandlerContext context
            ) =>
                RequireResolved(
                    await context.Dispatch(
                        new AttackCheckOp(
                            Actor,
                            Target,
                            Array.Empty<Modifier>(),
                            15,
                            CheckSource.From(new OpId(999))
                        )
                    )
                ).Value;
        }

        private sealed class DamageThenCheckWorkflowOp : IRuleOp<DamageThenCheckOutcome> { }

        private sealed class DamageThenCheckOutcome
        {
            public DamageRollOutcome Damage { get; }
            public CheckOutcome Check { get; }

            public DamageThenCheckOutcome(DamageRollOutcome damage, CheckOutcome check)
            {
                Damage = damage;
                Check = check;
            }
        }

        private sealed class DamageThenCheckWorkflowHandler
            : IOpHandler<DamageThenCheckWorkflowOp, DamageThenCheckOutcome>
        {
            public async ValueTask<DamageThenCheckOutcome> Handle(
                OpFrame<DamageThenCheckWorkflowOp> frame,
                OpHandlerContext context
            )
            {
                DamageRollOutcome damageResult = DamageRollOutcome.Roll(
                    new DiceExpression(2, 6),
                    2,
                    DegreeOfSuccess.CriticalSuccess,
                    context.Rolls
                );
                CheckOutcome check = RequireResolved(
                    await context.Dispatch(
                        new SkillCheckOp(Actor, Skill.Acrobatics, 20, CheckSource.From(frame.Id))
                    )
                ).Value;
                return new DamageThenCheckOutcome(damageResult, check);
            }
        }
    }
}
