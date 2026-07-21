using System;
using UnityEngine;

namespace Game.DungeonPersistence.Actors
{
    /// <summary>
    /// Carries stable identities assigned while materializing a dungeon party member. Values
    /// never fall back to mutable names or Unity instance IDs.
    /// </summary>
    public sealed class DungeonPartyMemberIdentity : MonoBehaviour
    {
        [SerializeField]
        private string rosterSlotId = string.Empty;

        [SerializeField]
        private string actorInstanceId = string.Empty;

        [SerializeField]
        private string creatureContentId = string.Empty;

        /// <summary>Gets the stable roster-slot or character-save identifier.</summary>
        public string RosterSlotId => rosterSlotId;

        /// <summary>Gets the stable actor instance identifier used by cross-actor references.</summary>
        public string ActorInstanceId => actorInstanceId;

        /// <summary>Gets the stable creature catalog identifier.</summary>
        public string CreatureContentId => creatureContentId;

        /// <summary>Gets whether all three stable identities were assigned.</summary>
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(rosterSlotId)
            && !string.IsNullOrWhiteSpace(actorInstanceId)
            && !string.IsNullOrWhiteSpace(creatureContentId);

        /// <summary>Assigns stable identities exactly once while the actor is materialized.</summary>
        /// <param name="rosterSlotId">Stable roster-slot or character-save identifier.</param>
        /// <param name="actorInstanceId">Stable actor identity within the dungeon run.</param>
        /// <param name="creatureContentId">Stable creature catalog identity.</param>
        public void Configure(string rosterSlotId, string actorInstanceId, string creatureContentId)
        {
            if (IsConfigured)
                throw new InvalidOperationException(
                    "Dungeon party identity is already configured."
                );

            string validatedRosterSlotId = RequireId(rosterSlotId, nameof(rosterSlotId));
            string validatedActorInstanceId = RequireId(actorInstanceId, nameof(actorInstanceId));
            string validatedCreatureContentId = RequireId(
                creatureContentId,
                nameof(creatureContentId)
            );

            this.rosterSlotId = validatedRosterSlotId;
            this.actorInstanceId = validatedActorInstanceId;
            this.creatureContentId = validatedCreatureContentId;
        }

        private static string RequireId(string value, string parameterName)
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
                throw new ArgumentException("A stable identity is required.", parameterName);
            return normalized;
        }
    }
}
