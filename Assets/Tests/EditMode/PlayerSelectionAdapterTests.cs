using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using NUnit.Framework;
using UnityEngine;

namespace Game.Rules.Unity.Tests
{
    /// <summary>
    /// Verifies player scene input crosses the injected mapping boundary as stable rules values.
    /// </summary>
    public sealed class PlayerSelectionAdapterTests
    {
        private static readonly SelectionRequestId RequestId = new SelectionRequestId(
            "player-selection-adapter"
        );

        /// <summary>
        /// Verifies scene objects, paths, cells, and area orientation are mapped without global lookup state.
        /// </summary>
        [Test]
        public async Task MapsSceneObjectPathCellAndAreaThroughInjectedMapper()
        {
            GameObject creatureObject = new GameObject("selected-creature");
            try
            {
                CreatureId creature = new CreatureId("mapped-creature");
                CreatureId mover = new CreatureId("mapped-mover");
                AreaTemplateId template = new AreaTemplateId("cone-template");
                List<Vector3Int> path = new List<Vector3Int>
                {
                    new Vector3Int(1, 0, 1),
                    new Vector3Int(2, 0, 1),
                };
                UnityAreaSelection area = new UnityAreaSelection(
                    template,
                    new Vector3Int(3, 0, 4),
                    new Vector3Int(4, 0, 4)
                );
                RecordingPlayerSource source = new RecordingPlayerSource(
                    creatureObject,
                    path,
                    new Vector3Int(7, 0, 8),
                    area
                );
                RecordingMapper mapper = new RecordingMapper(creatureObject, creature);
                PlayerSelectionAdapter adapter = new PlayerSelectionAdapter(source, mapper);

                SelectionOutcome<CreatureSelection> creatureOutcome = await adapter.SelectCreature(
                    new CreatureSelectionRequest(RequestId, new[] { creature })
                );
                SelectionOutcome<PathSelection> pathOutcome = await adapter.SelectPath(
                    new PathSelectionRequest(
                        RequestId,
                        mover,
                        new GridPosition(0, 0, 0),
                        new[] { new GridPosition(2, 0, 1) },
                        4
                    )
                );
                SelectionOutcome<GridCellSelection> cellOutcome = await adapter.SelectGridCell(
                    new GridCellSelectionRequest(RequestId, new[] { new GridPosition(7, 0, 8) })
                );
                SelectionOutcome<AreaSelection> areaOutcome = await adapter.SelectArea(
                    new AreaSelectionRequest(
                        RequestId,
                        new[] { template },
                        new[] { new GridPosition(3, 0, 4) }
                    )
                );
                path[0] = new Vector3Int(99, 99, 99);

                Assert.That(
                    ((CompletedSelectionOutcome<CreatureSelection>)creatureOutcome)
                        .Selection
                        .Creature,
                    Is.EqualTo(creature)
                );
                Assert.That(
                    ((CompletedSelectionOutcome<PathSelection>)pathOutcome).Selection.Positions,
                    Is.EqualTo(new[] { new GridPosition(1, 0, 1), new GridPosition(2, 0, 1) })
                );
                Assert.That(
                    ((CompletedSelectionOutcome<GridCellSelection>)cellOutcome).Selection.Cell,
                    Is.EqualTo(new GridPosition(7, 0, 8))
                );
                AreaSelection mappedArea = (
                    (CompletedSelectionOutcome<AreaSelection>)areaOutcome
                ).Selection;
                Assert.That(mappedArea.Template, Is.EqualTo(template));
                Assert.That(mappedArea.Orientation.Origin, Is.EqualTo(new GridPosition(3, 0, 4)));
                Assert.That(mappedArea.Orientation.Facing, Is.EqualTo(new GridPosition(4, 0, 4)));
                Assert.That(mapper.CreatureCalls, Is.EqualTo(1));
                Assert.That(mapper.PathCalls, Is.EqualTo(1));
                Assert.That(mapper.CellCalls, Is.EqualTo(1));
                Assert.That(mapper.AreaCalls, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(creatureObject);
            }
        }

        /// <summary>
        /// Verifies cancellation remains structural and does not invoke the reference mapper.
        /// </summary>
        [Test]
        public async Task CancelledPlayerSelectionDoesNotInvokeMapper()
        {
            GameObject unusedObject = new GameObject("unused-selection-object");
            try
            {
                RecordingPlayerSource source = new RecordingPlayerSource(
                    unusedObject,
                    Array.Empty<Vector3Int>(),
                    Vector3Int.zero,
                    new UnityAreaSelection(
                        new AreaTemplateId("unused-area"),
                        Vector3Int.zero,
                        Vector3Int.right
                    )
                );
                source.CancelCreatureSelection();
                RecordingMapper mapper = new RecordingMapper(
                    unusedObject,
                    new CreatureId("unused-creature")
                );
                PlayerSelectionAdapter adapter = new PlayerSelectionAdapter(source, mapper);

                SelectionOutcome<CreatureSelection> result = await adapter.SelectCreature(
                    new CreatureSelectionRequest(
                        RequestId,
                        new[] { new CreatureId("unused-creature") }
                    )
                );

                Assert.That(result, Is.TypeOf<CancelledSelectionOutcome<CreatureSelection>>());
                Assert.That(mapper.CreatureCalls, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(unusedObject);
            }
        }

        private sealed class RecordingPlayerSource : IPlayerSelectionSource
        {
            private readonly IReadOnlyList<Vector3Int> path;
            private readonly Vector3Int cell;
            private readonly UnityAreaSelection area;

            public RecordingPlayerSource(
                GameObject creature,
                IReadOnlyList<Vector3Int> path,
                Vector3Int cell,
                UnityAreaSelection area
            )
            {
                creatureOutcome = SelectionOutcome<GameObject>.Completed(creature);
                this.path = path;
                this.cell = cell;
                this.area = area;
            }

            private SelectionOutcome<GameObject> creatureOutcome;

            public void CancelCreatureSelection() =>
                creatureOutcome = SelectionOutcome<GameObject>.Cancelled;

            public ValueTask<SelectionOutcome<GameObject>> SelectCreature(
                CreatureSelectionRequest request
            ) => new ValueTask<SelectionOutcome<GameObject>>(creatureOutcome);

            public ValueTask<SelectionOutcome<IReadOnlyList<GameObject>>> SelectCreatures(
                MultipleCreatureSelectionRequest request
            ) => Unused<IReadOnlyList<GameObject>>();

            public ValueTask<SelectionOutcome<GameObject>> SelectItem(
                ItemSelectionRequest request
            ) => Unused<GameObject>();

            public ValueTask<SelectionOutcome<GameObject>> SelectWeapon(
                WeaponSelectionRequest request
            ) => Unused<GameObject>();

            public ValueTask<SelectionOutcome<IReadOnlyList<Vector3Int>>> SelectPath(
                PathSelectionRequest request
            ) =>
                new ValueTask<SelectionOutcome<IReadOnlyList<Vector3Int>>>(
                    SelectionOutcome<IReadOnlyList<Vector3Int>>.Completed(path)
                );

            public ValueTask<SelectionOutcome<Vector3Int>> SelectGridCell(
                GridCellSelectionRequest request
            ) =>
                new ValueTask<SelectionOutcome<Vector3Int>>(
                    SelectionOutcome<Vector3Int>.Completed(cell)
                );

            public ValueTask<SelectionOutcome<UnityAreaSelection>> SelectArea(
                AreaSelectionRequest request
            ) =>
                new ValueTask<SelectionOutcome<UnityAreaSelection>>(
                    SelectionOutcome<UnityAreaSelection>.Completed(area)
                );

            public ValueTask<SelectionOutcome<SpellVariantId>> SelectSpellVariant(
                SpellVariantSelectionRequest request
            ) => Unused<SpellVariantId>();

            public ValueTask<SelectionOutcome<SpellSlotPoolId>> SelectSpellSlot(
                SpellSlotSelectionRequest request
            ) => Unused<SpellSlotPoolId>();

            public ValueTask<SelectionOutcome<bool>> Confirm(
                ConfirmationSelectionRequest request
            ) => Unused<bool>();

            private static ValueTask<SelectionOutcome<T>> Unused<T>() =>
                new ValueTask<SelectionOutcome<T>>(SelectionOutcome<T>.Invalid("unused"));
        }

        private sealed class RecordingMapper : IUnitySelectionMapper
        {
            private readonly GameObject expectedCreature;
            private readonly CreatureId creature;

            public RecordingMapper(GameObject expectedCreature, CreatureId creature)
            {
                this.expectedCreature = expectedCreature;
                this.creature = creature;
            }

            public int CreatureCalls { get; private set; }

            public int PathCalls { get; private set; }

            public int CellCalls { get; private set; }

            public int AreaCalls { get; private set; }

            public CreatureSelection MapCreature(GameObject sceneObject)
            {
                Assert.That(sceneObject, Is.SameAs(expectedCreature));
                CreatureCalls++;
                return new CreatureSelection(creature);
            }

            public MultipleCreatureSelection MapCreatures(IReadOnlyList<GameObject> sceneObjects) =>
                throw new NotSupportedException();

            public ItemSelection MapItem(GameObject sceneObject) =>
                throw new NotSupportedException();

            public WeaponSelection MapWeapon(GameObject sceneObject) =>
                throw new NotSupportedException();

            public PathSelection MapPath(IReadOnlyList<Vector3Int> path)
            {
                PathCalls++;
                return new PathSelection(path.Select(ToGridPosition));
            }

            public GridCellSelection MapGridCell(Vector3Int cell)
            {
                CellCalls++;
                return new GridCellSelection(ToGridPosition(cell));
            }

            public AreaSelection MapArea(UnityAreaSelection area)
            {
                AreaCalls++;
                return new AreaSelection(
                    area.Template,
                    new AreaOrientation(ToGridPosition(area.Origin), ToGridPosition(area.Facing))
                );
            }

            private static GridPosition ToGridPosition(Vector3Int value) =>
                new GridPosition(value.x, value.y, value.z);
        }
    }
}
