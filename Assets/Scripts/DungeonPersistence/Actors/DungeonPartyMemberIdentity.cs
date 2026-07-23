using System;
using UnityEngine;

namespace Game.DungeonPersistence.Actors
{
    /// <summary>
    /// Assigns one authored party prefab a stable roster slot and creature content identity.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DungeonPartyMemberIdentity : MonoBehaviour
    {
        [SerializeField]
        private string rosterSlotId = string.Empty;

        [SerializeField]
        private string creatureContentId = string.Empty;

        /// <summary>Gets the stable roster slot used for party saves and actor references.</summary>
        public string RosterSlotId => rosterSlotId;

        /// <summary>Gets the immutable creature content identifier.</summary>
        public string CreatureContentId => creatureContentId;

        /// <summary>Gets whether both authored identifiers are present.</summary>
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(rosterSlotId)
            && !string.IsNullOrWhiteSpace(creatureContentId);

        /// <summary>Assigns both identifiers exactly once for runtime-created test fixtures.</summary>
        /// <param name="rosterSlotId">Stable roster slot and cross-actor identity.</param>
        /// <param name="creatureContentId">Stable creature content identifier.</param>
        public void Configure(string rosterSlotId, string creatureContentId)
        {
            if (IsConfigured)
                throw new InvalidOperationException(
                    "Dungeon party identity is already configured."
                );
            this.rosterSlotId = RequireId(rosterSlotId, nameof(rosterSlotId));
            this.creatureContentId = RequireId(creatureContentId, nameof(creatureContentId));
        }

        private static string RequireId(string value, string parameterName)
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
                throw new ArgumentException("A stable identifier is required.", parameterName);
            return normalized;
        }
    }
}
