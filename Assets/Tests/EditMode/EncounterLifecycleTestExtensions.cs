using System;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using NUnit.Framework;

/// <summary>Runs older Unity integration fixtures through exact encounter turn authority.</summary>
internal static class EncounterLifecycleTestExtensions
{
    /// <summary>Commits the typed one-action Interact used to spend fixture resources.</summary>
    internal static bool TryCommitInteract(this ActionController controller)
    {
        if (controller == null)
            throw new ArgumentNullException(nameof(controller));
        return controller.TryGetCombatRules(out UnityCombatRulesBridge bridge, out CreatureId actor)
            && bridge.Dispatch(new InteractActionOp(actor)) is ResolvedOpResult<InteractOutcome>;
    }

    /// <summary>Starts or advances a real encounter until the requested actor owns the turn.</summary>
    internal static void BeginTurn(
        this UnityCombatRulesBridge bridge,
        CreatureId actor,
        int expectedActions
    )
    {
        if (bridge == null)
            throw new ArgumentNullException(nameof(bridge));
        bool startedNow = !bridge.Snapshot.Encounters.TryGet(
            new EncounterId("unity-encounter-1"),
            out _
        );
        if (startedNow)
            bridge.StartEncounter(
                bridge.Snapshot.Creatures[actor].Player,
                EncounterConclusionPolicy.VictoryOrDefeat
            );

        int remaining = bridge.Snapshot.Creatures.Count + 1;
        EncounterState encounter = bridge.GetEncounter();
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
        Assert.That(bridge.GetStandardActionsRemaining(actor), Is.EqualTo(expectedActions));
    }
}
