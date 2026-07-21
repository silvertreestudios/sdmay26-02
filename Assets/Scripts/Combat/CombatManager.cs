using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
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
    private long encounterGeneration;
    private PendingEncounterCompletion pendingEncounterCompletion;

    /// <summary>Raised with the committed protagonist-relative dungeon outcome.</summary>
    public event Action<EncounterOutcome> DungeonCombatEnded = delegate { };
    public override event Action<bool> CombatActivityChanged = delegate { };

    /// <inheritdoc/>
    public override event Action<long> DungeonCombatStartupAborted = delegate { };
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
            !combatActive
            || encounterRules == null
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
            .Select(entry => encounterRules.GetController(entry.Creature))
            .Where(CanParticipate)
            .Select(controller => controller.gameObject)
            .ToList();
        if (encounter.CurrentTurn.HasValue)
        {
            ActionController currentController = encounterRules.GetController(
                encounter.CurrentTurn.Value.Actor
            );
            if (CanParticipate(currentController))
            {
                GameObject current = currentController.gameObject;
                if (ordered.Remove(current))
                    ordered.Insert(0, current);
            }
        }
        return ordered;
    }

    [ContextMenu("StartCombat")]
    public override void StartCombat() =>
        _ = BeginCombat(Combatants.Where(CanParticipate).ToArray(), false);

    /// <inheritdoc/>
    public override long StartDungeonCombat(IReadOnlyList<ActionController> participants) =>
        BeginCombat(participants, true);

    public override void AddDungeonReinforcements(IReadOnlyList<ActionController> reinforcements)
    {
        if (reinforcements == null)
            throw new ArgumentNullException(nameof(reinforcements));
        if (!combatActive || !dungeonDirectedCombat || pendingEncounterCompletion != null)
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
        UnityEncounterRulesBridge requestingBridge = encounterRules;
        long requestingGeneration = encounterGeneration;
        StartCoroutine(
            AddDungeonReinforcementsRoutine(additions, requestingBridge, requestingGeneration)
        );
    }

    public override void SuspendDungeonCombat()
    {
        if (!combatActive || !dungeonDirectedCombat || pendingEncounterCompletion != null)
            throw new InvalidOperationException(
                "Only an active dungeon-directed combat can be suspended."
            );
        StartCoroutine(SuspendDungeonCombatRoutine(encounterRules, encounterGeneration));
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

    private long BeginCombat(IReadOnlyList<ActionController> participants, bool dungeonDirected)
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
        // A PlayerActionController is the strongest legacy signal for protagonist ownership.
        // Older test scenes and AI-only harnesses do not always provide one, so retain their
        // historical selected-roster semantics by using the first participant's team.
        string protagonistTeam = ResolveProtagonistTeamDisplayName(selected);
        CombatStartupCheckpoint startupCheckpoint = CombatStartupCheckpoint.Capture(selected);
        UnityEncounterRulesBridge startingBridge;
        try
        {
            startingBridge = UnityEncounterRulesBridge.Create(selected, protagonistTeam);
            startingBridge.BeginStartupPresentationTransaction();
        }
        catch
        {
            startupCheckpoint.Restore();
            throw;
        }

        encounterGeneration = checked(encounterGeneration + 1);
        long startingGeneration = encounterGeneration;

        activeCombatants.Clear();
        activeCombatants.AddRange(selected);
        dungeonDirectedCombat = dungeonDirected;
        combatActive = true;
        encounterReady = false;
        foreach (ActionController controller in activeCombatants)
            controller.ResetEncounterTurnState(preserveActionReservation: true);
        encounterRules = startingBridge;
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
            AbortCombatStartup(
                startingBridge,
                startingGeneration,
                dungeonDirected,
                startupCheckpoint
            );
            throw;
        }
        finally
        {
            startupCombatants = Array.Empty<ActionController>();
        }
        StartCoroutine(
            BeginEncounterRules(selected, startingBridge, startingGeneration, startupCheckpoint)
        );
        return startingGeneration;
    }

    private IEnumerator BeginEncounterRules(
        ActionController[] selected,
        UnityEncounterRulesBridge startingBridge,
        long startingGeneration,
        CombatStartupCheckpoint startupCheckpoint
    )
    {
        bool completed = false;
        try
        {
            yield return CoroutineRunner.Await(
                Pf2eRulesEngine.ApplyCombatStartRulesAsync(selected)
            );
            if (!IsCurrentLifecycle(startingBridge, startingGeneration))
                yield break;

            CoroutineResult<EncounterStartOutcome> started = new();
            yield return CoroutineRunner.Await(startingBridge.StartEncounter(selected), started);
            if (!IsCurrentLifecycle(startingBridge, startingGeneration))
                yield break;

            yield return CoroutineRunner.Await(
                startingBridge.CommitStartupPresentationTransactionAsync()
            );
            if (!IsCurrentLifecycle(startingBridge, startingGeneration))
                yield break;

            encounterReady = true;
            LogInitiative(
                "Initiative Order",
                started
                    .Value.State.Roster.Select(entry =>
                        startingBridge.GetController(entry.Creature)
                    )
                    .ToArray()
            );
            startupCheckpoint.Commit();
            completed = true;
        }
        finally
        {
            // CoroutineRunner rethrows the original awaited failure after this finally executes.
            // Roll back only the bridge that owns this startup so a later successful retry cannot
            // be torn down by an obsolete continuation.
            if (!completed && IsCurrentLifecycle(startingBridge, startingGeneration))
                AbortCombatStartup(
                    startingBridge,
                    startingGeneration,
                    dungeonDirectedCombat,
                    startupCheckpoint
                );
            else if (!completed)
                startupCheckpoint.Commit();
        }
    }

    private IEnumerator AddDungeonReinforcementsRoutine(
        ActionController[] additions,
        UnityEncounterRulesBridge joiningBridge,
        long joiningGeneration
    )
    {
        while (IsCurrentLifecycle(joiningBridge, joiningGeneration) && !encounterReady)
            yield return null;
        if (!IsCurrentLifecycle(joiningBridge, joiningGeneration) || !encounterReady)
            yield break;
        CoroutineResult<EncounterJoinOutcome> joined = new();
        yield return CoroutineRunner.Await(
            joiningBridge.JoinEncounter(
                additions,
                () => PublishAcceptedReinforcements(joiningBridge, joiningGeneration, additions)
            ),
            joined
        );
        if (
            !IsCurrentLifecycle(joiningBridge, joiningGeneration)
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
        long joiningGeneration,
        IReadOnlyList<ActionController> additions
    )
    {
        if (
            !IsCurrentLifecycle(joiningBridge, joiningGeneration)
            || !joiningBridge.HasActiveEncounter
        )
            return;
        foreach (ActionController addition in additions)
            if (!activeCombatants.Contains(addition))
                activeCombatants.Add(addition);
    }

    private IEnumerator SuspendDungeonCombatRoutine(
        UnityEncounterRulesBridge suspendingBridge,
        long suspendingGeneration
    )
    {
        while (IsCurrentLifecycle(suspendingBridge, suspendingGeneration) && !encounterReady)
            yield return null;
        if (!IsCurrentLifecycle(suspendingBridge, suspendingGeneration) || !encounterReady)
            yield break;
        yield return CoroutineRunner.Await(
            CompleteDungeonSuspensionAsync(suspendingBridge, suspendingGeneration)
        );
    }

    private async ValueTask CompleteDungeonSuspensionAsync(
        UnityEncounterRulesBridge suspendingBridge,
        long suspendingGeneration
    )
    {
        if (!IsCurrentLifecycle(suspendingBridge, suspendingGeneration))
            return;
        List<Exception> failures = new();
        try
        {
            await suspendingBridge.SuspendEncounter();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (!IsCurrentLifecycle(suspendingBridge, suspendingGeneration))
            return;

        bool suspensionCommitted =
            suspendingBridge.Snapshot.Encounters.TryGet(
                suspendingBridge.EncounterId,
                out EncounterState encounter
            )
            && encounter.Phase == EncounterPhase.Suspended;
        if (!suspensionCommitted)
        {
            ThrowCompletionFailures(failures);
            return;
        }

        // A post-commit observer can fail the suspension task even though the encounter is already
        // durably closed. Cleanup and exact-host finalization remain mandatory in that case.
        ActionController[] suspendingCombatants = ResolveSuspensionCleanupCombatants(
            suspendingBridge,
            encounter
        );
        try
        {
            await Pf2eRulesEngine.EndEncounterAsync(suspendingCombatants);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        if (!IsCurrentLifecycle(suspendingBridge, suspendingGeneration))
            return;
        TryComplete(
            () => FinalizeCombatState(suspendingBridge, cancelInFlightActions: true),
            failures
        );
        ThrowCompletionFailures(failures);
    }

    private ActionController[] ResolveSuspensionCleanupCombatants(
        UnityEncounterRulesBridge suspendingBridge,
        EncounterState suspendedEncounter
    )
    {
        HashSet<ActionController> acceptedControllers = new(activeCombatants);
        return suspendedEncounter
            .Roster.Select(entry => suspendingBridge.GetController(entry.Creature))
            .Where(acceptedControllers.Contains)
            .Distinct()
            .ToArray();
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
            StartCoroutine(CloseUnpresentableTurn(encounterRules, encounterGeneration, turn));
            return;
        }
        actor.StartTurn();
    }

    private IEnumerator CloseUnpresentableTurn(
        UnityEncounterRulesBridge turnBridge,
        long turnGeneration,
        TurnIdentity turn
    )
    {
        // Return from the presentation callback before starting another dispatcher root. The
        // exact identity check then makes this scheduled cleanup harmless if another path already
        // closed the turn while presentation was settling.
        yield return null;
        if (
            !IsCurrentLifecycle(turnBridge, turnGeneration)
            || !turnBridge.CurrentTurn.HasValue
            || turnBridge.CurrentTurn.Value != turn
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
        if (!actor.TryReserveAction(out ActionReservationToken reservation))
        {
            pendingTurnEnd = null;
            return;
        }
        StartCoroutine(
            EndReservedTurn(encounterRules, encounterGeneration, turn, actor, reservation)
        );
    }

    private IEnumerator EndReservedTurn(
        UnityEncounterRulesBridge turnBridge,
        long turnGeneration,
        TurnIdentity turn,
        ActionController actor,
        ActionReservationToken reservation
    )
    {
        try
        {
            yield return CoroutineRunner.Await(turnBridge.EndTurn(turn));
        }
        finally
        {
            if (
                IsCurrentLifecycle(turnBridge, turnGeneration)
                && pendingTurnEnd.HasValue
                && pendingTurnEnd.Value == turn
            )
            {
                pendingTurnEnd = null;
                actor.ReleaseActionReservation(reservation);
            }
        }
    }

    private void OnTurnEndedCommitted(TurnIdentity turn)
    {
        ActionController actor = encounterRules.GetController(turn.Actor);
        actor.ResetEncounterTurnState(preserveActionReservation: true);
    }

    private ValueTask OnEncounterEndedCommitted(EncounterOutcome outcome)
    {
        if (pendingEncounterCompletion != null)
            throw new InvalidOperationException(
                "Only one committed encounter completion may await action settlement."
            );
        UnityEncounterRulesBridge endingBridge = encounterRules;
        long endingGeneration = encounterGeneration;
        ActionController[] endingCombatants = activeCombatants.ToArray();
        bool wasDungeonDirected = dungeonDirectedCombat;
        string winningTeam =
            outcome == EncounterOutcome.PlayerVictory
                ? ProtagonistTeamDisplayName()
                : OpposingTeamDisplayName();
        PendingEncounterCompletion completion = new(
            endingBridge,
            endingGeneration,
            endingCombatants,
            wasDungeonDirected,
            winningTeam,
            outcome
        );
        foreach (ActionController controller in endingCombatants)
        {
            if (!controller.TryGetCurrentActionReservation(out ActionReservationToken reservation))
                continue;
            completion.Reservations.Add(controller, reservation);
            controller.ActionReservationSettled += OnActionReservationSettled;
        }

        if (completion.Reservations.Count == 0)
            return CompleteEncounterHostAsync(completion);
        pendingEncounterCompletion = completion;
        return default;
    }

    private void OnActionReservationSettled(
        ActionController controller,
        ActionReservationToken reservation
    )
    {
        PendingEncounterCompletion completion = pendingEncounterCompletion;
        if (
            completion == null
            || !completion.Reservations.TryGetValue(
                controller,
                out ActionReservationToken expectedReservation
            )
            || expectedReservation != reservation
        )
            return;
        controller.ActionReservationSettled -= OnActionReservationSettled;
        completion.Reservations.Remove(controller);
        if (completion.Reservations.Count != 0 || completion.Started)
            return;
        completion.Started = true;
        StartCoroutine(CompleteEncounterAfterActionSettlement(completion));
    }

    private IEnumerator CompleteEncounterAfterActionSettlement(
        PendingEncounterCompletion completion
    )
    {
        // The reservation notification is the final observable mutation in each owning action,
        // but it fires from inside that finalizer. Yield once so the coroutine/async caller can
        // unwind before host callbacks may rebind or destroy its Unity objects. The exact bridge
        // generation makes this scheduled continuation inert after any lifecycle replacement.
        yield return null;
        if (
            !ReferenceEquals(pendingEncounterCompletion, completion)
            || !IsCurrentLifecycle(completion.Bridge, completion.Generation)
        )
            yield break;
        yield return CoroutineRunner.Await(CompleteEncounterHostAsync(completion));
    }

    private async ValueTask CompleteEncounterHostAsync(PendingEncounterCompletion completion)
    {
        if (!IsCurrentLifecycle(completion.Bridge, completion.Generation))
            return;
        List<Exception> failures = new();
        try
        {
            await Pf2eRulesEngine.EndEncounterAsync(completion.Combatants);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (!IsCurrentLifecycle(completion.Bridge, completion.Generation))
        {
            ThrowCompletionFailures(failures);
            return;
        }

        // EncounterEnded is already authoritative before this presentation callback begins. Host
        // shutdown and result publication are therefore completion work, not contingent cleanup:
        // every channel must run once even when an earlier cleanup or observer callback fails.
        TryComplete(
            () => FinalizeCombatState(completion.Bridge, cancelInFlightActions: false),
            failures
        );
        if (completion.WasDungeonDirected)
        {
            InvokeEach(DungeonCombatEnded, completion.Outcome, failures);
            if (completion.Outcome == EncounterOutcome.PlayerDefeat)
                TryComplete(() => OnCombatOutcome.Invoke(false), failures);
        }
        else
        {
            TryComplete(() => OnCombatEnd.Invoke(completion.WinningTeam), failures);
            TryComplete(
                () => OnCombatOutcome.Invoke(completion.Outcome == EncounterOutcome.PlayerVictory),
                failures
            );
        }

        ThrowCompletionFailures(failures);
    }

    private static void TryComplete(Action callback, ICollection<Exception> failures)
    {
        try
        {
            callback();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static void InvokeEach(
        Action<EncounterOutcome> callbacks,
        EncounterOutcome outcome,
        ICollection<Exception> failures
    )
    {
        foreach (Action<EncounterOutcome> callback in callbacks.GetInvocationList())
            TryComplete(() => callback(outcome), failures);
    }

    private static void ThrowCompletionFailures(IReadOnlyList<Exception> failures)
    {
        if (failures.Count == 0)
            return;
        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        throw new AggregateException(
            "Encounter completion failed while settling cleanup or notifications.",
            failures
        );
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

    private string ProtagonistTeamDisplayName()
    {
        EncounterState encounter = encounterRules.Snapshot.Encounters[encounterRules.EncounterId];
        return encounterRules.GetTeamDisplayName(encounter.ProtagonistTeam);
    }

    private static string ResolveProtagonistTeamDisplayName(
        IReadOnlyList<ActionController> selected
    )
    {
        ActionController protagonist =
            selected.FirstOrDefault(controller => controller is PlayerActionController)
            ?? selected[0];
        Team team = protagonist.GetComponent<Team>();
        return team == null || string.IsNullOrWhiteSpace(team.Name)
            ? "Unassigned"
            : team.Name.Trim();
    }

    private void AbortCombatStartup(
        UnityEncounterRulesBridge startingBridge,
        long startingGeneration,
        bool wasDungeonDirected,
        CombatStartupCheckpoint startupCheckpoint
    )
    {
        if (!ReferenceEquals(encounterRules, startingBridge))
            return;
        if (wasDungeonDirected)
            NotifyDungeonCombatStartupAborted(startingGeneration);
        try
        {
            StopCombatState(cancelInFlightActions: false);
        }
        finally
        {
            try
            {
                startingBridge.DiscardStartupPresentationTransaction();
                startingBridge.ReleaseHostOwnership();
            }
            finally
            {
                encounterRules = null;
                startupCheckpoint.Restore();
            }
        }
    }

    private void NotifyDungeonCombatStartupAborted(long generation)
    {
        foreach (Action<long> callback in DungeonCombatStartupAborted.GetInvocationList())
        {
            try
            {
                callback(generation);
            }
            catch (Exception exception)
            {
                // Startup rollback must remain complete even if a lifecycle owner has a bug. The
                // original startup fault continues through its coroutine while this secondary
                // notification fault remains visible in the Unity log.
                Debug.LogException(exception);
            }
        }
    }

    private void StopCombatState(bool cancelInFlightActions)
    {
        // Synchronous startup observers must never retain their selected-only projection after a
        // failed event, normal encounter cleanup, or a later exploration transition.
        startupCombatants = Array.Empty<ActionController>();
        ClearPendingEncounterCompletion();
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
        pendingTurnEnd = null;
        combatActive = false;
        encounterReady = false;
        dungeonDirectedCombat = false;
        CombatActivityChanged.Invoke(false);
    }

    private void FinalizeCombatState(
        UnityEncounterRulesBridge completingBridge,
        bool cancelInFlightActions
    )
    {
        if (!combatActive || !ReferenceEquals(encounterRules, completingBridge))
            return;
        StopCombatState(cancelInFlightActions);
    }

    private bool IsCurrentLifecycle(UnityEncounterRulesBridge bridge, long generation) =>
        combatActive
        && generation == encounterGeneration
        && ReferenceEquals(encounterRules, bridge);

    private void ClearPendingEncounterCompletion()
    {
        if (pendingEncounterCompletion == null)
            return;
        foreach (ActionController controller in pendingEncounterCompletion.Reservations.Keys)
            controller.ActionReservationSettled -= OnActionReservationSettled;
        pendingEncounterCompletion = null;
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

    /// <summary>Gets positions used to frame living, participating combatants in the camera.</summary>
    /// <returns>
    /// Positions in the same deterministic gameplay order as <see cref="GetCombatants"/>.
    /// Defeated encounter entries remain in the authoritative roster but are not camera targets.
    /// </returns>
    public Vector3[] getPoistions() =>
        GetCombatants().Select(value => value.transform.position).ToArray();

    private sealed class PendingEncounterCompletion
    {
        internal PendingEncounterCompletion(
            UnityEncounterRulesBridge bridge,
            long generation,
            ActionController[] combatants,
            bool wasDungeonDirected,
            string winningTeam,
            EncounterOutcome outcome
        )
        {
            Bridge = bridge;
            Generation = generation;
            Combatants = combatants;
            WasDungeonDirected = wasDungeonDirected;
            WinningTeam = winningTeam;
            Outcome = outcome;
        }

        internal UnityEncounterRulesBridge Bridge { get; }
        internal long Generation { get; }
        internal ActionController[] Combatants { get; }
        internal bool WasDungeonDirected { get; }
        internal string WinningTeam { get; }
        internal EncounterOutcome Outcome { get; }
        internal Dictionary<ActionController, ActionReservationToken> Reservations { get; } = new();
        internal bool Started { get; set; }
    }

    private sealed class CombatStartupCheckpoint
    {
        // Initial participants are not durably in combat until passive hooks and StartEncounter
        // settle. This host memento complements discarding the failed bridge's authoritative store.
        private readonly Entry[] entries;
        private bool settled;

        private CombatStartupCheckpoint(Entry[] entries) => this.entries = entries;

        internal static CombatStartupCheckpoint Capture(
            IReadOnlyList<ActionController> controllers
        ) =>
            new CombatStartupCheckpoint(
                controllers.Select(controller => new Entry(controller)).ToArray()
            );

        internal void Commit() => settled = true;

        internal void Restore()
        {
            if (settled)
                return;
            settled = true;
            foreach (Entry entry in entries)
                entry.Restore();
        }

        private sealed class Entry
        {
            private readonly ActionController controller;
            private readonly ActionControllerEncounterState actionState;
            private readonly CreatureComponent creature;
            private readonly CreatureEncounterState creatureState;
            private readonly Conditions conditions;
            private readonly bool hadConditions;
            private readonly IReadOnlyDictionary<
                string,
                IReadOnlyList<ConditionSource>
            > conditionState;

            internal Entry(ActionController controller)
            {
                this.controller = controller;
                actionState = controller.CaptureEncounterStartupState();
                creature = controller.GetComponent<CreatureComponent>();
                creatureState = creature.CaptureEncounterStartupState();
                conditions = controller.GetComponent<Conditions>();
                hadConditions = conditions != null;
                conditionState = conditions?.CaptureEncounterStartupState();
            }

            internal void Restore()
            {
                creature.RestoreEncounterStartupState(creatureState);
                controller.RestoreEncounterStartupState(actionState);
                if (hadConditions)
                    conditions.RestoreEncounterStartupState(conditionState);
                else
                {
                    Conditions createdConditions = controller.GetComponent<Conditions>();
                    if (createdConditions != null)
                    {
                        // This component is owned entirely by the failed transaction. Delayed
                        // destruction would let an immediate retry discover its stale conditions
                        // before Unity's end-of-frame cleanup.
                        UnityEngine.Object.DestroyImmediate(createdConditions);
                    }
                }
            }
        }
    }
}
