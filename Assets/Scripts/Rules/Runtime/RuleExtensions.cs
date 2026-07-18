using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Identifies the semantic stage in which a rule extension participates.
    /// </summary>
    /// <remarks>
    /// The fixed stages provide deterministic ordering without exposing arbitrary numeric
    /// priorities. Choose the stage that describes the rule's purpose. If two rules need a more
    /// specific ordering relationship, introduce a distinct lifecycle operation instead of using
    /// a stage as an undocumented priority. Fact listeners run in the order shown. Middleware
    /// nests in reverse phase order so its post-<c>next</c> result settles through Prevention,
    /// Transformation, Reaction, and finally Observation.
    /// </remarks>
    public enum RuleLifecyclePhase
    {
        /// <summary>
        /// Rules that can prevent the operation or committed response from proceeding normally.
        /// </summary>
        Prevention,

        /// <summary>
        /// Rules that replace or transform the value produced by ordinary resolution.
        /// </summary>
        Transformation,

        /// <summary>
        /// Rules that perform a rules response, such as offering or resolving a reaction.
        /// </summary>
        Reaction,

        /// <summary>
        /// Rules that observe the settled outcome after rules-changing stages have run.
        /// </summary>
        Observation
    }

    /// <summary>
    /// Continues an operation's middleware chain and returns its typed structural result.
    /// </summary>
    /// <typeparam name="TResult">The successful value type declared by the operation.</typeparam>
    /// <returns>The result produced by the next middleware or the operation's resolver.</returns>
    /// <remarks>
    /// Middleware may invoke this delegate at most once and must await it before returning. It may
    /// omit the call to short-circuit or replace the remaining work. The continuation and any
    /// child dispatched through the callback's <see cref="OpMiddlewareContext"/> must be awaited
    /// sequentially; neither may begin while the other result remains unconsumed.
    /// </remarks>
    public delegate ValueTask<OpResult<TResult>> OpNext<TResult>();

    /// <summary>
    /// Wraps resolution of one concrete operation type for each matching active rule binding.
    /// </summary>
    /// <typeparam name="TOp">The concrete operation type being wrapped.</typeparam>
    /// <typeparam name="TResult">The successful value type declared by the operation.</typeparam>
    /// <remarks>
    /// Middleware receives read-only state and may change rules state only by dispatching nested
    /// operations through its context. It runs before the enclosing operation has settled, so it
    /// may continue, replace, or short-circuit that work.
    /// </remarks>
    public interface IOpMiddleware<TOp, TResult>
        where TOp : IRuleOp<TResult>
    {
        /// <summary>
        /// Wraps the next stage of resolution for one active binding.
        /// </summary>
        /// <param name="frame">The immutable frame for the operation being resolved.</param>
        /// <param name="context">
        /// Read-only rules services, the authorizing binding, and binding-scoped nested dispatch.
        /// </param>
        /// <param name="next">The remaining middleware chain and final resolver.</param>
        /// <returns>The structural operation result to expose to the enclosing stage.</returns>
        ValueTask<OpResult<TResult>> Invoke(
            OpFrame<TOp> frame,
            OpMiddlewareContext context,
            OpNext<TResult> next);
    }

    /// <summary>
    /// Reacts to one committed Fact after the Fact's complete root resolution has finished.
    /// </summary>
    /// <typeparam name="TFact">The committed Fact type observed by the listener.</typeparam>
    /// <remarks>
    /// A listener cannot change or cancel the state transition described by the Fact. It may
    /// dispatch a new, causally linked root operation through <see cref="FactContext"/>. Listener
    /// eligibility is frozen when the Fact's source operation frame begins, then the binding is
    /// checked again immediately before notification. A binding enabled or created by a frame
    /// cannot observe that frame's Facts, while a binding disabled, removed, or changed before
    /// delivery is skipped.
    /// </remarks>
    public interface IFactListener<TFact>
        where TFact : RuleFact
    {
        /// <summary>
        /// Handles one matching committed Fact for one active binding.
        /// </summary>
        /// <param name="fact">The already committed Fact.</param>
        /// <param name="context">
        /// The authorizing binding, post-commit state, trace data, and causal dispatch.
        /// </param>
        /// <returns>A task-like value that completes when the listener and its dispatched work finish.</returns>
        ValueTask OnFactCommitted(
            TFact fact,
            FactContext context);
    }

    /// <summary>
    /// Reacts once to all matching Facts committed by one completed root resolution.
    /// </summary>
    /// <typeparam name="TFact">The committed Fact type grouped for the listener.</typeparam>
    /// <remarks>
    /// A batch contains only the root's matching Facts whose source frames began while the
    /// binding was eligible. The binding must also remain active when delivery begins.
    /// </remarks>
    public interface IFactBatchListener<TFact>
        where TFact : RuleFact
    {
        /// <summary>
        /// Handles the matching Facts from one committed root for one active binding.
        /// </summary>
        /// <param name="batch">A non-empty, root-scoped collection in commit order.</param>
        /// <param name="context">
        /// The authorizing binding, post-commit state, trace data, and causal dispatch.
        /// </param>
        /// <returns>A task-like value that completes when the listener and its dispatched work finish.</returns>
        ValueTask OnFactsCommitted(
            CommittedFactBatch<TFact> batch,
            FactContext context);
    }
}
