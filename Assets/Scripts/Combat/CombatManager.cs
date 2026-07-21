using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
    private readonly List<ActionController> activeCombatants = new();
    private IReadOnlyList<ActionController> startupCombatants = Array.Empty<ActionController>();
    private bool combatActive;
    private bool encounterReady;
    private bool dungeonDirectedCombat;
    private UnityEncounterRulesBridge encounterRules;
    private TurnIdentity? pendingTurnEnd;

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

    public override GameObject WhosTurn()
    {
        if (encounterRules?.CurrentTurn is not TurnIdentity turn)
            return null;
        return encounterRules.GetController(turn.Actor).gameObject;
    }

    public override List<GameObject> GetCombatants()
    {
        if (startupCombatants.Count > 0)
            return startupCombatants
                .Where(CanParticipate)
                .Select(value => value.gameObject)
                .ToList();
        if (
            encounterRules == null
            || !encounterRules.Snapshot.Encounters.TryGet(
                encounterRules.EncounterId,
                out EncounterState encounter
            )
        )
            return Combatants.Where(CanParticipate).Select(value => value.gameObject).ToList();
        List<GameObject> ordered = encounter
            .Roster.Where(entry =>
                encounterRules.Snapshot.Health.TryGet(entry.Creature, out HealthState health)
                && health.Current > 0
            )
            .Select(entry => encounterRules.GetController(entry.Creature).gameObject)
            .ToList();
        if (encounter.CurrentTurn.HasValue)
        {
            GameObject current = encounterRules
                .GetController(encounter.CurrentTurn.Value.Actor)
                .gameObject;
            if (ordered.Remove(current))
                ordered.Insert(0, current);
        }
        return ordered;
    }

    [ContextMenu("StartCombat")]
    public override void StartCombat() =>
        BeginCombat(Combatants.Where(CanParticipate).ToArray(), false);

    public override void StartDungeonCombat(IReadOnlyList<ActionController> participants) =>
        BeginCombat(participants, true);

    public override void AddDungeonReinforcements(IReadOnlyList<ActionController> reinforcements)
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
        StartCoroutine(AddDungeonReinforcementsRoutine(additions));
    }

    public override void SuspendDungeonCombat()
    {
        if (!combatActive || !dungeonDirectedCombat)
            throw new InvalidOperationException(
                "Only an active dungeon-directed combat can be suspended."
            );
        StartCoroutine(SuspendDungeonCombatRoutine());
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

    public override void NextTurn()
    {
        if (combatActive && encounterRules.CurrentTurn.HasValue)
            RequestTurnEnd(encounterRules.CurrentTurn.Value);
    }

    public override void EndCurrentTurn(ActionController actor)
    {
        if (actor == null)
            throw new ArgumentNullException(nameof(actor));
        if (
            !combatActive
            || !encounterRules.CurrentTurn.HasValue
            || encounterRules.CurrentTurn.Value.Actor != encounterRules.GetCreatureId(actor)
        )
            return;
        RequestTurnEnd(encounterRules.CurrentTurn.Value);
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
        encounterReady = false;
        foreach (ActionController controller in activeCombatants)
            controller.ResetEncounterTurnState();
        encounterRules = UnityEncounterRulesBridge.Create(selected, "Players");
        encounterRules.TurnBegan += OnTurnBeganCommitted;
        encounterRules.TurnEnded += OnTurnEndedCommitted;
        encounterRules.EncounterEnded += OnEncounterEndedCommitted;
        startupCombatants = selected;
        try
        {
            CombatActivityChanged.Invoke(true);
            OnCombatStart.Invoke();
        }
        catch
        {
            StopCombatState(cancelInFlightActions: true);
            throw;
        }
        finally
        {
            startupCombatants = Array.Empty<ActionController>();
        }
        StartCoroutine(BeginEncounterRules(selected));
    }

    private IEnumerator BeginEncounterRules(ActionController[] selected)
    {
        yield return CoroutineRunner.Await(
            Pf2eRulesEngine.ApplyCombatStartRulesAsync(activeCombatants)
        );
        CoroutineResult<EncounterStartOutcome> started = new();
        yield return CoroutineRunner.Await(encounterRules.StartEncounter(selected), started);
        encounterReady = true;
        LogInitiative(
            "Initiative Order",
            started
                .Value.State.Roster.Select(entry => encounterRules.GetController(entry.Creature))
                .ToArray()
        );
    }

    private IEnumerator AddDungeonReinforcementsRoutine(ActionController[] additions)
    {
        while (combatActive && !encounterReady)
            yield return null;
        if (!combatActive)
            yield break;
        UnityEncounterRulesBridge joiningBridge = encounterRules;
        CoroutineResult<EncounterJoinOutcome> joined = new();
        yield return CoroutineRunner.Await(
            joiningBridge.JoinEncounter(
                additions,
                () => PublishAcceptedReinforcements(joiningBridge, additions)
            ),
            joined
        );
        if (
            !combatActive
            || !ReferenceEquals(encounterRules, joiningBridge)
            || !joiningBridge.HasActiveEncounter
        )
            yield break;
        HashSet<ActionController> accepted = new(additions);
        ActionController[] acceptedOrder = joined
            .Value.State.Roster.Select(entry => joiningBridge.GetController(entry.Creature))
            .Where(accepted.Contains)
            .ToArray();
        LogInitiative("Reinforcements", acceptedOrder);
    }

    private void PublishAcceptedReinforcements(
        UnityEncounterRulesBridge joiningBridge,
        IReadOnlyList<ActionController> additions
    )
    {
        if (
            !combatActive
            || !ReferenceEquals(encounterRules, joiningBridge)
            || !joiningBridge.HasActiveEncounter
        )
            return;
        foreach (ActionController addition in additions)
            if (!activeCombatants.Contains(addition))
                activeCombatants.Add(addition);
    }

    private IEnumerator SuspendDungeonCombatRoutine()
    {
        while (combatActive && !encounterReady)
            yield return null;
        if (!combatActive)
            yield break;
        yield return CoroutineRunner.Await(encounterRules.SuspendEncounter());
        yield return CoroutineRunner.Await(Pf2eRulesEngine.EndEncounterAsync(activeCombatants));
        StopCombatState(cancelInFlightActions: true);
    }

    private void OnTurnBeganCommitted(TurnIdentity turn)
    {
        if (!combatActive)
            return;
        ActionController actor = encounterRules.GetController(turn.Actor);
        OnNextTurn.Invoke(actor.gameObject);
        if (
            !combatActive
            || !encounterRules.CurrentTurn.HasValue
            || encounterRules.CurrentTurn.Value != turn
        )
            return;
        if (!CanParticipate(actor))
        {
            StartCoroutine(CloseUnpresentableTurn(turn));
            return;
        }
        actor.StartTurn();
    }

    private IEnumerator CloseUnpresentableTurn(TurnIdentity turn)
    {
        // Return from the presentation callback before starting another dispatcher root. The
        // exact identity check then makes this scheduled cleanup harmless if another path already
        // closed the turn while presentation was settling.
        yield return null;
        if (
            !combatActive
            || !encounterRules.CurrentTurn.HasValue
            || encounterRules.CurrentTurn.Value != turn
        )
            yield break;
        RequestTurnEnd(turn);
    }

    private void RequestTurnEnd(TurnIdentity turn)
    {
        if (
            !combatActive
            || pendingTurnEnd.HasValue
            || !encounterRules.CurrentTurn.HasValue
            || encounterRules.CurrentTurn.Value != turn
        )
            return;

        ActionController actor = encounterRules.GetController(turn.Actor);
        if (actor.IsTakingAction)
            return;

        // Reserve both the exact reducer turn and the Unity action surface before the dispatcher
        // can yield. Repeated end requests and actions must not queue behind the same stale turn.
        pendingTurnEnd = turn;
        actor.IsTakingAction = true;
        StartCoroutine(EndReservedTurn(turn, actor));
    }

    private IEnumerator EndReservedTurn(TurnIdentity turn, ActionController actor)
    {
        try
        {
            yield return CoroutineRunner.Await(encounterRules.EndTurn(turn));
        }
        finally
        {
            if (pendingTurnEnd.HasValue && pendingTurnEnd.Value == turn)
                pendingTurnEnd = null;
            actor.IsTakingAction = false;
        }
    }

    private void OnTurnEndedCommitted(TurnIdentity turn)
    {
        ActionController actor = encounterRules.GetController(turn.Actor);
        actor.ResetEncounterTurnState();
    }

    private async ValueTask OnEncounterEndedCommitted(EncounterOutcome outcome)
    {
        bool wasDungeonDirected = dungeonDirectedCombat;
        string winningTeam =
            outcome == EncounterOutcome.PlayerVictory ? "Players" : OpposingTeamDisplayName();
        await Pf2eRulesEngine.EndEncounterAsync(activeCombatants);
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
        RulesSnapshot snapshot = encounterRules.Snapshot;
        EncounterState encounter = snapshot.Encounters[encounterRules.EncounterId];
        // Initiative order is the encounter's deterministic cross-team tie breaker. Only living
        // entries may supply a concrete winner; simultaneous defeat uses the neutral fallback.
        InitiativeEntry opposition = encounter.Roster.FirstOrDefault(entry =>
            entry.Team != encounter.ProtagonistTeam
            && snapshot.Health.TryGet(entry.Creature, out HealthState health)
            && health.Current > 0
        );
        return opposition == null
            ? "Opponents"
            : encounterRules.GetTeamDisplayName(opposition.Team);
    }

    private void StopCombatState(bool cancelInFlightActions)
    {
        // Synchronous startup observers must never retain their selected-only projection after a
        // failed event, normal encounter cleanup, or a later exploration transition.
        startupCombatants = Array.Empty<ActionController>();
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
        combatActive = false;
        encounterReady = false;
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

    /// <summary>Gets positions used to frame the living combatants in the camera.</summary>
    /// <returns>
    /// Positions in the same deterministic gameplay order as <see cref="GetCombatants"/>.
    /// Defeated encounter entries remain in the authoritative roster but are not camera targets.
    /// </returns>
    public Vector3[] getPoistions() =>
        GetCombatants().Select(value => value.transform.position).ToArray();
}
