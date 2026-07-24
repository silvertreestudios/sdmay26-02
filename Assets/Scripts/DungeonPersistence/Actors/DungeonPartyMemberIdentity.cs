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

        internal void Configure(string rosterId, string contentId)
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
