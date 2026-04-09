using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GridPrivate
{
    public interface IPathfinder
    {
        /// <summary>
        /// Pathfinds from start to end. Call Search and find if many calls may be needed
        /// </summary>
        /// <param name="pathfinder">the token pathfinding. Can be null.</param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <returns></returns>
        public List<PathNode> Pathfind(GameObject pathfinder, Vector3Int start, Vector3Int end);

        /// <summary>
        /// Runs Dijkstras from the given start point for rapid querying
        /// </summary>
        /// <param name="pathfinder">the token pathfinding. Can be null.</param>
        /// <param name="start">of search</param>
        public void Search(GameObject pathfinder, Vector3Int start);

        /// <summary>
        /// Returns the list of PathNodes to a given point
        /// Only use after Search
        /// </summary>
        /// <param name="end"></param>
        /// <returns>null if Search was not called prior or if not found</returns>
        public List<PathNode> Find(Vector3Int end);

        /// <summary>
        /// Returns the list of locations in range from position
        /// Only use after Search
        /// </summary>
        /// <param name="pathfinder">the token pathfinding. Can be null.</param>
        /// <param name="start"></param>
        /// <param name="range"></param>
        /// <returns></returns>
        public List<Vector3Int> InRange(GameObject pathfinder, Vector3Int start, float range);
    }


    /// <summary>
    /// Class representing a path
    /// </summary>
    public class PathNode
    {
        public float Dist;
        public PathNode Prev;
        public Vector3Int Location;

        public PathNode(float dist, Vector3Int location)
        {
            Dist = dist;
            Prev = null;
            Location = location;
        }

        public PathNode(float dist, Vector3Int location, PathNode prev)
        {
            Dist = dist;
            Prev = prev;
            Location = location;
        }

        public static bool Cmp(PathNode t1, PathNode t2)
        {
            return t1.Dist < t2.Dist;
        }

        public static bool Eq(PathNode t1, PathNode t2)
        {
            return t1.Location == t2.Location;
        }
    }
}
