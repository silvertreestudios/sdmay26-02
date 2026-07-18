using System;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Requests one typed player, AI, replay, or test decision as a nested rules operation.
    /// </summary>
    /// <typeparam name="TChoice">The immutable data type selected from the request.</typeparam>
    /// <remarks>
    /// The operation contains no callback or presentation object. Dispatcher registration selects
    /// the adapter for <typeparamref name="TChoice"/>, while the adapter may route by
    /// <see cref="Player"/> to player or AI decision infrastructure.
    /// </remarks>
    public sealed class PromptChoiceOp<TChoice> : IRuleOp<ChoiceResult<TChoice>>
    {
        /// <summary>Gets the player or controller responsible for the decision.</summary>
        public PlayerId Player { get; }

        /// <summary>Gets the immutable typed request.</summary>
        public ChoiceRequest<TChoice> Request { get; }

        /// <summary>Creates a nested typed prompt operation.</summary>
        /// <param name="player">The player or controller responsible for the decision.</param>
        /// <param name="request">The immutable request and choices.</param>
        /// <exception cref="ArgumentException"><paramref name="player"/> is empty.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
        public PromptChoiceOp(PlayerId player, ChoiceRequest<TChoice> request)
        {
            if (player.IsEmpty)
                throw new ArgumentException("A prompt requires a player identity.", nameof(player));
            Player = player;
            Request = request ?? throw new ArgumentNullException(nameof(request));
        }
    }

    internal sealed class PromptRegistration<TChoice>
        : Registration<PromptChoiceOp<TChoice>, ChoiceResult<TChoice>>
    {
        private readonly IPromptAdapter<TChoice> adapter;

        public PromptRegistration(IPromptAdapter<TChoice> adapter)
            : base(InvocationPolicy.NestedOnly, ResolverMiddlewarePolicy.Disabled)
        {
            this.adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        public override bool IsReducer => false;

        public override async ValueTask<object> Invoke(
            IFrameInvocation invocation,
            RuleDispatcher dispatcher)
        {
            OpFrame<PromptChoiceOp<TChoice>> frame = GetFrame(invocation);
            OpResult<ChoiceResult<TChoice>> result = await adapter.Prompt(
                frame.Op,
                frame.StartSnapshot);
            if (result == null)
                throw new InvalidOperationException("A prompt adapter returned no operation result.");
            if (result.Facts.Count != 0)
                throw new InvalidOperationException("Prompt adapters cannot attach committed Facts.");

            if (result is ResolvedOpResult<ChoiceResult<TChoice>> resolved)
            {
                if (resolved.Value == null)
                    throw new InvalidOperationException("A resolved prompt requires a choice outcome.");
                if (resolved.Value is SelectedChoiceResult<TChoice> selected &&
                    !frame.Op.Request.Contains(selected.Choice))
                {
                    throw new InvalidOperationException(
                        "A prompt adapter selected a value not declared by the request.");
                }
                return result;
            }

            if (result is CancelledOpResult<ChoiceResult<TChoice>>)
                return result;

            throw new InvalidOperationException(
                "A prompt adapter may return only a resolved choice outcome or explicit cancellation.");
        }
    }
}
