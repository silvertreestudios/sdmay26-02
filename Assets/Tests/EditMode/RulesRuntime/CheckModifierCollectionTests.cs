using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    /// <summary>
    /// Verifies that skill and saving-throw modifiers pass through typed middleware before rolling.
    /// </summary>
    public sealed class CheckModifierCollectionTests
    {
        private static readonly CreatureId Actor = new CreatureId("modifier-check-actor");
        private static readonly Skill SailingLore = Skill.FromName("Sailing Lore");
        private static readonly RuleDefinitionId SkillDefinition =
            new RuleDefinitionId("skill-check-status");
        private static readonly RuleDefinitionId SaveDefinition =
            new RuleDefinitionId("saving-throw-status");
        private static readonly RuleSource SkillSource = RuleSource.FromSlug("skill-effect");
        private static readonly RuleSource SaveSource = RuleSource.FromSlug("save-effect");

        [Test]
        public async Task SkillAndSaveModifiersAreCollectedThroughMiddlewareBeforeRolling()
        {
            RuleRegistryBuilder registry = new RuleRegistryBuilder();
            registry.Define(SkillDefinition).Middleware(
                RuleLifecyclePhase.Transformation,
                new SkillStatusMiddleware());
            registry.Define(SaveDefinition).Middleware(
                RuleLifecyclePhase.Transformation,
                new SaveStatusMiddleware());

            RulesStateSeed seed = CreateSeed()
                .SeedRuleBinding(new ActiveRuleBinding(
                    new BindingId("skill-binding"),
                    SkillDefinition,
                    Actor,
                    new ActiveEffectId("skill-active-effect"),
                    SkillSource,
                    0))
                .SeedRuleBinding(new ActiveRuleBinding(
                    new BindingId("save-binding"),
                    SaveDefinition,
                    Actor,
                    new ActiveEffectId("save-active-effect"),
                    SaveSource,
                    1));
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                    new InMemoryRulesStore(seed),
                    new ScriptedRollService(10, 10),
                    new SequentialOpIdProvider(500))
                .RegisterHandler<CheckWorkflowOp, CheckWorkflowOutcome>(
                    new CheckWorkflowHandler())
                .UseCheckResolution()
                .UseRuleRegistry(registry.Build())
                .Build();

            CheckWorkflowOutcome outcome = RequireResolved(
                await dispatcher.Dispatch(new CheckWorkflowOp())).Value;

            Assert.That(outcome.Skill.Modifiers.Total, Is.EqualTo(8));
            Assert.That(outcome.Skill.Modifiers.Applied.Select(value => value.Source),
                Does.Contain(SkillSource));
            Assert.That(outcome.Skill.Total, Is.EqualTo(18));
            Assert.That(outcome.Save.Modifiers.Total, Is.EqualTo(9));
            Assert.That(outcome.Save.Modifiers.Applied.Select(value => value.Source),
                Does.Contain(SaveSource));
            Assert.That(outcome.Save.Total, Is.EqualTo(19));

            OpFrame<CollectSkillCheckModifiersOp> skillCollection =
                dispatcher.Trace.Get<CollectSkillCheckModifiersOp>(new OpId(502));
            Assert.That(skillCollection.ParentId, Is.EqualTo(new OpId(501)));
            Assert.That(skillCollection.Op.Skill, Is.EqualTo(SailingLore));
            Assert.That(skillCollection.Op.Source, Is.EqualTo(CheckSource.From(new OpId(500))));

            OpFrame<CollectSavingThrowModifiersOp> saveCollection =
                dispatcher.Trace.Get<CollectSavingThrowModifiersOp>(new OpId(504));
            Assert.That(saveCollection.ParentId, Is.EqualTo(new OpId(503)));
            Assert.That(saveCollection.Op.Save, Is.EqualTo(SaveKind.Reflex));
            Assert.That(saveCollection.Op.Source, Is.EqualTo(CheckSource.From(new OpId(500))));
        }

        private static RulesStateSeed CreateSeed()
        {
            PlayerId player = new PlayerId("players");
            return new RulesStateSeed()
                .SeedCreature(new CreatureState(Actor, player, Array.Empty<Trait>()))
                .SeedStatistics(new CreatureStatisticsState(
                    Actor,
                    7,
                    18,
                    6,
                    8,
                    5,
                    new Dictionary<Skill, int> { [SailingLore] = 6 },
                    Array.Empty<Modifier>()));
        }

        private static ResolvedOpResult<T> RequireResolved<T>(OpResult<T> result)
        {
            Assert.That(result, Is.TypeOf<ResolvedOpResult<T>>());
            return (ResolvedOpResult<T>)result;
        }

        private sealed class CheckWorkflowOp : IRuleOp<CheckWorkflowOutcome>
        {
        }

        private sealed class CheckWorkflowOutcome
        {
            public CheckOutcome Skill { get; }
            public CheckOutcome Save { get; }

            public CheckWorkflowOutcome(CheckOutcome skill, CheckOutcome save)
            {
                Skill = skill;
                Save = save;
            }
        }

        private sealed class CheckWorkflowHandler
            : IOpHandler<CheckWorkflowOp, CheckWorkflowOutcome>
        {
            public async ValueTask<CheckWorkflowOutcome> Handle(
                OpFrame<CheckWorkflowOp> frame,
                OpHandlerContext context)
            {
                CheckSource source = CheckSource.From(frame.Id);
                CheckOutcome skill = RequireResolved(await context.Dispatch(new SkillCheckOp(
                    Actor,
                    SailingLore,
                    20,
                    source))).Value;
                CheckOutcome save = RequireResolved(await context.Dispatch(new SavingThrowOp(
                    Actor,
                    SaveKind.Reflex,
                    20,
                    source))).Value;
                return new CheckWorkflowOutcome(skill, save);
            }
        }

        private sealed class SkillStatusMiddleware
            : IOpMiddleware<CollectSkillCheckModifiersOp, ModifierCollection>
        {
            public async ValueTask<OpResult<ModifierCollection>> Invoke(
                OpFrame<CollectSkillCheckModifiersOp> frame,
                OpMiddlewareContext context,
                OpNext<ModifierCollection> next)
            {
                OpResult<ModifierCollection> result = await next();
                if (result is ResolvedOpResult<ModifierCollection> resolved &&
                    frame.Op.Actor == Actor && frame.Op.Skill == SailingLore)
                {
                    return OpResult<ModifierCollection>.Resolved(resolved.Value.Add(
                        Modifier.StatusBonus(2, context.Source, Statistic.SkillCheck)));
                }
                return result;
            }
        }

        private sealed class SaveStatusMiddleware
            : IOpMiddleware<CollectSavingThrowModifiersOp, ModifierCollection>
        {
            public async ValueTask<OpResult<ModifierCollection>> Invoke(
                OpFrame<CollectSavingThrowModifiersOp> frame,
                OpMiddlewareContext context,
                OpNext<ModifierCollection> next)
            {
                OpResult<ModifierCollection> result = await next();
                if (result is ResolvedOpResult<ModifierCollection> resolved &&
                    frame.Op.Actor == Actor && frame.Op.Save == SaveKind.Reflex)
                {
                    return OpResult<ModifierCollection>.Resolved(resolved.Value.Add(
                        Modifier.StatusBonus(1, context.Source, Statistic.ReflexSave)));
                }
                return result;
            }
        }
    }
}
