using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Game.DungeonPersistence.Repository;
using UnityEngine;

[assembly: InternalsVisibleTo("EditModeAssembly")]
[assembly: InternalsVisibleTo("PlayModeAssembly")]

namespace Game.DungeonPersistence.Actors
{
    /// <summary>Associates a live actor with the stable identities supplied by dungeon runtime.</summary>
    internal sealed class DungeonActorCaptureTarget
    {
        /// <summary>Creates one explicitly identified capture target.</summary>
        /// <param name="controller">The live actor controller.</param>
        /// <param name="instanceId">Stable actor identity within the dungeon run.</param>
        /// <param name="creatureContentId">Stable creature catalog identity.</param>
        public DungeonActorCaptureTarget(
            ActionController controller,
            string instanceId,
            string creatureContentId
        )
        {
            Controller =
                controller != null
                    ? controller
                    : throw new ArgumentNullException(nameof(controller));
            InstanceId = DungeonActorStateAdapter.RequireId(instanceId, nameof(instanceId));
            CreatureContentId = DungeonActorStateAdapter.RequireId(
                creatureContentId,
                nameof(creatureContentId)
            );
        }

        /// <summary>Gets the live actor controller.</summary>
        public ActionController Controller { get; }

        /// <summary>Gets the stable actor identity.</summary>
        public string InstanceId { get; }

        /// <summary>Gets the stable creature catalog identity.</summary>
        public string CreatureContentId { get; }
    }

    /// <summary>Associates a newly materialized live actor with its durable state.</summary>
    internal sealed class DungeonActorRestoreTarget
    {
        /// <summary>Creates one restore target without deriving identity from its GameObject.</summary>
        /// <param name="controller">The newly materialized actor controller.</param>
        /// <param name="state">The complete actor state to restore.</param>
        public DungeonActorRestoreTarget(
            ActionController controller,
            DungeonCreatureSaveState state
        )
        {
            Controller =
                controller != null
                    ? controller
                    : throw new ArgumentNullException(nameof(controller));
            State = state ?? throw new ArgumentNullException(nameof(state));
        }

        /// <summary>Gets the newly materialized actor controller.</summary>
        public ActionController Controller { get; }

        /// <summary>Gets the complete durable actor state.</summary>
        public DungeonCreatureSaveState State { get; }
    }

    /// <summary>
    /// Converts between authoritative dungeon actor DTOs and newly materialized Unity actors.
    /// Combat action points, reaction use, turn authority, and multiple-attack penalty are
    /// deliberately excluded because encounter composition owns those transient values.
    /// </summary>
    internal static partial class DungeonActorStateAdapter
    {
        private const string LegacyEffectStateDiscriminator = "legacy-spell-effect/v1";
        private const string EmptyEffectStateJson = "{}";

        /// <summary>Captures one actor whose timed-effect sources are limited to itself.</summary>
        /// <param name="controller">The live actor controller.</param>
        /// <param name="instanceId">Stable actor identity within the dungeon run.</param>
        /// <param name="creatureContentId">Stable creature catalog identity.</param>
        /// <returns>A complete authoritative actor snapshot.</returns>
        /// <remarks>
        /// Use <see cref="Capture(IEnumerable{DungeonActorCaptureTarget})"/> when any timed effect
        /// can refer to another actor.
        /// </remarks>
        public static DungeonCreatureSaveState Capture(
            ActionController controller,
            string instanceId,
            string creatureContentId
        ) =>
            Capture(
                    new[]
                    {
                        new DungeonActorCaptureTarget(controller, instanceId, creatureContentId),
                    }
                )
                .Single();

        /// <summary>
        /// Captures an actor group in stable-ID order, preserving cross-actor spell and shared
        /// condition-source identity.
        /// </summary>
        /// <param name="targets">Every actor that can participate in a captured reference.</param>
        /// <returns>Snapshots ordered by stable actor identity.</returns>
        public static IReadOnlyList<DungeonCreatureSaveState> Capture(
            IEnumerable<DungeonActorCaptureTarget> targets
        )
        {
            if (targets == null)
                throw new ArgumentNullException(nameof(targets));
            DungeonActorCaptureTarget[] copied = targets.ToArray();
            if (copied.Any(target => target == null))
                throw new ArgumentException(
                    "Capture targets cannot contain null.",
                    nameof(targets)
                );
            if (
                copied.Select(target => target.InstanceId).Distinct(StringComparer.Ordinal).Count()
                != copied.Length
            )
                throw new ArgumentException("Actor instance IDs must be unique.", nameof(targets));
            if (
                copied
                    .Select(target => target.Controller.gameObject)
                    .Distinct(ReferenceEqualityComparer<GameObject>.Instance)
                    .Count() != copied.Length
            )
                throw new ArgumentException(
                    "A live actor cannot be captured under more than one identity.",
                    nameof(targets)
                );

            DungeonActorCaptureTarget[] ordered = copied
                .OrderBy(target => target.InstanceId, StringComparer.Ordinal)
                .ToArray();
            Dictionary<GameObject, string> actorIds = ordered.ToDictionary(
                target => target.Controller.gameObject,
                target => target.InstanceId,
                ReferenceEqualityComparer<GameObject>.Instance
            );
            CaptureContext context = new(actorIds);
            return Array.AsReadOnly(ordered.Select(context.Capture).ToArray());
        }

        /// <summary>Prevalidates a single newly materialized actor before restoring it.</summary>
        /// <param name="controller">The newly materialized actor controller.</param>
        /// <param name="state">The complete actor state to restore.</param>
        /// <returns>A single-use, fully prevalidated restore plan.</returns>
        /// <remarks>Use the grouped overload when effects can refer to a different actor.</remarks>
        public static DungeonActorRestorePlan PreflightRestore(
            ActionController controller,
            DungeonCreatureSaveState state
        ) => PreflightRestore(new[] { new DungeonActorRestoreTarget(controller, state) });

        /// <summary>
        /// Resolves every saved stable actor ID, then prevalidates the complete restore without
        /// mutating live objects.
        /// </summary>
        /// <param name="states">Complete saved actor states.</param>
        /// <param name="resolveController">Stable actor ID to materialized controller resolver.</param>
        /// <returns>A single-use, fully prevalidated restore plan.</returns>
        public static DungeonActorRestorePlan PreflightRestore(
            IEnumerable<DungeonCreatureSaveState> states,
            Func<string, ActionController> resolveController
        )
        {
            if (states == null)
                throw new ArgumentNullException(nameof(states));
            if (resolveController == null)
                throw new ArgumentNullException(nameof(resolveController));
            DungeonCreatureSaveState[] copied = states.ToArray();
            if (copied.Any(state => state == null))
                throw new ArgumentException("Actor states cannot contain null.", nameof(states));
            return PreflightRestore(
                copied.Select(state => new DungeonActorRestoreTarget(
                    resolveController(state.InstanceId),
                    state
                ))
            );
        }

        /// <summary>Prevalidates all materialized actors and cross-actor references.</summary>
        /// <param name="targets">Every actor participating in the restore.</param>
        /// <returns>A single-use restore plan.</returns>
        public static DungeonActorRestorePlan PreflightRestore(
            IEnumerable<DungeonActorRestoreTarget> targets
        )
        {
            if (targets == null)
                throw new ArgumentNullException(nameof(targets));
            DungeonActorRestoreTarget[] copied = targets.ToArray();
            if (copied.Any(target => target == null))
                throw new ArgumentException(
                    "Restore targets cannot contain null.",
                    nameof(targets)
                );
            if (
                copied
                    .Select(target => target.State.InstanceId)
                    .Distinct(StringComparer.Ordinal)
                    .Count() != copied.Length
            )
                throw new ArgumentException("Actor state IDs must be unique.", nameof(targets));
            if (
                copied
                    .Select(target => target.Controller.gameObject)
                    .Distinct(ReferenceEqualityComparer<GameObject>.Instance)
                    .Count() != copied.Length
            )
                throw new ArgumentException(
                    "A live actor cannot restore more than one saved identity.",
                    nameof(targets)
                );

            ValidateForRestore(copied.Select(target => target.State));

            DungeonActorRestoreTarget[] ordered = copied
                .OrderBy(target => target.State.InstanceId, StringComparer.Ordinal)
                .ToArray();
            Dictionary<string, ActionController> controllersById = ordered.ToDictionary(
                target => target.State.InstanceId,
                target => target.Controller,
                StringComparer.Ordinal
            );
            Dictionary<string, ConditionSource> conditionSources = new(StringComparer.Ordinal);
            List<ActorRestorePlan> plans = new();
            foreach (DungeonActorRestoreTarget target in ordered)
                plans.Add(BuildRestorePlan(target, controllersById, conditionSources));
            DungeonActorGridRestorePlan gridPlan = DungeonActorGridRestorePlan.Preflight(ordered);
            return new DungeonActorRestorePlan(plans.AsReadOnly(), gridPlan);
        }

        internal static string RequireId(string value, string parameterName)
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
                throw new ArgumentException("A stable identity is required.", parameterName);
            return normalized;
        }

        private static string NormalizeDefinitionId(string value, string kind)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"A live {kind} has no definition name.");
            return value.Trim().ToLowerInvariant().Replace(' ', '-');
        }

        private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
            where T : class
        {
            internal static readonly ReferenceEqualityComparer<T> Instance = new();

            public bool Equals(T left, T right) => ReferenceEquals(left, right);

            public int GetHashCode(T value) => RuntimeHelpers.GetHashCode(value);
        }
    }
}
