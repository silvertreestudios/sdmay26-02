using System;
using System.Linq;
using Game.DungeonGeneration;
using Game.DungeonPersistence.Repository;

namespace Game.DungeonPersistence.Floors
{
    /// <summary>
    /// Projects one validated repository floor into the generated-level document consumed by map
    /// population and encounter restoration.
    /// </summary>
    public static class DungeonFloorSaveProjector
    {
        /// <summary>Reconstructs a population document without mutating the saved static topology.</summary>
        /// <param name="floor">The complete versioned floor state loaded by a repository.</param>
        /// <returns>
        /// A reparsed generated-level document whose doors, encounter flags, defeated IDs, living
        /// actor cells, health, and canonical actor tokens exactly reflect the save.
        /// </returns>
        /// <remarks>
        /// Active and suspended fights intentionally share the generated JSON representation. The
        /// encounter runtime normalizes either unfinished state to suspended exploration and begins
        /// a fresh initiative round when the party reaches it.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="floor"/> is absent.</exception>
        /// <exception cref="InvalidOperationException">
        /// Static topology cannot be parsed or does not match the floor depth.
        /// </exception>
        public static DungeonLevelDocument ProjectForPopulation(DungeonFloorSaveState floor)
        {
            if (floor == null)
                throw new ArgumentNullException(nameof(floor));

            DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(floor.StaticFloorJson);
            if (!parsed.IsSuccess)
            {
                throw new InvalidOperationException(
                    "Saved static floor JSON could not be parsed: "
                        + string.Join(" ", parsed.Diagnostics.Select(item => item.Message))
                );
            }
            if (parsed.Document.Generation.Depth != floor.Depth)
            {
                throw new InvalidOperationException(
                    "Saved static floor depth does not match its mutable floor document."
                );
            }

            DungeonRuntimeState runtime = new(
                floor.Doors.Where(door => door.IsOpen).Select(door => door.DoorId),
                floor
                    .Encounters.Where(encounter =>
                        encounter.Status == DungeonEncounterSaveStatus.Cleared
                    )
                    .Select(encounter => encounter.EncounterId),
                floor
                    .Creatures.Where(creature => creature.Creature.IsDefeated)
                    .Select(creature => creature.Creature.InstanceId),
                floor
                    .Creatures.Where(creature => !creature.Creature.IsDefeated)
                    .Select(creature => new DungeonCreatureRuntimeState(
                        creature.Creature.InstanceId,
                        creature.Creature.CreatureContentId,
                        creature.EncounterId,
                        new DungeonCell(creature.Creature.Cell.X, creature.Creature.Cell.Z),
                        creature.Creature.Health.CurrentHitPoints,
                        DungeonSaveJsonCodec.SerializeCreature(creature.Creature)
                    ))
            );

            return DungeonFloorStateProjector.ProjectValidated(parsed.Document, runtime);
        }
    }
}
