using System;
using System.Linq;
using Game.Combat.Encounters;
using Game.DungeonGeneration;
using Game.DungeonPersistence.Floors;
using Game.DungeonPersistence.Repository;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class DungeonFloorSaveProjectorTests
{
    private const string FixturePath = "Assets/Maps/KayKit/GeneratedDungeonFixture.json";

    [Test]
    public void ProjectForPopulationRestoresMutableStateWithoutChangingStaticJson()
    {
        TextAsset fixture = AssetDatabase.LoadAssetAtPath<TextAsset>(FixturePath);
        Assert.That(fixture, Is.Not.Null);
        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(fixture.text);
        Assert.That(parsed.IsSuccess, Is.True);
        DungeonLevelDocument source = parsed.Document;
        DungeonEncounterPlan activePlan = source.EncounterPlans.First();
        string originalStaticJson = DungeonLevelJsonSerializer.Serialize(source);

        DungeonEncounterCreatureSaveState[] creatures = activePlan
            .CreatureIds.Select(
                (contentId, index) =>
                    new DungeonEncounterCreatureSaveState(
                        activePlan.Id,
                        Creature(
                            DungeonCreatureInstanceIdentity.Create(activePlan.Id, index),
                            contentId,
                            activePlan.SpawnCells[index],
                            isDefeated: index == 0
                        )
                    )
            )
            .ToArray();
        DungeonFloorSaveState floor = new(
            DungeonSaveSchema.FloorStateVersion,
            source.Generation.Depth,
            originalStaticJson,
            source.Doors.Select(
                (door, index) => new DungeonDoorSaveState(door.Id, isOpen: index == 0)
            ),
            source.EncounterPlans.Select(plan => new DungeonEncounterSaveState(
                plan.Id,
                plan.Id == activePlan.Id
                    ? DungeonEncounterSaveStatus.Active
                    : DungeonEncounterSaveStatus.Dormant
            )),
            creatures
        );

        DungeonLevelDocument projected = DungeonFloorSaveProjector.ProjectForPopulation(floor);

        Assert.That(projected.RuntimeState, Is.Not.Null);
        Assert.That(projected.RuntimeState.OpenDoorIds, Is.EqualTo(new[] { source.Doors[0].Id }));
        Assert.That(projected.Doors.Single(door => door.Id == source.Doors[0].Id).IsOpen, Is.True);
        Assert.That(projected.RuntimeState.ResolvedEncounterIds, Is.Empty);
        Assert.That(
            projected.RuntimeState.DefeatedCreatureIds,
            Is.EqualTo(new[] { creatures[0].Creature.InstanceId })
        );
        Assert.That(projected.RuntimeState.Creatures, Has.Count.EqualTo(creatures.Length - 1));
        foreach (DungeonCreatureRuntimeState living in projected.RuntimeState.Creatures)
        {
            DungeonSaveParseResult<DungeonCreatureSaveState> actor =
                DungeonSaveJsonCodec.ParseCreature(living.State);
            Assert.That(actor, Is.TypeOf<DungeonSaveParseSuccess<DungeonCreatureSaveState>>());
            Assert.That(
                ((DungeonSaveParseSuccess<DungeonCreatureSaveState>)actor).Value.InstanceId,
                Is.EqualTo(living.InstanceId)
            );
        }
        DungeonEncounterLifecycleSnapshot lifecycle =
            DungeonEncounterLifecycleSnapshot.FromRuntimeState(
                projected.EncounterPlans,
                projected.RuntimeState
            );
        Assert.That(
            lifecycle.Groups.Single(group => group.EncounterId == activePlan.Id).State,
            Is.EqualTo(DungeonEncounterGroupState.Suspended),
            "An unfinished saved fight must resume through fresh initiative."
        );
        Assert.That(DungeonLevelJsonSerializer.Serialize(source), Is.EqualTo(originalStaticJson));
        Assert.That(source.RuntimeState, Is.Null);
    }

    [Test]
    public void ProjectForPopulationRestoresClearedEncounterWithoutLivingActors()
    {
        TextAsset fixture = AssetDatabase.LoadAssetAtPath<TextAsset>(FixturePath);
        DungeonLevelDocument source = DungeonLevelJsonParser.Parse(fixture.text).Document;
        DungeonEncounterPlan clearedPlan = source.EncounterPlans.First();
        DungeonFloorSaveState floor = new(
            DungeonSaveSchema.FloorStateVersion,
            source.Generation.Depth,
            DungeonLevelJsonSerializer.Serialize(source),
            source.Doors.Select(door => new DungeonDoorSaveState(door.Id, false)),
            source.EncounterPlans.Select(plan => new DungeonEncounterSaveState(
                plan.Id,
                plan.Id == clearedPlan.Id
                    ? DungeonEncounterSaveStatus.Cleared
                    : DungeonEncounterSaveStatus.Dormant
            )),
            clearedPlan.CreatureIds.Select(
                (contentId, index) =>
                    new DungeonEncounterCreatureSaveState(
                        clearedPlan.Id,
                        Creature(
                            DungeonCreatureInstanceIdentity.Create(clearedPlan.Id, index),
                            contentId,
                            clearedPlan.SpawnCells[index],
                            isDefeated: true
                        )
                    )
            )
        );

        DungeonLevelDocument projected = DungeonFloorSaveProjector.ProjectForPopulation(floor);

        Assert.That(projected.RuntimeState.Creatures, Is.Empty);
        Assert.That(
            projected.RuntimeState.DefeatedCreatureIds,
            Has.Count.EqualTo(clearedPlan.CreatureIds.Count)
        );
        Assert.That(
            projected.RuntimeState.ResolvedEncounterIds,
            Is.EqualTo(new[] { clearedPlan.Id })
        );
        Assert.That(
            projected.EncounterPlans.Single(plan => plan.Id == clearedPlan.Id).IsResolved,
            Is.True
        );
    }

    private static DungeonCreatureSaveState Creature(
        string instanceId,
        string contentId,
        DungeonCell cell,
        bool isDefeated
    ) =>
        new(
            instanceId,
            contentId,
            new DungeonSaveCell(cell.X, cell.Z),
            new DungeonHealthSaveState(
                isDefeated ? 0 : 5,
                10,
                0,
                string.Empty,
                Array.Empty<string>()
            ),
            isDefeated,
            Array.Empty<DungeonConditionSaveState>(),
            Array.Empty<DungeonTimedEffectSaveState>(),
            new DungeonPreparedRuleSaveState(
                Array.Empty<string>(),
                Array.Empty<DungeonPreparedEffectSaveState>(),
                Array.Empty<DungeonSpellPoolSaveState>()
            ),
            new DungeonEquipmentSaveState(
                Array.Empty<DungeonInventoryItemSaveState>(),
                Array.Empty<DungeonAmmunitionSaveState>()
            )
        );
}
