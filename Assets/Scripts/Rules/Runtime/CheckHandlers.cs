using System;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    // Check handlers deliberately contain no state mutation. They combine a pure selector result
    // with the callback-scoped roll source so middleware and trace provenance remain engine-owned.
    internal sealed class SkillCheckHandler : IOpHandler<SkillCheckOp, CheckOutcome>
    {
        public async ValueTask<CheckOutcome> Handle(
            OpFrame<SkillCheckOp> frame,
            OpHandlerContext context)
        {
            CheckHandlerSupport.RequireAncestorSource(frame.Id, frame.Op.Source, context.Trace);
            OpResult<ModifierCollection> modifiersResult = await context.Dispatch(
                new CollectSkillCheckModifiersOp(
                    frame.Op.Actor,
                    frame.Op.Skill,
                    frame.Op.Source));
            if (!(modifiersResult is ResolvedOpResult<ModifierCollection> resolvedModifiers))
            {
                throw new InvalidOperationException(
                    "Skill-check modifier collection must produce a resolved result.");
            }

            RollResult roll = context.Rolls.Roll(DiceExpressions.D20);
            return new CheckOutcome(
                frame.Op.Actor,
                frame.Op.Source,
                roll,
                resolvedModifiers.Value,
                frame.Op.DifficultyClass);
        }
    }

    internal sealed class SavingThrowHandler : IOpHandler<SavingThrowOp, CheckOutcome>
    {
        public async ValueTask<CheckOutcome> Handle(
            OpFrame<SavingThrowOp> frame,
            OpHandlerContext context)
        {
            CheckHandlerSupport.RequireAncestorSource(frame.Id, frame.Op.Source, context.Trace);
            OpResult<ModifierCollection> modifiersResult = await context.Dispatch(
                new CollectSavingThrowModifiersOp(
                    frame.Op.Actor,
                    frame.Op.Save,
                    frame.Op.Source));
            if (!(modifiersResult is ResolvedOpResult<ModifierCollection> resolvedModifiers))
            {
                throw new InvalidOperationException(
                    "Saving-throw modifier collection must produce a resolved result.");
            }

            RollResult roll = context.Rolls.Roll(DiceExpressions.D20);
            return new CheckOutcome(
                frame.Op.Actor,
                frame.Op.Source,
                roll,
                resolvedModifiers.Value,
                frame.Op.DifficultyClass);
        }
    }

    internal sealed class CollectSkillCheckModifiersHandler
        : IOpHandler<CollectSkillCheckModifiersOp, ModifierCollection>
    {
        private readonly IRulesSelectors selectors;

        public CollectSkillCheckModifiersHandler(IRulesSelectors selectors) =>
            this.selectors = selectors ?? throw new ArgumentNullException(nameof(selectors));

        public ValueTask<ModifierCollection> Handle(
            OpFrame<CollectSkillCheckModifiersOp> frame,
            OpHandlerContext context)
        {
            CheckHandlerSupport.RequireAncestorSource(frame.Id, frame.Op.Source, context.Trace);
            return new ValueTask<ModifierCollection>(selectors.GetSkillCheckModifiers(
                context.Snapshot,
                frame.Op.Actor,
                frame.Op.Skill));
        }
    }

    internal sealed class CollectSavingThrowModifiersHandler
        : IOpHandler<CollectSavingThrowModifiersOp, ModifierCollection>
    {
        private readonly IRulesSelectors selectors;

        public CollectSavingThrowModifiersHandler(IRulesSelectors selectors) =>
            this.selectors = selectors ?? throw new ArgumentNullException(nameof(selectors));

        public ValueTask<ModifierCollection> Handle(
            OpFrame<CollectSavingThrowModifiersOp> frame,
            OpHandlerContext context)
        {
            CheckHandlerSupport.RequireAncestorSource(frame.Id, frame.Op.Source, context.Trace);
            return new ValueTask<ModifierCollection>(selectors.GetSavingThrowModifiers(
                context.Snapshot,
                frame.Op.Actor,
                frame.Op.Save));
        }
    }

    internal sealed class CollectAttackModifiersHandler
        : IOpHandler<CollectAttackModifiersOp, ModifierCollection>
    {
        private readonly IRulesSelectors selectors;

        public CollectAttackModifiersHandler(IRulesSelectors selectors) =>
            this.selectors = selectors ?? throw new ArgumentNullException(nameof(selectors));

        public ValueTask<ModifierCollection> Handle(
            OpFrame<CollectAttackModifiersOp> frame,
            OpHandlerContext context)
        {
            CheckHandlerSupport.RequireAncestorSource(frame.Id, frame.Op.Source, context.Trace);
            return new ValueTask<ModifierCollection>(
                selectors.GetAttackModifiers(context.Snapshot, frame.Op.Attacker));
        }
    }

    internal static class CheckHandlerSupport
    {
        public static void RequireAncestorSource(
            OpId checkId,
            CheckSource source,
            ResolutionTrace trace)
        {
            if (trace == null)
                throw new ArgumentNullException(nameof(trace));
            if (!trace.Exists(source.OperationId) ||
                !trace.IsDescendantOf(checkId, source.OperationId))
            {
                throw new InvalidOperationException(
                    $"Check source {source.OperationId.Value} is not an ancestor of operation " +
                    $"{checkId.Value}.");
            }
        }
    }
}
