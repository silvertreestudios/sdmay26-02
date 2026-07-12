using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using GridPrivate;
using GridPublic;
using UnityEngine;

namespace Game.Creature.Rules
{
    public static class CreatureAuraArea
    {
        public static AreaTargetResult EvaluateEmanation(GameObject sourceObject, CreatureAura aura, Tile[,] tiles)
        {
            if (sourceObject == null || aura == null || aura.radiusFeet <= 0)
                return null;

            AreaTargetRequest request = new()
            {
                Shape = AreaShape.Emanation,
                SizeFeet = aura.radiusFeet,
                IncludeCenter = true,
                RequiresLineOfEffect = true
            };
            AreaPlacement placement = new()
            {
                Shape = AreaShape.Emanation,
                OriginCell = Vector3Int.RoundToInt(sourceObject.transform.position)
            };
            return AreaTargeting.Evaluate(sourceObject, tiles, request, placement);
        }

        public static bool AffectsCreature(AreaTargetResult area, GameObject targetObject)
        {
            if (area == null || targetObject == null)
                return false;

            return area.Creatures.Any(creature => creature.Creature == targetObject && creature.IsAffected);
        }

        public static List<Vector3Int> GetCells(IEnumerable<CreatureAuraInstance> auraInstances, Tile[,] tiles)
        {
            HashSet<Vector3Int> cells = new();
            if (auraInstances == null || tiles == null)
                return new List<Vector3Int>();

            foreach (CreatureAuraInstance instance in auraInstances)
            {
                AreaTargetResult result = EvaluateEmanation(instance.SourceObject, instance.Aura, tiles);
                if (result == null)
                    continue;

                foreach (Vector3Int cell in result.Cells)
                    cells.Add(cell);
            }

            return cells.ToList();
        }
    }
}
