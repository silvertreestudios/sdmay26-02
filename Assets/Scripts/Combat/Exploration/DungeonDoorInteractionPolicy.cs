using Game.DungeonGeneration;

namespace Game.Combat.Exploration
{
    /// <summary>Identifies the gameplay mode in which a dungeon door interaction is requested.</summary>
    public enum DungeonDoorInteractionMode
    {
        /// <summary>The party is navigating the dungeon outside combat.</summary>
        Exploration = 1,

        /// <summary>The actor is taking actions during combat.</summary>
        Combat = 2,
    }

    /// <summary>Identifies why a dungeon door interaction request was rejected.</summary>
    public enum DungeonDoorInteractionRejection
    {
        /// <summary>The interaction is authorized.</summary>
        None,

        /// <summary>The supplied gameplay mode is not defined.</summary>
        InvalidMode,

        /// <summary>The target door is already open.</summary>
        DoorAlreadyOpen,

        /// <summary>The actor does not occupy a cardinally adjacent cell.</summary>
        ActorIsNotAdjacent,
    }

    /// <summary>
    /// Captures the immutable facts needed to authorize one attempt to open a dungeon door.
    /// </summary>
    public readonly struct DungeonDoorInteractionRequest
    {
        /// <summary>Creates a door-opening authorization request without referencing mutable Unity objects.</summary>
        /// <param name="mode">The gameplay mode in which the interaction occurs.</param>
        /// <param name="actorCell">The actor's current grid cell.</param>
        /// <param name="doorCell">The target door's grid cell.</param>
        /// <param name="doorIsOpen">Whether the target door is already open.</param>
        public DungeonDoorInteractionRequest(
            DungeonDoorInteractionMode mode,
            DungeonCell actorCell,
            DungeonCell doorCell,
            bool doorIsOpen
        )
        {
            Mode = mode;
            ActorCell = actorCell;
            DoorCell = doorCell;
            DoorIsOpen = doorIsOpen;
        }

        /// <summary>Gets the gameplay mode in which the interaction occurs.</summary>
        public DungeonDoorInteractionMode Mode { get; }

        /// <summary>Gets the actor's current grid cell.</summary>
        public DungeonCell ActorCell { get; }

        /// <summary>Gets the target door's grid cell.</summary>
        public DungeonCell DoorCell { get; }

        /// <summary>Gets whether the target door is already open.</summary>
        public bool DoorIsOpen { get; }
    }

    /// <summary>Reports whether a door interaction is geometrically authorized.</summary>
    public readonly struct DungeonDoorInteractionDecision
    {
        internal DungeonDoorInteractionDecision(
            bool isAllowed,
            DungeonDoorInteractionRejection rejection
        )
        {
            IsAllowed = isAllowed;
            Rejection = rejection;
        }

        /// <summary>Gets whether the caller may open the door.</summary>
        public bool IsAllowed { get; }

        /// <summary>Gets the rejection category, or <see cref="DungeonDoorInteractionRejection.None"/> when authorized.</summary>
        public DungeonDoorInteractionRejection Rejection { get; }
    }

    /// <summary>
    /// Applies the pure geometry and mode rules for opening generated dungeon doors.
    /// </summary>
    public static class DungeonDoorInteractionPolicy
    {
        /// <summary>
        /// Evaluates a door-opening request without opening the door or granting rules authority.
        /// </summary>
        /// <param name="request">The immutable actor, door, and mode facts to evaluate.</param>
        /// <returns>An explicit geometry and mode authorization decision.</returns>
        public static DungeonDoorInteractionDecision Evaluate(DungeonDoorInteractionRequest request)
        {
            if (
                request.Mode != DungeonDoorInteractionMode.Exploration
                && request.Mode != DungeonDoorInteractionMode.Combat
            )
                return Reject(DungeonDoorInteractionRejection.InvalidMode);
            if (request.DoorIsOpen)
                return Reject(DungeonDoorInteractionRejection.DoorAlreadyOpen);
            if (!IsCardinallyAdjacent(request.ActorCell, request.DoorCell))
                return Reject(DungeonDoorInteractionRejection.ActorIsNotAdjacent);
            return new DungeonDoorInteractionDecision(true, DungeonDoorInteractionRejection.None);
        }

        private static bool IsCardinallyAdjacent(DungeonCell actorCell, DungeonCell doorCell)
        {
            long xDistance = System.Math.Abs((long)actorCell.X - doorCell.X);
            long zDistance = System.Math.Abs((long)actorCell.Z - doorCell.Z);
            return xDistance + zDistance == 1;
        }

        private static DungeonDoorInteractionDecision Reject(
            DungeonDoorInteractionRejection rejection
        ) => new(false, rejection);
    }
}
