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
        public EventCoroutine<(GameObject, Vector3Int, Ref<bool>)> OnExitTile { get; protected set; } = new();

        public List<GameObject> Occupants { get; protected set; } = new();

        public bool IsObstructing { get; protected set; }

        /// <summary>
        /// Returns true if the given token can stride
        /// on this cell
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public bool CanStrideOn(GameObject token)
        {
            if (Occupants.Count == 0)
                return true;
            foreach (GameObject occupant in Occupants)
            {
                Team team = occupant.GetComponent<Team>();
                Team team2 = token.GetComponent<Team>();
                if (team && team2 && TeamRules.GetInstance().IsFriendly(team.Name, team2.Name))
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
            yield return OnEnterTile.Invoke((
                token, 
                Vector3Int.RoundToInt(token.transform.position)
            ));
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
            yield return OnExitTile.Invoke((
                token, 
                Vector3Int.RoundToInt(token.transform.position),
                prevented
            ));
            if(!prevented.Value)
                Occupants.Remove(token);
        }
    }
}
