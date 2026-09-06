using System;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using NUnit.Framework;

/// <summary>Runs older Unity integration fixtures through exact encounter turn authority.</summary>
internal static class EncounterLifecycleTestExtensions
{
    /// <summary>Starts or advances a real encounter until the requested actor owns the turn.</summary>
    internal static void BeginTurn(
        this UnityCombatRulesBridge bridge,
        CreatureId actor,
        int expectedActions
    )
    {
        if (bridge == null)
            throw new ArgumentNullException(nameof(bridge));
        EncounterState encounter = bridge.GetEncounter();
        bool startedNow = encounter.Phase == EncounterPhase.Initialized;
        if (startedNow)
            encounter = bridge.AdvanceEncounter();

        int remaining = bridge.Snapshot.Creatures.Count + 1;
        if (
            !startedNow
            && encounter.Phase == EncounterPhase.Active
            && encounter.CurrentTurn.HasValue
            && encounter.CurrentTurn.Value.Actor == actor
        )
        {
            bridge.EndTurn(actor);
            encounter = bridge.GetEncounter();
        }
        while (
            encounter.Phase == EncounterPhase.Active
            && encounter.CurrentTurn.HasValue
            && encounter.CurrentTurn.Value.Actor != actor
            && remaining-- > 0
        )
        {
            bridge.EndTurn(encounter.CurrentTurn.Value.Actor);
            encounter = bridge.GetEncounter();
        }

        Assert.That(encounter.Phase, Is.EqualTo(EncounterPhase.Active));
        Assert.That(encounter.CurrentTurn.HasValue, Is.True);
        Assert.That(encounter.CurrentTurn.Value.Actor, Is.EqualTo(actor));
        Assert.That(bridge.GetActionsRemaining(actor), Is.EqualTo(expectedActions));
    }
}
