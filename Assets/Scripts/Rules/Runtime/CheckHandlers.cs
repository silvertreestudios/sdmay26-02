using System;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    // Check handlers deliberately contain no state mutation. They combine a pure selector result
    // with the callback-scoped roll source so middleware and trace provenance remain engine-owned.
    internal sealed class SkillCheckHandler : IOpHandler<SkillCheckOp, CheckOutcome>
    {
        private static readonly DiceExpression D20 = new DiceExpression(1, 20);
        private readonly IRulesSelectors selectors;

        public SkillCheckHandler(IRulesSelectors selectors) =>
            this.selectors = selectors ?? throw new ArgumentNullException(nameof(selectors));

        public ValueTask<CheckOutcome> Handle(
            OpFrame<SkillCheckOp> frame,
            OpHandlerContext context)
        {
            CheckHandlerSupport.RequireAncestorSource(frame.Id, frame.Op.Source, context.Trace);
            ModifierCollection modifiers = selectors.GetSkillCheckModifiers(
                context.Snapshot,
                frame.Op.Actor,
                frame.Op.Skill);
            RollResult roll = context.Rolls.Roll(D20);
            return new ValueTask<CheckOutcome>(new CheckOutcome(
                frame.Op.Actor,
                frame.Op.Source,
                roll,
                modifiers,
                frame.Op.DifficultyClass));
        }
    }

    internal sealed class SavingThrowHandler : IOpHandler<SavingThrowOp, CheckOutcome>
    {
        private static readonly DiceExpression D20 = new DiceExpression(1, 20);
        private readonly IRulesSelectors selectors;

        public SavingThrowHandler(IRulesSelectors selectors) =>
            this.selectors = selectors ?? throw new ArgumentNullException(nameof(selectors));

        public ValueTask<CheckOutcome> Handle(
            OpFrame<SavingThrowOp> frame,
            OpHandlerContext context)
        {
            CheckHandlerSupport.RequireAncestorSource(frame.Id, frame.Op.Source, context.Trace);
            ModifierCollection modifiers = selectors.GetSavingThrowModifiers(
                context.Snapshot,
                frame.Op.Actor,
                frame.Op.Save);
            RollResult roll = context.Rolls.Roll(D20);
            return new ValueTask<CheckOutcome>(new CheckOutcome(
                frame.Op.Actor,
                frame.Op.Source,
                roll,
                modifiers,
                frame.Op.DifficultyClass));
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
