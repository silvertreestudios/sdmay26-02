using System;
using System.Collections;
using System.Collections.Generic;
using Game.Creature;
using Game.KayKit;
using Game.Rules.Runtime;
using GridPrivate;
using UnityEngine;

namespace Game.Rules.Unity
{
    /// <summary>Projects committed Stride movement into Unity occupancy and animation.</summary>
    internal sealed class UnityStrideProjectionObserver : IFactObserver<TokenMovedFact>
    {
        private readonly GameObject character;
        private readonly CreatureId creature;
        private readonly GridBase grid;
        private readonly bool startedInExploration;
        private readonly Queue<CommittedStep> committedSteps = new();
        private IExplorationPresentationDrain explorationPresentation;

        internal UnityStrideProjectionObserver(
            GameObject character,
            CreatureId creature,
            GridBase grid,
            bool startedInExploration
        )
        {
            this.character = character ?? throw new ArgumentNullException(nameof(character));
            if (creature.IsEmpty)
                throw new ArgumentException("A rules creature ID is required.", nameof(creature));
            this.creature = creature;
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
            this.startedInExploration = startedInExploration;
        }

        /// <summary>
        /// Gets whether the committed exploration step projected but invalidated its remaining
        /// destination route.
        /// </summary>
        internal bool WasRouteInterrupted { get; private set; }

        /// <summary>
        /// Projects all steps captured during mechanically complete rules dispatch, then drains
        /// follower presentation queued by the exploration coordinator.
        /// </summary>
        internal IEnumerator DrainPresentation()
        {
            while (committedSteps.Count > 0)
            {
                CommittedStep step = committedSteps.Dequeue();
                yield return Project(step.Fact, step.Snapshot);
                if (WasRouteInterrupted)
                    break;
            }
            if (explorationPresentation != null)
                yield return explorationPresentation.DrainPresentation(character);
        }

        /// <inheritdoc/>
        public void OnFactCommitted(TokenMovedFact fact, RulesSnapshot currentSnapshot)
        {
            if (fact.Mover != creature)
                return;
            if (currentSnapshot == null)
                throw new ArgumentNullException(nameof(currentSnapshot));
            committedSteps.Enqueue(new CommittedStep(fact, currentSnapshot));
        }

        private IEnumerator Project(TokenMovedFact fact, RulesSnapshot currentSnapshot)
        {
            Vector3Int from = ToUnity(fact.From);
            Vector3Int to = ToUnity(fact.To);
            if (Vector3Int.RoundToInt(character.transform.position) != from)
            {
                throw new InvalidOperationException(
                    $"Stride projection expected {character.name} at {from}, but its transform was "
                        + $"{Vector3Int.RoundToInt(character.transform.position)}."
                );
            }

            IExplorationStrideCoordinator exploration = grid.ExplorationStrideCoordinator;
            if (startedInExploration && exploration.Handles(character))
            {
                explorationPresentation = exploration as IExplorationPresentationDrain;
                Ref<bool> continuePath = new Ref<bool>(false);
                Ref<bool> pathInterrupted = new Ref<bool>(false);
                yield return exploration.ProjectCommittedStep(
                    character,
                    from,
                    to,
                    grid.GetTiles(),
                    TokenMovement.GetInstance(),
                    continuePath,
                    pathInterrupted
                );
                if (!continuePath.Value)
                {
                    if (pathInterrupted.Value)
                    {
                        if (!exploration.Handles(character))
                        {
                            // Encounter setup clears every controller's turn state before granting
                            // initiative. Restore this still-running action's guard until its owner
                            // finalizes, so no combat action can start and be cleared by that finalizer.
                            ActionController controller =
                                character.GetComponent<ActionController>();
                            if (controller != null)
                                controller.IsTakingAction = true;
                        }
                        WasRouteInterrupted = true;
                        yield break;
                    }
                    throw new InvalidOperationException(
                        "Exploration could not project an already-committed Stride step."
                    );
                }
                yield break;
            }

            if (!currentSnapshot.Positions.TryGet(creature, out GridPosition currentPosition))
                throw new InvalidOperationException("The mover has no authoritative position.");
            Tile[,] tiles = grid.GetTiles();
            ReconcileOccupancy(tiles, ToUnity(currentPosition));

            CreaturePresentation presentation = character.GetComponent<CreaturePresentation>();
            TokenMovement movement = TokenMovement.GetInstance();
            if (presentation?.AnimationController != null)
                yield return movement.Walk(character.transform, to);
            else
                yield return movement.Hop(character.transform, to);
        }

        private void ReconcileOccupancy(Tile[,] tiles, Vector3Int authoritativePosition)
        {
            if (!IsInBounds(tiles, authoritativePosition))
                throw new InvalidOperationException(
                    "The authoritative Stride position is outside the projected grid."
                );

            for (int x = 0; x < tiles.GetLength(0); x++)
            {
                for (int z = 0; z < tiles.GetLength(1); z++)
                    tiles[x, z]?.ProjectCommittedDeparture(character);
            }
            Tile destination = tiles[authoritativePosition.x, authoritativePosition.z];
            if (destination == null)
                throw new InvalidOperationException(
                    "The authoritative Stride position has no projected tile."
                );
            destination.ProjectCommittedArrival(character);
        }

        private static bool IsInBounds(Tile[,] tiles, Vector3Int position) =>
            position.x >= 0
            && position.z >= 0
            && position.x < tiles.GetLength(0)
            && position.z < tiles.GetLength(1);

        private static Vector3Int ToUnity(GridPosition value) =>
            new Vector3Int(value.X, value.Y, value.Z);

        private readonly struct CommittedStep
        {
            internal CommittedStep(TokenMovedFact fact, RulesSnapshot snapshot)
            {
                Fact = fact;
                Snapshot = snapshot;
            }

            internal TokenMovedFact Fact { get; }
            internal RulesSnapshot Snapshot { get; }
        }
    }
}
