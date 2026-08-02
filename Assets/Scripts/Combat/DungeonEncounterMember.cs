using System;
using UnityEngine;

namespace Game.Combat.Encounters
{
    /// <summary>Constructs floor-scoped durable identities for generated dungeon enemies.</summary>
    internal static class DungeonEnemyDurableActorIdentity
    {
        internal const string ReservedPrefix = "dungeon-enemy-v1/";

        /// <summary>Constructs the exact versioned identity for one floor-local enemy instance.</summary>
        internal static string Create(int floorDepth, string instanceId)
        {
            if (floorDepth < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(floorDepth),
                    "Dungeon enemy floor depth cannot be negative."
                );
            if (
                string.IsNullOrWhiteSpace(instanceId)
                || !string.Equals(instanceId, instanceId.Trim(), StringComparison.Ordinal)
            )
                throw new ArgumentException(
                    "A dungeon enemy instance ID must be nonempty and canonical.",
                    nameof(instanceId)
                );
            return $"{ReservedPrefix}{floorDepth}/{instanceId}";
        }

        /// <summary>Determines whether an identity occupies the enemy-only durable namespace.</summary>
        internal static bool IsReserved(string durableActorId) =>
            durableActorId != null
            && durableActorId.StartsWith(ReservedPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Identifies one materialized creature as a stable member of a generated room encounter.
    /// </summary>
    /// <remarks>
    /// Identity is assigned once by <see cref="DungeonEncounterMaterializer"/>. Reconfiguration is
    /// rejected so persistence and defeat notifications cannot silently move an instance between
    /// encounter groups.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class DungeonEncounterMember : MonoBehaviour
    {
        [SerializeField]
        private string encounterId = string.Empty;

        [SerializeField]
        private string instanceId = string.Empty;

        [SerializeField]
        private int floorDepth = -1;

        [SerializeField]
        private string creatureContentId = string.Empty;

        [SerializeField]
        private string persistentState = string.Empty;

        [SerializeField]
        private bool isConfigured;

        [SerializeField]
        private bool defeatWasReported;

        /// <summary>
        /// Raised once when the owning creature reports its defeat to the encounter lifecycle.
        /// </summary>
        public event Action<DungeonEncounterMember> Defeated = delegate { };

        /// <summary>Gets whether stable identity has already been assigned.</summary>
        public bool IsConfigured => isConfigured;

        /// <summary>Gets the stable encounter group ID, or an empty string before configuration.</summary>
        public string EncounterId => encounterId;

        /// <summary>Gets the stable plan-derived creature instance ID, or an empty string before configuration.</summary>
        public string InstanceId => instanceId;

        /// <summary>Gets the dungeon floor depth, or <c>-1</c> before configuration.</summary>
        public int FloorDepth => floorDepth;

        /// <summary>
        /// Gets the canonical floor-scoped durable actor identity, or an empty string before
        /// configuration.
        /// </summary>
        public string DurableActorId =>
            isConfigured
                ? DungeonEnemyDurableActorIdentity.Create(floorDepth, instanceId)
                : string.Empty;

        /// <summary>Gets the immutable creature content ID, or an empty string before configuration.</summary>
        public string CreatureContentId => creatureContentId;

        /// <summary>Gets the opaque child-persistence token restored for this instance.</summary>
        public string PersistentState => persistentState;

        /// <summary>Gets whether this instance has already reported its permanent defeat.</summary>
        public bool DefeatWasReported => defeatWasReported;

        /// <summary>Assigns stable encounter identity exactly once.</summary>
        /// <param name="encounterId">The non-empty canonical encounter group ID.</param>
        /// <param name="instanceId">The non-empty canonical instance ID derived from plan order.</param>
        /// <param name="floorDepth">The nonnegative dungeon floor that owns the instance.</param>
        /// <param name="creatureContentId">The non-empty canonical creature content ID from the plan.</param>
        /// <param name="persistentState">The losslessly preserved child-state token, or an empty string.</param>
        /// <exception cref="ArgumentException">Any identity value is blank or noncanonical.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="floorDepth"/> is negative.</exception>
        /// <exception cref="InvalidOperationException">This member was already configured.</exception>
        public void Configure(
            string encounterId,
            string instanceId,
            int floorDepth,
            string creatureContentId,
            string persistentState
        )
        {
            if (isConfigured)
                throw new InvalidOperationException(
                    $"Dungeon encounter member '{name}' is already configured as '{this.instanceId}'."
                );
            if (
                string.IsNullOrWhiteSpace(encounterId)
                || !string.Equals(encounterId, encounterId.Trim(), StringComparison.Ordinal)
            )
                throw new ArgumentException(
                    "An encounter ID must be nonempty and canonical.",
                    nameof(encounterId)
                );
            if (
                string.IsNullOrWhiteSpace(instanceId)
                || !string.Equals(instanceId, instanceId.Trim(), StringComparison.Ordinal)
            )
                throw new ArgumentException(
                    "A creature instance ID must be nonempty and canonical.",
                    nameof(instanceId)
                );
            if (floorDepth < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(floorDepth),
                    "Dungeon encounter floor depth cannot be negative."
                );
            if (
                string.IsNullOrWhiteSpace(creatureContentId)
                || !string.Equals(
                    creatureContentId,
                    creatureContentId.Trim(),
                    StringComparison.Ordinal
                )
            )
                throw new ArgumentException(
                    "A creature content ID must be nonempty and canonical.",
                    nameof(creatureContentId)
                );

            this.encounterId = encounterId;
            this.instanceId = instanceId;
            this.floorDepth = floorDepth;
            this.creatureContentId = creatureContentId;
            this.persistentState = persistentState ?? string.Empty;
            isConfigured = true;
        }

        /// <summary>Reports this configured creature's permanent defeat exactly once.</summary>
        /// <remarks>
        /// <see cref="Game.Creature.CreatureComponent"/> calls this narrow instance event before
        /// disabling the object. Subscribers therefore do not need another global death event.
        /// Repeated calls are harmless because creature defeat is a permanent fact.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Stable encounter identity is not configured.</exception>
        public void ReportDefeated()
        {
            if (!isConfigured)
                throw new InvalidOperationException(
                    $"Dungeon encounter member '{name}' cannot report defeat before configuration."
                );
            if (defeatWasReported)
                return;

            defeatWasReported = true;
            Defeated.Invoke(this);
        }
    }
}
