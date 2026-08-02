using System;
using Game.Combat.Encounters;
using UnityEngine;

namespace Game.DungeonPersistence.Actors
{
    /// <summary>Gives an authored party prefab stable save and content identifiers.</summary>
    [DisallowMultipleComponent]
    public sealed class DungeonPartyMemberIdentity : MonoBehaviour
    {
        [SerializeField]
        private string rosterSlotId = string.Empty;

        [SerializeField]
        private string creatureContentId = string.Empty;

        internal string RosterSlotId => rosterSlotId;
        internal string CreatureContentId => creatureContentId;
        internal bool IsConfigured =>
            !string.IsNullOrWhiteSpace(rosterSlotId)
            && !string.IsNullOrWhiteSpace(creatureContentId);

        /// <summary>
        /// Configures a runtime-created party actor with the same stable identity contract used
        /// by authored prefabs. Identity is immutable after the first successful call.
        /// </summary>
        /// <param name="rosterId">
        /// The non-empty canonical stable slot within the player party. Enemy-reserved identities
        /// are not valid party slots.
        /// </param>
        /// <param name="contentId">The non-empty canonical creature content identifier.</param>
        /// <exception cref="InvalidOperationException">The component is already configured.</exception>
        /// <exception cref="ArgumentException">
        /// Either identifier is blank or noncanonical, or <paramref name="rosterId"/> occupies the
        /// enemy-only durable actor namespace.
        /// </exception>
        public void Configure(string rosterId, string contentId)
        {
            if (IsConfigured)
                throw new InvalidOperationException(
                    "Dungeon party identity is already configured."
                );
            if (
                string.IsNullOrWhiteSpace(rosterId)
                || string.IsNullOrWhiteSpace(contentId)
                || !string.Equals(rosterId, rosterId.Trim(), StringComparison.Ordinal)
                || !string.Equals(contentId, contentId.Trim(), StringComparison.Ordinal)
                || DungeonEnemyDurableActorIdentity.IsReserved(rosterId)
            )
                throw new ArgumentException(
                    "Dungeon party identifiers must be nonempty, canonical, and outside the enemy-only durable actor namespace."
                );
            rosterSlotId = rosterId;
            creatureContentId = contentId;
        }
    }
}
