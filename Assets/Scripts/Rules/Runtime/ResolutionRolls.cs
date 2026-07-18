using System;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Records one random input against the operation frame that consumed it.
    /// </summary>
    /// <remarks>
    /// The sequence is one-based within an operation. Together with the immutable dice expression
    /// and individual results, it is sufficient to reproduce check and damage calculations without
    /// depending on formatted diagnostics.
    /// </remarks>
    public sealed class ResolutionRoll
    {
        /// <summary>Gets the operation that consumed the roll.</summary>
        public OpId OperationId { get; }

        /// <summary>Gets this roll's one-based order within the operation.</summary>
        public int Sequence { get; }

        /// <summary>Gets the dice expression requested by the rules calculation.</summary>
        public DiceExpression Dice { get; }

        /// <summary>Gets the immutable individual values and total returned by the source.</summary>
        public RollResult Result { get; }

        internal ResolutionRoll(
            OpId operationId,
            int sequence,
            DiceExpression dice,
            RollResult result)
        {
            if (operationId.IsEmpty)
                throw new ArgumentException("A resolution roll requires an operation ID.", nameof(operationId));
            if (sequence <= 0)
                throw new ArgumentOutOfRangeException(nameof(sequence));

            OperationId = operationId;
            Sequence = sequence;
            Dice = dice;
            Result = result ?? throw new ArgumentNullException(nameof(result));
        }
    }

    public sealed partial class RuleDispatcher
    {
        internal RollResult Roll(OpId operationId, DiceExpression dice)
        {
            lock (gate)
            {
                if (activeRoot == null)
                    throw new InvalidOperationException("A rules roll requires an active root resolution.");

                IOpFrameView frame = Trace.Require(operationId);
                if (frame.RootId != activeRoot.RootId)
                {
                    throw new InvalidOperationException(
                        $"Operation {operationId.Value} does not belong to the active root resolution.");
                }

                RollResult result = rollService.Roll(dice);
                Trace.RecordRoll(operationId, dice, result);
                return result;
            }
        }
    }
}
