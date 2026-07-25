using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace GridPrivate
{
    [System.Serializable]
    public class Tile
    {
        /// <summary>
        /// Event called upon token entering this tile
        /// </summary>
        public EventCoroutine<(GameObject, Vector3Int)> OnEnterTile { get; protected set; } = new();

        /// <summary>
        /// Event called upon token exiting this tile. Location and prevented exit.
        /// </summary>
        public EventCoroutine<(GameObject, Vector3Int, Ref<bool>)> OnExitTile
        {
            get;
            protected set;
        } = new();

        public List<GameObject> Occupants { get; protected set; } = new();

        public bool IsObstructing { get; protected set; }

        /// <summary>
        /// Returns whether the given moving token may traverse this cell.
        /// </summary>
        /// <param name="token">The token attempting to Stride through the cell.</param>
        /// <returns>
        /// <see langword="true"/> when the cell is empty or the mover considers an occupant
        /// friendly; otherwise, <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// Team relationships are directional. The mover must therefore be the first argument to
        /// <see cref="TeamRules.IsFriendly(string, string)"/>, matching the authoritative Stride
        /// selection request.
        /// </remarks>
        public bool CanStrideOn(GameObject token)
        {
            if (Occupants.Count == 0)
                return true;
            foreach (GameObject occupant in Occupants)
            {
                Team team = occupant.GetComponent<Team>();
                Team team2 = token.GetComponent<Team>();
                if (team && team2 && TeamRules.GetInstance().IsFriendly(team2.Name, team.Name))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Places a token on this tile
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public IEnumerator PlaceToken(GameObject token)
        {
            Occupants.Add(token);
            yield return OnEnterTile.Invoke(
                (token, Vector3Int.RoundToInt(token.transform.position))
            );
        }

        /// <summary>
        /// Attempts to remove a token from this tile
        /// </summary>
        /// <param name="token"></param>
        /// <param name="prevented"></param>
        /// <returns></returns>
        public IEnumerator RemoveToken(GameObject token, Ref<bool> prevented)
        {
            // Viva RUST, the most vastly superior programming language with good formatting
            yield return OnExitTile.Invoke(
                (token, Vector3Int.RoundToInt(token.transform.position), prevented)
            );
            if (!prevented.Value)
                Occupants.Remove(token);
        }

        /// <summary>Updates projected occupancy for an already-committed departure.</summary>
        /// <param name="token">The token whose authoritative position has changed.</param>
        /// <returns>Whether the token was present in this projected cell.</returns>
        internal bool ProjectCommittedDeparture(GameObject token) => Occupants.Remove(token);

        /// <summary>Updates projected occupancy for an already-committed arrival.</summary>
        /// <param name="token">The token whose authoritative position has changed.</param>
        internal void ProjectCommittedArrival(GameObject token)
        {
            if (!Occupants.Contains(token))
                Occupants.Add(token);
        }
    }
}
