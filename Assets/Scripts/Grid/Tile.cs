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
        public UnityEvent<GameObject, Vector3Int> OnEnterTile { get; protected set; } = new();

        /// <summary>
        /// Event called upon token exiting this tile
        /// </summary>
        public UnityEvent<GameObject, Vector3Int> OnExitTile { get; protected set; } = new();

        public GameObject Occupant { get; set; }

        public bool IsObstructing { get; protected set; }

        /// <summary>
        /// Returns true if the given token can stride
        /// on this cell
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public bool CanStrideOn(GameObject token)
        {
            if (Occupant == null)
                return true;
            Team team = Occupant.GetComponent<Team>();
            Team team2 = token.GetComponent<Team>();
            if (team && team2 && TeamRules.GetInstance().IsFriendly(team.Name, team2.Name))
                return true;
            return false;
        }
    }
}
