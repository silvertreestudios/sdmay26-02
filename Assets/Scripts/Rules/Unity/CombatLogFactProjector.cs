using System;
using System.Collections.Generic;
using Game.Rules.Runtime;

namespace Game.Rules.Unity
{
    /// <summary>
    /// Converts one concrete committed Fact into a structured combat-log entry.
    /// </summary>
    /// <typeparam name="TFact">The exact Fact type converted by the projector.</typeparam>
    public interface ICombatLogFactProjector<TFact>
        where TFact : RuleFact
    {
        /// <summary>
        /// Creates the structured log entry for an already committed Fact.
        /// </summary>
        /// <param name="fact">The typed committed Fact.</param>
        /// <param name="commit">The commit envelope and immutable snapshots.</param>
        /// <returns>A new structured entry; the projector must not return <see langword="null"/>.</returns>
        CombatLogEntry Project(TFact fact, CommittedRuleFact commit);
    }

    /// <summary>
    /// Selects typed combat-log projectors without operation-name or feature-ID branching.
    /// </summary>
    public sealed class CombatLogFactProjector
    {
        private static readonly IReadOnlyList<CombatLogEntry> NoEntries =
            Array.AsReadOnly(Array.Empty<CombatLogEntry>());
        private readonly Dictionary<Type, IProjectorRegistration> registrations =
            new Dictionary<Type, IProjectorRegistration>();

        /// <summary>
        /// Registers the single structured-log projector for an exact Fact type.
        /// </summary>
        /// <typeparam name="TFact">The Fact type converted by <paramref name="projector"/>.</typeparam>
        /// <param name="projector">The required typed projector.</param>
        /// <returns>This registry so composition can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="projector"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">The Fact type already has a projector.</exception>
        public CombatLogFactProjector Register<TFact>(
            ICombatLogFactProjector<TFact> projector)
            where TFact : RuleFact
        {
            if (projector == null)
                throw new ArgumentNullException(nameof(projector));
            if (registrations.ContainsKey(typeof(TFact)))
            {
                throw new InvalidOperationException(
                    $"{typeof(TFact).Name} already has a combat-log projector.");
            }
            registrations.Add(typeof(TFact), new ProjectorRegistration<TFact>(projector));
            return this;
        }

        /// <summary>
        /// Projects a committed Fact when its exact type has a registered projector.
        /// </summary>
        /// <param name="commit">The committed Fact and snapshot pair.</param>
        /// <returns>An empty collection or one structured log entry.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="commit"/> is <see langword="null"/>.</exception>
        public IReadOnlyList<CombatLogEntry> Project(CommittedRuleFact commit)
        {
            if (commit == null)
                throw new ArgumentNullException(nameof(commit));
            if (!registrations.TryGetValue(
                commit.Fact.GetType(),
                out IProjectorRegistration registration))
            {
                return NoEntries;
            }
            return Array.AsReadOnly(new[] { registration.Project(commit) });
        }

        private interface IProjectorRegistration
        {
            CombatLogEntry Project(CommittedRuleFact commit);
        }

        private sealed class ProjectorRegistration<TFact> : IProjectorRegistration
            where TFact : RuleFact
        {
            private readonly ICombatLogFactProjector<TFact> projector;

            public ProjectorRegistration(ICombatLogFactProjector<TFact> projector) =>
                this.projector = projector;

            public CombatLogEntry Project(CommittedRuleFact commit)
            {
                if (!(commit.Fact is TFact fact))
                {
                    throw new InvalidOperationException(
                        "A combat-log projector received an incompatible registration.");
                }
                return projector.Project(fact, commit) ?? throw new InvalidOperationException(
                    $"The {typeof(TFact).Name} combat-log projector returned null.");
            }
        }
    }

    /// <summary>
    /// Receives structured combat-log entries independently of UI Toolkit lifecycle details.
    /// </summary>
    public interface ICombatLogSink
    {
        /// <summary>
        /// Appends one structured player-facing combat-log entry.
        /// </summary>
        /// <param name="entry">The required structured entry.</param>
        void Log(CombatLogEntry entry);
    }
}
