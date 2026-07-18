using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    /// <summary>
    /// Verifies deterministic roll sources, callback ownership, and resolution-trace provenance.
    /// </summary>
    public sealed class RollServiceTests
    {
        [Test]
        public void ScriptedSourceConsumesIndividualValuesAtomically()
        {
            ScriptedRollService rolls = new ScriptedRollService(4, 6, 20);

            RollResult damage = rolls.Roll(new DiceExpression(2, 6));

            Assert.That(damage.Values, Is.EqualTo(new[] { 4, 6 }));
            Assert.That(damage.Total, Is.EqualTo(10));
            Assert.That(rolls.Remaining, Is.EqualTo(1));

            InvalidOperationException exhausted = Assert.Throws<InvalidOperationException>(() =>
                rolls.Roll(new DiceExpression(2, 20)));
            Assert.That(exhausted.Message, Does.Contain("only 1 remain"));
            Assert.That(rolls.Remaining, Is.EqualTo(1),
                "A failed request must not partially consume the script.");

            Assert.That(rolls.Roll(new DiceExpression(1, 20)).Values.Single(), Is.EqualTo(20));
            Assert.That(rolls.Remaining, Is.Zero);
        }

        [Test]
        public void ScriptedSourceRejectsOutOfRangeValueWithoutConsumingIt()
        {
            ScriptedRollService rolls = new ScriptedRollService(7);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                rolls.Roll(new DiceExpression(1, 6)));

            Assert.That(error.Message, Does.Contain("outside the 1-6 range"));
            Assert.That(rolls.Remaining, Is.EqualTo(1));
        }

        [Test]
        public void RollContractsRejectUninitializedDiceExpressions()
        {
            DiceExpression empty = default;

            Assert.That(empty.IsEmpty, Is.True);
            Assert.Throws<ArgumentException>(() => new RollResult(empty, Array.Empty<int>()));
        }

        [Test]
        public void SeededRuntimeSourceProducesOnlyValuesInTheRequestedRange()
        {
            RandomRollService rolls = new RandomRollService(12345);

            for (int index = 0; index < 100; index++)
            {
                RollResult result = rolls.Roll(new DiceExpression(3, 8));
                Assert.That(result.Values, Has.Count.EqualTo(3));
                Assert.That(result.Values.All(value => value >= 1 && value <= 8), Is.True);
            }
        }

        [Test]
        public async Task CallbackRollsAreRecordedAgainstTheirFrameAndShownInDiagnostics()
        {
            ScriptedRollService rolls = new ScriptedRollService(5, 2, 17);
            RecordingRollHandler handler = new RecordingRollHandler();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                    new InMemoryRulesStore(),
                    rolls,
                    new SequentialOpIdProvider(40))
                .RegisterHandler<RecordingRollOp, int>(handler)
                .Build();

            OpResult<int> result = await dispatcher.Dispatch(new RecordingRollOp(
                new DiceExpression(2, 6),
                new DiceExpression(1, 20)));

            Assert.That(result, Is.TypeOf<ResolvedOpResult<int>>());
            Assert.That(((ResolvedOpResult<int>)result).Value, Is.EqualTo(24));
            IReadOnlyList<ResolutionRoll> recorded = dispatcher.Trace.GetRolls(new OpId(40));
            Assert.That(recorded, Has.Count.EqualTo(2));
            Assert.That(recorded.Select(value => value.Sequence), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(recorded.Select(value => value.Dice), Is.EqualTo(new[]
            {
                new DiceExpression(2, 6),
                new DiceExpression(1, 20)
            }));
            Assert.That(recorded[0].Result.Values, Is.EqualTo(new[] { 5, 2 }));
            Assert.That(recorded[1].Result.Values, Is.EqualTo(new[] { 17 }));
            Assert.That(dispatcher.Diagnostics.Compact, Does.Contain("roll 1: 2d6 -> [5, 2] total=7"));
            Assert.That(dispatcher.Diagnostics.Compact, Does.Contain("roll 2: 1d20 -> [17] total=17"));

            InvalidOperationException retainedError = Assert.Throws<InvalidOperationException>(() =>
                handler.RetainedRolls.Roll(new DiceExpression(1, 6)));
            Assert.That(retainedError.Message, Does.Contain("after its callback returns"));
        }

        private sealed class RecordingRollOp : IRuleOp<int>
        {
            public IReadOnlyList<DiceExpression> Dice { get; }

            public RecordingRollOp(params DiceExpression[] dice) =>
                Dice = Array.AsReadOnly(dice);
        }

        private sealed class RecordingRollHandler : IOpHandler<RecordingRollOp, int>
        {
            public IRollService RetainedRolls { get; private set; }

            public ValueTask<int> Handle(
                OpFrame<RecordingRollOp> frame,
                OpHandlerContext context)
            {
                RetainedRolls = context.Rolls;
                int total = 0;
                foreach (DiceExpression dice in frame.Op.Dice)
                    total += context.Rolls.Roll(dice).Total;
                return new ValueTask<int>(total);
            }
        }
    }
}
