using System;
using System.Collections.Generic;
using System.Linq;
using Game.DungeonGeneration;

namespace Game.DungeonPersistence
{
    /// <summary>
    /// Projects captured mutable floor state onto immutable generation facts and validates the
    /// resulting complete dungeon document through the public JSON boundary.
    /// </summary>
    /// <remarks>
    /// Door and encounter flags intentionally have one persisted source of truth: they are rebuilt
    /// from <see cref="DungeonRuntimeState"/> before serialization. Reparsing prevents a caller
    /// from saving a partially consistent document that scene population would later reject.
    /// </remarks>
    public static class DungeonFloorStateProjector
    {
        /// <summary>
        /// Creates and reparses a complete floor document containing the supplied runtime state.
        /// </summary>
        /// <param name="source">The validated immutable generation and encounter facts.</param>
        /// <param name="runtimeState">The complete mutable state captured from the active floor.</param>
        /// <returns>
        /// A newly parsed document whose door and encounter flags exactly mirror runtime state.
        /// </returns>
        /// <exception cref="ArgumentNullException">A required argument is absent.</exception>
        /// <exception cref="ArgumentException">
        /// Runtime IDs are duplicated, unknown, or produce a document that fails schema validation.
        /// </exception>
        public static DungeonLevelDocument ProjectValidated(
            DungeonLevelDocument source,
            DungeonRuntimeState runtimeState
        )
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (runtimeState == null)
                throw new ArgumentNullException(nameof(runtimeState));

            HashSet<string> openDoors = RequireKnownDistinctIds(
                runtimeState.OpenDoorIds,
                source.Doors.Select(door => door.Id),
                "open door"
            );
            HashSet<string> resolvedEncounters = RequireKnownDistinctIds(
                runtimeState.ResolvedEncounterIds,
                source.EncounterPlans.Select(plan => plan.Id),
                "resolved encounter"
            );

            DungeonLevelDocument projected = new(
                source.Generation,
                source.Rows,
                source.Rooms,
                source.Doors.Select(door => new DungeonDoor(
                    door.Id,
                    door.Cell,
                    openDoors.Contains(door.Id)
                )),
                source.Stairs,
                source.StartCell,
                source.SafeCells,
                source.Objects,
                source.EncounterPlans.Select(plan => new DungeonEncounterPlan(
                    plan.Id,
                    plan.RoomId,
                    plan.Threat,
                    plan.Budget,
                    plan.SpawnCells,
                    plan.CreatureIds,
                    resolvedEncounters.Contains(plan.Id)
                )),
                runtimeState
            );

            string json = DungeonLevelJsonSerializer.Serialize(projected);
            DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(json);
            if (!parsed.IsSuccess)
            {
                throw new ArgumentException(
                    "Captured dungeon floor state is invalid: "
                        + string.Join(
                            " ",
                            parsed.Diagnostics.Select(diagnostic => diagnostic.Message)
                        ),
                    nameof(runtimeState)
                );
            }
            return parsed.Document;
        }

        private static HashSet<string> RequireKnownDistinctIds(
            IEnumerable<string> suppliedIds,
            IEnumerable<string> knownIds,
            string description
        )
        {
            if (suppliedIds == null)
                throw new ArgumentNullException(nameof(suppliedIds));

            string[] supplied = suppliedIds.ToArray();
            if (supplied.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException(
                    $"A {description} ID cannot be blank.",
                    nameof(suppliedIds)
                );
            if (supplied.Distinct(StringComparer.Ordinal).Count() != supplied.Length)
            {
                throw new ArgumentException(
                    $"{description} IDs must be unique.",
                    nameof(suppliedIds)
                );
            }

            HashSet<string> known = new(knownIds, StringComparer.Ordinal);
            if (supplied.Any(id => !known.Contains(id)))
            {
                throw new ArgumentException(
                    $"Every {description} ID must belong to the source floor.",
                    nameof(suppliedIds)
                );
            }
            return new HashSet<string>(supplied, StringComparer.Ordinal);
        }
    }
}
