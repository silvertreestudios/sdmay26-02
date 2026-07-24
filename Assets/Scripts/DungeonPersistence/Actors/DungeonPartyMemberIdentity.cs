using System;
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
        /// <param name="rosterId">The non-empty stable slot within the player party.</param>
        /// <param name="contentId">The non-empty creature content identifier.</param>
        /// <exception cref="InvalidOperationException">The component is already configured.</exception>
        /// <exception cref="ArgumentException">Either identifier is blank.</exception>
        public void Configure(string rosterId, string contentId)
        {
            if (IsConfigured)
                throw new InvalidOperationException(
                    "Dungeon party identity is already configured."
                );
            if (string.IsNullOrWhiteSpace(rosterId) || string.IsNullOrWhiteSpace(contentId))
                throw new ArgumentException("Dungeon party identifiers are required.");
            rosterSlotId = rosterId.Trim();
            creatureContentId = contentId.Trim();
        }
    }
}
