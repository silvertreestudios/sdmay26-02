using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using GridPrivate;
using GridPublic;
using UnityEngine;

public class CombatManager : CombatManagerInterface
{
    protected readonly List<ActionController> Combatants = new();
    protected ActionController TurnTaker;
    private readonly List<ActionController> activeCombatants = new();
    private bool combatActive;
    private bool dungeonDirectedCombat;
    private UnityEncounterRulesBridge encounterRules;

    /// <summary>Raised with the committed protagonist-relative dungeon outcome.</summary>
    public event Action<EncounterOutcome> DungeonCombatEnded = delegate { };
    public override event Action<bool> CombatActivityChanged = delegate { };
    public override bool IsCombatActive => combatActive;

    public override void AddCombatant(ActionController combatant)
    {
        if (combatant == null)
            throw new ArgumentNullException(nameof(combatant));
        if (Combatants.Contains(combatant))
            return;
        OnCombatantJoin.Invoke(combatant.gameObject);
        Combatants.Add(combatant);
    }

    public override void Remove(ActionController combatant)
    {
        if (combatActive && activeCombatants.Contains(combatant))
            return;
        Combatants.Remove(combatant);
    }

    public override GameObject WhosTurn() => TurnTaker == null ? null : TurnTaker.gameObject;

    public override List<GameObject> GetCombatants()
    {
        if (
            encounterRules == null
            || !encounterRules.Snapshot.Encounters.TryGet(
                encounterRules.EncounterId,
                out EncounterState encounter
            )
        )
            return Combatants
                .Where(value => value != null)
                .Select(value => value.gameObject)
                .ToList();
        List<GameObject> ordered = encounter
            .Roster.Select(entry => encounterRules.GetController(entry.Creature).gameObject)
            .ToList();
        if (TurnTaker != null && ordered.Remove(TurnTaker.gameObject))
            ordered.Insert(0, TurnTaker.gameObject);
        return ordered;
    }

    [ContextMenu("StartCombat")]
    public override void StartCombat() =>
        BeginCombat(Combatants.Where(CanParticipate).ToArray(), false);

    public override void StartDungeonCombat(IReadOnlyList<ActionController> participants) =>
        BeginCombat(participants, true);

    public override async void AddDungeonReinforcements(
        IReadOnlyList<ActionController> reinforcements
    )
    {
        if (reinforcements == null)
            throw new ArgumentNullException(nameof(reinforcements));
        if (!combatActive || !dungeonDirectedCombat)
            throw new InvalidOperationException(
                "Dungeon reinforcements require an active dungeon-directed combat."
            );
        ActionController[] additions = reinforcements.Distinct().ToArray();
        if (
            additions.Length == 0
            || additions.Any(controller =>
                controller == null
                || !Combatants.Contains(controller)
                || activeCombatants.Contains(controller)
                || !CanParticipate(controller)
            )
        )
            throw new InvalidOperationException(
                "Every reinforcement must be a new living registered controller."
            );
        await encounterRules.JoinEncounter(additions);
        activeCombatants.AddRange(additions);
        Pf2eRulesEngine.ApplyCombatStartRules(additions);
        LogInitiative("Reinforcements", additions);
    }

    public override async void SuspendDungeonCombat()
    {
        if (!combatActive || !dungeonDirectedCombat)
            throw new InvalidOperationException(
                "Only an active dungeon-directed combat can be suspended."
            );
        await encounterRules.SuspendEncounter();
        Pf2eRulesEngine.EndEncounter(activeCombatants);
        StopCombatState(cancelInFlightActions: true);
    }

    public override bool CheckForEndOfGame()
    {
        if (
            encounterRules == null
            || !encounterRules.Snapshot.Encounters.TryGet(
                encounterRules.EncounterId,
                out EncounterState encounter
            )
        )
            return false;
        return encounter.Phase == EncounterPhase.Ended;
    }

    public override async void NextTurn()
    {
        if (combatActive && encounterRules.CurrentTurn.HasValue)
            await encounterRules.EndTurn(encounterRules.CurrentTurn.Value);
    }

    public override async void EndCurrentTurn(ActionController actor)
    {
        if (actor == null)
            throw new ArgumentNullException(nameof(actor));
        if (
            !combatActive
            || !encounterRules.CurrentTurn.HasValue
            || encounterRules.CurrentTurn.Value.Actor != encounterRules.GetCreatureId(actor)
        )
            return;
        await encounterRules.EndTurn(encounterRules.CurrentTurn.Value);
    }

    private void BeginCombat(IReadOnlyList<ActionController> participants, bool dungeonDirected)
    {
        if (participants == null)
            throw new ArgumentNullException(nameof(participants));
        if (combatActive)
            throw new InvalidOperationException("Combat is already active.");
        ActionController[] selected = participants.Distinct().ToArray();
        if (selected.Length == 0 || selected.Any(controller => controller == null))
            throw new ArgumentException(
                "Combat requires non-null participants.",
                nameof(participants)
            );
        if (
            selected.Any(controller =>
                !Combatants.Contains(controller) || !CanParticipate(controller)
            )
        )
            throw new InvalidOperationException(
                "Every participant must be a living registered controller."
            );
        activeCombatants.Clear();
        activeCombatants.AddRange(selected);
        dungeonDirectedCombat = dungeonDirected;
        combatActive = true;
        TurnTaker = null;
        foreach (ActionController controller in activeCombatants)
            controller.ResetEncounterTurnState();
        encounterRules = UnityEncounterRulesBridge.Create(Combatants, "Players");
        encounterRules.TurnBegan += OnTurnBeganCommitted;
        encounterRules.TurnEnded += OnTurnEndedCommitted;
        encounterRules.EncounterEnded += OnEncounterEndedCommitted;
        Pf2eRulesEngine.ApplyCombatStartRules(activeCombatants);
        CombatActivityChanged.Invoke(true);
        OnCombatStart.Invoke();
        BeginEncounterRules(selected);
    }

    private async void BeginEncounterRules(ActionController[] selected)
    {
        EncounterStartOutcome started = await encounterRules.StartEncounter(selected);
        LogInitiative(
            "Initiative Order",
            started
                .State.Roster.Select(entry => encounterRules.GetController(entry.Creature))
                .ToArray()
        );
    }

    private void OnTurnBeganCommitted(TurnIdentity turn)
    {
        if (!combatActive)
            return;
        ActionController actor = encounterRules.GetController(turn.Actor);
        TurnTaker = actor;
        OnNextTurn.Invoke(actor.gameObject);
        if (
            !combatActive
            || !encounterRules.CurrentTurn.HasValue
            || encounterRules.CurrentTurn.Value != turn
            || !CanParticipate(actor)
        )
            return;
        ApplyTurnStartAuras(actor);
        if (
            !combatActive
            || !encounterRules.CurrentTurn.HasValue
            || encounterRules.CurrentTurn.Value != turn
            || !CanParticipate(actor)
        )
            return;
        uint desired = actor.CalculateTurnStartActions();
        uint available = actor.ActionPoints;
        if (desired < available)
            encounterRules.SpendActions(turn.Actor, checked((int)(available - desired)));
        if (CanParticipate(actor))
            actor.StartTurn();
    }

    private void OnTurnEndedCommitted(TurnIdentity turn)
    {
        ActionController actor = encounterRules.GetController(turn.Actor);
        actor.ResetEncounterTurnState();
        if (TurnTaker == actor)
            TurnTaker = null;
    }

    private void OnEncounterEndedCommitted(EncounterOutcome outcome)
    {
        bool wasDungeonDirected = dungeonDirectedCombat;
        string winningTeam =
            outcome == EncounterOutcome.PlayerVictory ? "Players" : OpposingTeamDisplayName();
        Pf2eRulesEngine.EndEncounter(activeCombatants);
        StopCombatState(cancelInFlightActions: false);
        if (wasDungeonDirected)
        {
            DungeonCombatEnded.Invoke(outcome);
            if (outcome == EncounterOutcome.PlayerDefeat)
                OnCombatOutcome.Invoke(false);
        }
        else
        {
            OnCombatEnd.Invoke(winningTeam);
            OnCombatOutcome.Invoke(outcome == EncounterOutcome.PlayerVictory);
        }
    }

    private string OpposingTeamDisplayName()
    {
        EncounterState encounter = encounterRules.Snapshot.Encounters[encounterRules.EncounterId];
        InitiativeEntry opposition = encounter.Roster.FirstOrDefault(entry =>
            entry.Team != encounter.ProtagonistTeam
        );
        return opposition == null
            ? "Opponents"
            : encounterRules.GetTeamDisplayName(opposition.Team);
    }

    private void StopCombatState(bool cancelInFlightActions)
    {
        if (encounterRules != null)
        {
            encounterRules.TurnBegan -= OnTurnBeganCommitted;
            encounterRules.TurnEnded -= OnTurnEndedCommitted;
            encounterRules.EncounterEnded -= OnEncounterEndedCommitted;
        }
        if (cancelInFlightActions)
        {
            foreach (ActionController controller in activeCombatants)
                controller.ResetEncounterTurnState();
        }
        activeCombatants.Clear();
        TurnTaker = null;
        combatActive = false;
        dungeonDirectedCombat = false;
        CombatActivityChanged.Invoke(false);
    }

    private void LogInitiative(string heading, IReadOnlyList<ActionController> order)
    {
        string log = heading + ":\n";
        EncounterState encounter = encounterRules.Snapshot.Encounters[encounterRules.EncounterId];
        for (int index = 0; index < order.Count; index++)
        {
            CreatureId id = encounterRules.GetCreatureId(order[index]);
            InitiativeEntry entry = encounter.Roster.First(value => value.Creature == id);
            log += $"  {index + 1}. {order[index].gameObject.name} (Initiative: {entry.Total})\n";
        }
        CombatLog.GetInstance().Log(log);
    }

    private static bool CanParticipate(ActionController controller) =>
        controller != null
        && controller.gameObject.activeSelf
        && controller.isActiveAndEnabled
        && controller.GetComponent<CreatureComponent>().Health.Current > 0;

    private void ApplyTurnStartAuras(ActionController acting)
    {
        GridAPI grid = UnityEngine.Object.FindFirstObjectByType<GridAPI>();
        if (grid is GridAPIPrivate gridPrivate)
            CreatureAuraResolver.ApplyTurnStartAuras(
                acting,
                activeCombatants,
                gridPrivate.GetTiles()
            );
    }

    public Vector3[] getPoistions() =>
        (combatActive ? activeCombatants : Combatants)
            .Where(value => value != null)
            .Select(value => value.transform.position)
            .ToArray();
}
