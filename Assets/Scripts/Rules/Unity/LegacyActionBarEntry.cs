using System;

namespace Game.Rules.Unity
{
    /// <summary>
    /// Keeps an existing <see cref="EntityAction"/> explicit while it remains owned by an
    /// <see cref="ActionController"/>.
    /// </summary>
    /// <remarks>
    /// This compatibility entry deliberately exposes no rules operation or dispatch result. Calling
    /// <see cref="Invoke"/> follows the legacy controller path, including its existing turn and cost checks.
    /// </remarks>
    public sealed class LegacyActionBarEntry : IActionBarEntry
    {
        private readonly ActionController controller;

        /// <summary>
        /// Initializes a legacy entry without adapting the action into a rules operation.
        /// </summary>
        /// <param name="key">The explicit stable key a replacement definition may share.</param>
        /// <param name="action">The existing legacy action instance.</param>
        /// <param name="controller">The legacy controller that owns invocation and availability checks.</param>
        /// <exception cref="ArgumentException"><paramref name="key"/> is uninitialized or the action name is blank.</exception>
        /// <exception cref="ArgumentNullException">A required Unity or action reference is <see langword="null"/>.</exception>
        public LegacyActionBarEntry(
            ActionBarEntryKey key,
            EntityAction action,
            ActionController controller
        )
        {
            if (key.IsEmpty)
                throw new ArgumentException("A legacy entry requires a stable key.", nameof(key));
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));
            if (string.IsNullOrWhiteSpace(action.ActionName))
                throw new ArgumentException(
                    "A legacy action requires a display name.",
                    nameof(action)
                );

            Key = key;
            LegacyAction = action;
            this.controller = controller;
        }

        /// <inheritdoc/>
        public ActionBarEntryKey Key { get; }

        /// <inheritdoc/>
        public string DisplayName => LegacyAction.ActionName;

        /// <summary>
        /// Gets the unchanged legacy action represented by this entry.
        /// </summary>
        public EntityAction LegacyAction { get; }

        /// <summary>
        /// Invokes the action through its owning legacy controller.
        /// </summary>
        public void Invoke() => controller.TakeAction(LegacyAction);
    }
}
