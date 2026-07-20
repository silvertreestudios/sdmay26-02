using System;
using UnityEngine;

namespace Game.Combat.Encounters
{
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

        /// <summary>Gets the immutable creature content ID, or an empty string before configuration.</summary>
        public string CreatureContentId => creatureContentId;

        /// <summary>Gets the opaque child-persistence token restored for this instance.</summary>
        public string PersistentState => persistentState;

        /// <summary>Gets whether this instance has already reported its permanent defeat.</summary>
        public bool DefeatWasReported => defeatWasReported;

        /// <summary>Assigns stable encounter identity exactly once.</summary>
        /// <param name="encounterId">The non-empty encounter group ID.</param>
        /// <param name="instanceId">The non-empty instance ID derived from plan order.</param>
        /// <param name="creatureContentId">The non-empty creature content ID from the plan.</param>
        /// <param name="persistentState">The losslessly preserved child-state token, or an empty string.</param>
        /// <exception cref="ArgumentException">Any identity value is blank.</exception>
        /// <exception cref="InvalidOperationException">This member was already configured.</exception>
        public void Configure(
            string encounterId,
            string instanceId,
            string creatureContentId,
            string persistentState
        )
        {
            if (isConfigured)
                throw new InvalidOperationException(
                    $"Dungeon encounter member '{name}' is already configured as '{this.instanceId}'."
                );
            if (string.IsNullOrWhiteSpace(encounterId))
                throw new ArgumentException("An encounter ID is required.", nameof(encounterId));
            if (string.IsNullOrWhiteSpace(instanceId))
                throw new ArgumentException(
                    "A creature instance ID is required.",
                    nameof(instanceId)
                );
            if (string.IsNullOrWhiteSpace(creatureContentId))
                throw new ArgumentException(
                    "A creature content ID is required.",
                    nameof(creatureContentId)
                );

            this.encounterId = encounterId;
            this.instanceId = instanceId;
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
