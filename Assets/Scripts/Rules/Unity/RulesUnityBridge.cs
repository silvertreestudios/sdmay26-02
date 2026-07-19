using System;
using Game.Rules.Runtime;
using UnityEngine;

namespace Game.Rules.Unity
{
    /// <summary>
    /// Connects an encounter rules runtime to queued Unity presentation beside <see cref="GameManager"/>.
    /// </summary>
    /// <remarks>
    /// The runtime callback only enqueues immutable data. Unity presenters run later from
    /// <see cref="Update"/>, so animation, HUD, audio, and combat-log work cannot delay or decide
    /// rules commitment. This component owns no authoritative rules state.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RulesUnityBridge : MonoBehaviour
    {
        private readonly RulesFactPresentationQueue queue = new RulesFactPresentationQueue();
        private BridgeConfiguration configuration = UnconfiguredBridge.Instance;
        private bool isSubscribed;

        /// <summary>
        /// Gets whether this component has received its required encounter runtime and presenters.
        /// </summary>
        public bool IsConfigured => configuration.IsConfigured;

        /// <summary>
        /// Gets the number of committed Facts waiting for Unity presentation.
        /// </summary>
        public int PendingPresentationCount => queue.Count;

        /// <summary>
        /// Configures the bridge exactly once with encounter-scoped dependencies.
        /// </summary>
        /// <param name="runtime">The required rules runtime instance.</param>
        /// <param name="presentation">The required presentation coordinator for that runtime.</param>
        /// <exception cref="ArgumentNullException">Either dependency is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">The bridge was already configured.</exception>
        public void Configure(
            IRulesRuntime runtime,
            RulesFactPresentation presentation)
        {
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));
            if (presentation == null)
                throw new ArgumentNullException(nameof(presentation));
            if (configuration.IsConfigured)
                throw new InvalidOperationException("A rules Unity bridge cannot be reconfigured.");

            configuration = new ConfiguredBridge(runtime, presentation);
            if (isActiveAndEnabled)
                Subscribe();
        }

        /// <summary>
        /// Immediately drains queued Facts through the configured presenters.
        /// </summary>
        /// <returns>The number of queued envelopes consumed, or zero before configuration.</returns>
        /// <remarks>
        /// Production code normally relies on <see cref="Update"/>. This explicit seam supports
        /// deterministic lifecycle tests and controlled hosts that own their own update loop.
        /// </remarks>
        public int DrainPresentationQueue()
        {
            if (!configuration.IsConfigured)
                return 0;
            return queue.Drain(configuration.Presentation);
        }

        private void OnEnable() => Subscribe();

        private void OnDisable() => Unsubscribe();

        private void OnDestroy() => Unsubscribe();

        private void Update() => DrainPresentationQueue();

        private void Subscribe()
        {
            if (!configuration.IsConfigured || isSubscribed)
                return;
            configuration.Runtime.FactCommitted += QueueCommittedFact;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
                return;
            configuration.Runtime.FactCommitted -= QueueCommittedFact;
            isSubscribed = false;
        }

        private void QueueCommittedFact(CommittedRuleFact commit) => queue.Enqueue(commit);

        private abstract class BridgeConfiguration
        {
            public abstract bool IsConfigured { get; }

            public virtual IRulesRuntime Runtime => throw new InvalidOperationException(
                "The rules Unity bridge has not been configured.");

            public virtual RulesFactPresentation Presentation => throw new InvalidOperationException(
                "The rules Unity bridge has not been configured.");
        }

        private sealed class UnconfiguredBridge : BridgeConfiguration
        {
            public static UnconfiguredBridge Instance { get; } = new UnconfiguredBridge();

            public override bool IsConfigured => false;

            private UnconfiguredBridge()
            {
            }
        }

        private sealed class ConfiguredBridge : BridgeConfiguration
        {
            private readonly IRulesRuntime runtime;
            private readonly RulesFactPresentation presentation;

            public ConfiguredBridge(
                IRulesRuntime runtime,
                RulesFactPresentation presentation)
            {
                this.runtime = runtime;
                this.presentation = presentation;
            }

            public override bool IsConfigured => true;
            public override IRulesRuntime Runtime => runtime;
            public override RulesFactPresentation Presentation => presentation;
        }
    }
}
