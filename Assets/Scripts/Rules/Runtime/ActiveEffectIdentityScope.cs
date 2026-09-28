using System;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Namespaces newly generated active-effect and effect-binding identities for one rules host.
    /// </summary>
    /// <remarks>
    /// Restored identities bypass this factory and retain their exact saved values. A production
    /// host should use <see cref="CreateUnique"/> so effects created by independent dispatchers can
    /// coexist in one persistence graph. Tests may supply a stable scope value for deterministic
    /// assertions.
    /// </remarks>
    public sealed class ActiveEffectIdentityScope
    {
        private readonly string scope;

        /// <summary>Creates a deterministic namespace from a required stable value.</summary>
        /// <param name="scope">The namespace shared by one rules host and its enrollment path.</param>
        /// <exception cref="ArgumentException"><paramref name="scope"/> is blank.</exception>
        public ActiveEffectIdentityScope(string scope) =>
            this.scope = StableId.Require(scope, nameof(scope));

        /// <summary>Creates a process-independent unique namespace for a production rules host.</summary>
        /// <returns>A new namespace suitable for identities that may be persisted together.</returns>
        public static ActiveEffectIdentityScope CreateUnique() =>
            new ActiveEffectIdentityScope(Guid.NewGuid().ToString("N"));

        /// <summary>Creates one paired effect and binding identity inside this namespace.</summary>
        /// <param name="kind">A stable feature-agnostic discriminator for the creation path.</param>
        /// <param name="localIdentity">A value unique within that path and rules host.</param>
        /// <returns>Distinct typed identities carrying the same scoped application key.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="kind"/> or <paramref name="localIdentity"/> is blank.
        /// </exception>
        public (ActiveEffectId EffectId, BindingId BindingId) Create(
            string kind,
            string localIdentity
        )
        {
            string requiredKind = StableId.Require(kind, nameof(kind));
            string requiredLocalIdentity = StableId.Require(localIdentity, nameof(localIdentity));
            string key = $"{scope}:{requiredKind}:{requiredLocalIdentity}";
            return (
                new ActiveEffectId($"active-effect:{key}"),
                new BindingId($"effect-binding:{key}")
            );
        }
    }
}
