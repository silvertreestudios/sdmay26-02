using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    /// <summary>Verifies immutable movement values and pure PF2e grid-cost calculations.</summary>
    public sealed class MovementValueTests
    {
        [Test]
        public void OrthogonalAndAlternatingDiagonalCostsPreserveStartingPhase()
        {
            GridPosition origin = new GridPosition(0, 0, 0);
            MovementStepCost orthogonal = MovementCostRules.Calculate(
                origin,
                new GridPosition(1, 0, 0),
                TerrainCost.Normal,
                DiagonalMovementPhase.NextCostsTenFeet
            );
            MovementStepCost firstDiagonal = MovementCostRules.Calculate(
                origin,
                new GridPosition(1, 0, 1),
                TerrainCost.Normal,
                DiagonalMovementPhase.NextCostsFiveFeet
            );
            MovementStepCost secondDiagonal = MovementCostRules.Calculate(
                new GridPosition(1, 0, 1),
                new GridPosition(2, 0, 2),
                TerrainCost.Normal,
                firstDiagonal.NextDiagonalPhase
            );

            Assert.That(orthogonal.Distance.Feet, Is.EqualTo(5));
            Assert.That(
                orthogonal.NextDiagonalPhase,
                Is.EqualTo(DiagonalMovementPhase.NextCostsTenFeet),
                "Orthogonal movement must not reset the turn-persistent diagonal phase."
            );
            Assert.That(firstDiagonal.Distance.Feet, Is.EqualTo(5));
            Assert.That(
                firstDiagonal.NextDiagonalPhase,
                Is.EqualTo(DiagonalMovementPhase.NextCostsTenFeet)
            );
            Assert.That(secondDiagonal.Distance.Feet, Is.EqualTo(10));
            Assert.That(
                secondDiagonal.NextDiagonalPhase,
                Is.EqualTo(DiagonalMovementPhase.NextCostsFiveFeet)
            );
        }

        [Test]
        public void DifficultAndGreaterDifficultTerrainAddFixedEntryCosts()
        {
            GridPosition from = new GridPosition(0, 0, 0);
            GridPosition orthogonal = new GridPosition(1, 0, 0);
            GridPosition diagonal = new GridPosition(1, 0, 1);

            Assert.That(
                MovementCostRules
                    .Calculate(
                        from,
                        orthogonal,
                        TerrainCost.Difficult,
                        DiagonalMovementPhase.NextCostsFiveFeet
                    )
                    .Distance.Feet,
                Is.EqualTo(10)
            );
            Assert.That(
                MovementCostRules
                    .Calculate(
                        from,
                        diagonal,
                        TerrainCost.GreaterDifficult,
                        DiagonalMovementPhase.NextCostsTenFeet
                    )
                    .Distance.Feet,
                Is.EqualTo(20)
            );
        }

        [Test]
        public void GroundCostRejectsVerticalStationaryAndNoncontiguousSteps()
        {
            GridPosition origin = new GridPosition(0, 0, 0);

            Assert.That(MovementCostRules.IsContiguous(origin, origin), Is.False);
            Assert.That(
                MovementCostRules.IsContiguous(origin, new GridPosition(0, 1, 0)),
                Is.False
            );
            Assert.That(
                MovementCostRules.IsContiguous(origin, new GridPosition(2, 0, 0)),
                Is.False
            );
            Assert.Throws<ArgumentException>(() =>
                MovementCostRules.Calculate(
                    origin,
                    new GridPosition(2, 0, 0),
                    TerrainCost.Normal,
                    DiagonalMovementPhase.NextCostsFiveFeet
                )
            );
        }

        [Test]
        public void TopologyAndPathDefensivelyCopyCallerCollections()
        {
            GridPosition difficult = new GridPosition(1, 0, 0);
            List<GridCell> cells = new List<GridCell>
            {
                new GridCell(difficult, false, TerrainCost.Difficult),
            };
            GridTopology topology = new GridTopology(
                new GridBounds(new GridPosition(0, 0, 0), new GridPosition(3, 0, 3)),
                cells
            );
            List<GridPosition> steps = new List<GridPosition> { difficult };
            MovementPath path = new MovementPath(new GridPosition(0, 0, 0), steps);

            cells[0] = new GridCell(difficult, true, TerrainCost.GreaterDifficult);
            steps[0] = new GridPosition(3, 0, 3);

            Assert.That(topology.IsBlocked(difficult), Is.False);
            Assert.That(topology.GetTerrainCost(difficult), Is.EqualTo(TerrainCost.Difficult));
            Assert.That(path.Destination, Is.EqualTo(difficult));
        }

        [Test]
        public void MovementBudgetSeedIsAuthoritativeAndKeyValidated()
        {
            CreatureId mover = new CreatureId("mover");
            MovementBudgetState budget = new MovementBudgetState(
                new MovementBudgetId(new OpId(42)),
                mover,
                new GridDistance(25),
                DiagonalMovementPhase.NextCostsTenFeet
            );
            RulesSnapshot snapshot = new InMemoryRulesStore(
                new RulesStateSeed().SeedMovementBudget(mover, budget)
            ).Snapshot;

            Assert.That(snapshot.MovementBudgets[mover], Is.EqualTo(budget));
            Assert.Throws<ArgumentException>(() =>
                new RulesStateSeed().SeedMovementBudget(new CreatureId("other"), budget)
            );
        }

        [Test]
        public void TopologyRejectsOutOfBoundsAndDuplicateOverrides()
        {
            GridBounds bounds = new GridBounds(
                new GridPosition(0, 0, 0),
                new GridPosition(1, 0, 1)
            );
            GridCell cell = new GridCell(new GridPosition(1, 0, 1), false, TerrainCost.Difficult);

            Assert.Throws<ArgumentException>(() => new GridTopology(bounds, new[] { cell, cell }));
            Assert.Throws<ArgumentException>(() =>
                new GridTopology(
                    bounds,
                    new[] { new GridCell(new GridPosition(2, 0, 1), false, TerrainCost.Normal) }
                )
            );
        }
    }
}
