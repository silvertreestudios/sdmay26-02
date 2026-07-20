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

        /// <summary>The actor is not a player character.</summary>
        ActorIsNotPlayerCharacter,

        /// <summary>The actor is not alive.</summary>
        ActorIsDead,

        /// <summary>The target door is already open.</summary>
        DoorAlreadyOpen,

        /// <summary>The actor does not occupy a cardinally adjacent cell.</summary>
        ActorIsNotAdjacent,

        /// <summary>The actor lacks the actions required to open the door in combat.</summary>
        InsufficientActionPoints,
    }

    /// <summary>
    /// Captures the immutable facts needed to authorize one attempt to open a dungeon door.
    /// </summary>
    public readonly struct DungeonDoorInteractionRequest
    {
        /// <summary>Creates a door-opening authorization request without referencing mutable Unity objects.</summary>
        /// <param name="mode">The gameplay mode in which the interaction occurs.</param>
        /// <param name="actorIsPlayerCharacter">Whether the actor belongs to the player-character party.</param>
        /// <param name="actorIsAlive">Whether the actor is currently alive.</param>
        /// <param name="actorCell">The actor's current grid cell.</param>
        /// <param name="doorCell">The target door's grid cell.</param>
        /// <param name="doorIsOpen">Whether the target door is already open.</param>
        /// <param name="availableActionPoints">The actor's current unspent combat actions.</param>
        public DungeonDoorInteractionRequest(
            DungeonDoorInteractionMode mode,
            bool actorIsPlayerCharacter,
            bool actorIsAlive,
            DungeonCell actorCell,
            DungeonCell doorCell,
            bool doorIsOpen,
            uint availableActionPoints
        )
        {
            Mode = mode;
            ActorIsPlayerCharacter = actorIsPlayerCharacter;
            ActorIsAlive = actorIsAlive;
            ActorCell = actorCell;
            DoorCell = doorCell;
            DoorIsOpen = doorIsOpen;
            AvailableActionPoints = availableActionPoints;
        }

        /// <summary>Gets the gameplay mode in which the interaction occurs.</summary>
        public DungeonDoorInteractionMode Mode { get; }

        /// <summary>Gets whether the actor belongs to the player-character party.</summary>
        public bool ActorIsPlayerCharacter { get; }

        /// <summary>Gets whether the actor is currently alive.</summary>
        public bool ActorIsAlive { get; }

        /// <summary>Gets the actor's current grid cell.</summary>
        public DungeonCell ActorCell { get; }

        /// <summary>Gets the target door's grid cell.</summary>
        public DungeonCell DoorCell { get; }

        /// <summary>Gets whether the target door is already open.</summary>
        public bool DoorIsOpen { get; }

        /// <summary>Gets the actor's current unspent combat actions.</summary>
        public uint AvailableActionPoints { get; }
    }

    /// <summary>Reports whether a door interaction is authorized and its required action cost.</summary>
    public readonly struct DungeonDoorInteractionDecision
    {
        internal DungeonDoorInteractionDecision(
            bool isAllowed,
            uint actionCost,
            DungeonDoorInteractionRejection rejection
        )
        {
            IsAllowed = isAllowed;
            ActionCost = actionCost;
            Rejection = rejection;
        }

        /// <summary>Gets whether the caller may open the door.</summary>
        public bool IsAllowed { get; }

        /// <summary>
        /// Gets the number of actions the caller must spend if the interaction is performed.
        /// A rejected valid-mode request still reports its mode's required cost.
        /// </summary>
        public uint ActionCost { get; }

        /// <summary>Gets the rejection category, or <see cref="DungeonDoorInteractionRejection.None"/> when authorized.</summary>
        public DungeonDoorInteractionRejection Rejection { get; }
    }

    /// <summary>
    /// Applies the pure authorization and action-cost rules for opening generated dungeon doors.
    /// </summary>
    public static class DungeonDoorInteractionPolicy
    {
        /// <summary>The number of combat actions required to open a closed adjacent door.</summary>
        public const uint CombatActionCost = 1;

        /// <summary>
        /// Evaluates a door-opening request without opening the door or consuming action points.
        /// </summary>
        /// <param name="request">The immutable actor, door, and mode facts to evaluate.</param>
        /// <returns>An explicit authorization decision and the applicable action cost.</returns>
        public static DungeonDoorInteractionDecision Evaluate(DungeonDoorInteractionRequest request)
        {
            if (!TryGetActionCost(request.Mode, out uint actionCost))
                return Reject(0, DungeonDoorInteractionRejection.InvalidMode);
            if (!request.ActorIsPlayerCharacter)
                return Reject(
                    actionCost,
                    DungeonDoorInteractionRejection.ActorIsNotPlayerCharacter
                );
            if (!request.ActorIsAlive)
                return Reject(actionCost, DungeonDoorInteractionRejection.ActorIsDead);
            if (request.DoorIsOpen)
                return Reject(actionCost, DungeonDoorInteractionRejection.DoorAlreadyOpen);
            if (!IsCardinallyAdjacent(request.ActorCell, request.DoorCell))
                return Reject(actionCost, DungeonDoorInteractionRejection.ActorIsNotAdjacent);
            if (
                request.Mode == DungeonDoorInteractionMode.Combat
                && request.AvailableActionPoints < actionCost
            )
                return Reject(actionCost, DungeonDoorInteractionRejection.InsufficientActionPoints);

            return new DungeonDoorInteractionDecision(
                true,
                actionCost,
                DungeonDoorInteractionRejection.None
            );
        }

        private static bool TryGetActionCost(DungeonDoorInteractionMode mode, out uint actionCost)
        {
            switch (mode)
            {
                case DungeonDoorInteractionMode.Exploration:
                    actionCost = 0;
                    return true;
                case DungeonDoorInteractionMode.Combat:
                    actionCost = CombatActionCost;
                    return true;
                default:
                    actionCost = 0;
                    return false;
            }
        }

        private static bool IsCardinallyAdjacent(DungeonCell actorCell, DungeonCell doorCell)
        {
            long xDistance = System.Math.Abs((long)actorCell.X - doorCell.X);
            long zDistance = System.Math.Abs((long)actorCell.Z - doorCell.Z);
            return xDistance + zDistance == 1;
        }

        private static DungeonDoorInteractionDecision Reject(
            uint actionCost,
            DungeonDoorInteractionRejection rejection
        ) => new(false, actionCost, rejection);
    }
}
