using System;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Resolves one concrete choice type for player, AI, replay, or deterministic test callers.
    /// </summary>
    /// <typeparam name="TChoice">The immutable choice data type handled by the adapter.</typeparam>
    /// <remarks>
    /// Implementations receive only the immutable prompt operation and snapshot. They cannot mutate
    /// rules state or dispatch privileged work. Return a resolved selected, unavailable, or failed
    /// choice for normal outcomes; return <see cref="CancelledOpResult{TResult}"/> only when the
    /// surrounding workflow explicitly cancels.
    /// </remarks>
    public interface IPromptAdapter<TChoice>
    {
        /// <summary>Asynchronously resolves one typed prompt request.</summary>
        /// <param name="prompt">The player identity and immutable request data.</param>
        /// <param name="snapshot">The committed snapshot captured when the prompt frame began.</param>
        /// <returns>
        /// A resolved choice outcome or an explicit cancelled operation result. Invalid and
        /// interrupted operation cases are not valid adapter responses.
        /// </returns>
        ValueTask<OpResult<ChoiceResult<TChoice>>> Prompt(
            PromptChoiceOp<TChoice> prompt,
            RulesSnapshot snapshot);
    }

    /// <summary>
    /// Consumes caller-supplied prompt results in order for deterministic tests, replays, and simulations.
    /// </summary>
    /// <typeparam name="TChoice">The immutable choice data type in the script.</typeparam>
    /// <remarks>
    /// Script exhaustion is a fixture or replay error and therefore throws. Expected unavailability,
    /// timeout, disconnect, decline, and cancellation must be supplied explicitly in the script.
    /// </remarks>
    public sealed class ScriptedPromptAdapter<TChoice> : IPromptAdapter<TChoice>
    {
        private readonly object gate = new object();
        private readonly OpResult<ChoiceResult<TChoice>>[] results;
        private int nextIndex;

        /// <summary>Creates an adapter that returns the supplied prompt results in order.</summary>
        /// <param name="results">The non-null resolved or cancelled results to consume.</param>
        /// <exception cref="ArgumentNullException"><paramref name="results"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// The script contains a missing result, attached Facts, a resolved result without a choice
        /// outcome, or an operation status other than resolved or cancelled.
        /// </exception>
        public ScriptedPromptAdapter(params OpResult<ChoiceResult<TChoice>>[] results)
        {
            if (results == null)
                throw new ArgumentNullException(nameof(results));
            foreach (OpResult<ChoiceResult<TChoice>> result in results)
            {
                if (result == null)
                    throw new ArgumentException("A prompt script cannot contain missing results.", nameof(results));
                if (result.Facts.Count != 0)
                    throw new ArgumentException("A prompt script cannot contain committed Facts.", nameof(results));
                if (result is ResolvedOpResult<ChoiceResult<TChoice>> resolved)
                {
                    if (resolved.Value == null)
                    {
                        throw new ArgumentException(
                            "A resolved prompt script result requires a choice outcome.",
                            nameof(results));
                    }
                    continue;
                }
                if (result is CancelledOpResult<ChoiceResult<TChoice>>)
                    continue;
                throw new ArgumentException(
                    "A prompt script may contain only resolved choice outcomes or cancellation.",
                    nameof(results));
            }
            this.results = (OpResult<ChoiceResult<TChoice>>[])results.Clone();
        }

        /// <summary>Gets the number of scripted prompt results not yet consumed.</summary>
        public int Remaining
        {
            get
            {
                lock (gate)
                    return results.Length - nextIndex;
            }
        }

        /// <inheritdoc/>
        public ValueTask<OpResult<ChoiceResult<TChoice>>> Prompt(
            PromptChoiceOp<TChoice> prompt,
            RulesSnapshot snapshot)
        {
            if (prompt == null)
                throw new ArgumentNullException(nameof(prompt));
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            lock (gate)
            {
                if (nextIndex >= results.Length)
                    throw new InvalidOperationException("The scripted prompt adapter has no result remaining.");
                return new ValueTask<OpResult<ChoiceResult<TChoice>>>(results[nextIndex++]);
            }
        }
    }
}
