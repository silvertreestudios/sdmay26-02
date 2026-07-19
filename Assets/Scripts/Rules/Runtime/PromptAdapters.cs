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
            RulesSnapshot snapshot
        );
    }
}
