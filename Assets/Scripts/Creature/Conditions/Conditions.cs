using System;
using System.Collections.Generic;
using System.Linq;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using UnityEngine;

/// <summary>Accepts one-shot restore input and projects authoritative store conditions read-only.</summary>
/// <remarks>
/// Pending restore input is consumed after successful enrollment and is never exposed as live
/// condition state. Once detached, projections fail closed and persistence capture reports that
/// authoritative state is unavailable.
/// </remarks>
public sealed class Conditions : MonoBehaviour
{
    private IReadOnlyList<ConditionApplicationSnapshot> pending = Array.AsReadOnly(
        Array.Empty<ConditionApplicationSnapshot>()
    );
    private long pendingGeneration;
    private bool hasPending;
    private bool wasEnrolled;

    /// <summary>Gets canonical active condition slugs from the attached authoritative store.</summary>
    public IReadOnlyCollection<string> ActiveConditionNames
    {
        get
        {
            if (!TryGetAuthority(out UnityCombatRulesBridge bridge, out CreatureId owner))
                return Array.Empty<string>();
            return ConditionSelectors.GetActiveSlugs(bridge.Snapshot, owner);
        }
    }

    internal IReadOnlyList<ConditionApplicationSnapshot> CaptureApplications()
    {
        if (!TryGetAuthority(out UnityCombatRulesBridge bridge, out CreatureId owner))
        {
            if (!wasEnrolled && !hasPending)
                return Array.AsReadOnly(Array.Empty<ConditionApplicationSnapshot>());
            throw new InvalidOperationException(
                "Condition persistence capture requires attached authoritative combat rules."
            );
        }

        List<ConditionApplicationSnapshot> captured = new List<ConditionApplicationSnapshot>();
        foreach (
            ActiveRuleBinding binding in bridge
                .Snapshot.RuleBindings.Select(pair => pair.Value)
                .Where(binding => binding.Owner == owner && binding.EffectId.HasValue)
                .OrderBy(binding => binding.CreationOrder)
                .ThenBy(binding => binding.Id.Value, StringComparer.Ordinal)
                .ThenBy(binding => binding.EffectId.Value.Value, StringComparer.Ordinal)
        )
        {
            if (!ConditionRuleDefinitions.IsConditionDefinition(binding.DefinitionId))
                continue;
            if (
                !bridge.Snapshot.ActiveEffects.TryGet(
                    binding.EffectId.Value,
                    out ActiveEffectInstance effect
                )
            )
                throw new InvalidOperationException(
                    $"Condition binding {binding.Id.Value} has no authoritative effect."
                );
            if (
                effect.DefinitionId != binding.DefinitionId
                || effect.Source != binding.Source
                || effect.Id != binding.EffectId.Value
                || !ConditionRuleDefinitions.Accepts(effect.DefinitionId, effect.State)
            )
                throw new InvalidOperationException(
                    $"Condition binding {binding.Id.Value} does not match its authoritative effect."
                );
            bridge.Snapshot.ActiveEffectTimings.TryGet(
                effect.Id,
                out ActiveEffectTimingState timing
            );
            captured.Add(
                new ConditionApplicationSnapshot(
                    effect.Id,
                    binding.Id,
                    effect.DefinitionId,
                    bridge.GetController(effect.SourceCreature).gameObject,
                    effect.Source,
                    effect.Duration,
                    effect.EffectStateVersion,
                    effect.State,
                    effect.Status,
                    binding.CreationOrder,
                    binding.IsEnabled,
                    timing == null
                        ? null
                        : new ConditionTimingSnapshot(
                            timing.RemainingBoundaries,
                            timing.ExpiresWithEncounter
                        )
                )
            );
        }
        return Array.AsReadOnly(captured.ToArray());
    }

    internal void RestoreApplications(IEnumerable<ConditionApplicationSnapshot> applications)
    {
        if (applications == null)
            throw new ArgumentNullException(nameof(applications));
        if (TryGetAuthority(out _, out _))
            throw new InvalidOperationException(
                "Restore input cannot replace conditions while authoritative combat rules are attached."
            );
        ConditionApplicationSnapshot[] copied = applications.ToArray();
        if (copied.Any(application => application == null))
            throw new ArgumentException(
                "Restored condition applications cannot contain null.",
                nameof(applications)
            );
        if (
            copied.Select(application => application.EffectId).Distinct().Count() != copied.Length
            || copied.Select(application => application.BindingId).Distinct().Count()
                != copied.Length
        )
            throw new ArgumentException(
                "Restored condition applications require unique stable identities.",
                nameof(applications)
            );
        pending = Array.AsReadOnly(copied);
        pendingGeneration = checked(pendingGeneration + 1);
        hasPending = copied.Length > 0;
    }

    internal bool TryPrepareRestore(
        CreatureId owner,
        EncounterId encounter,
        Func<GameObject, CreatureId> resolveSource,
        out ConditionRestoreLease lease
    )
    {
        if (owner.IsEmpty || encounter.IsEmpty)
            throw new ArgumentException("Condition enrollment requires complete stable identity.");
        if (resolveSource == null)
            throw new ArgumentNullException(nameof(resolveSource));
        if (!hasPending)
        {
            lease = null;
            return false;
        }

        ConditionRegistration[] registrations = pending
            .Select(application =>
                application.CreateRegistration(
                    owner,
                    resolveSource(application.SourceCreature),
                    encounter
                )
            )
            .ToArray();
        lease = new ConditionRestoreLease(this, pendingGeneration, registrations);
        return true;
    }

    private void ConsumePending(long generation)
    {
        if (!hasPending || generation != pendingGeneration)
            throw new InvalidOperationException(
                "Condition restore input changed during enrollment."
            );
        pending = Array.AsReadOnly(Array.Empty<ConditionApplicationSnapshot>());
        hasPending = false;
    }

    internal void CompleteEnrollment() => wasEnrolled = true;

    private bool TryGetAuthority(out UnityCombatRulesBridge bridge, out CreatureId owner)
    {
        ActionController controller = GetComponent<ActionController>();
        if (controller != null && controller.TryGetCombatRules(out bridge, out owner))
            return true;
        bridge = null;
        owner = default;
        return false;
    }

    internal sealed class ConditionRestoreLease
    {
        private readonly Conditions owner;
        private readonly long generation;

        internal ConditionRestoreLease(
            Conditions owner,
            long generation,
            IReadOnlyList<ConditionRegistration> registrations
        )
        {
            this.owner = owner;
            this.generation = generation;
            Registrations = registrations;
        }

        internal IReadOnlyList<ConditionRegistration> Registrations { get; }

        internal void Consume() => owner.ConsumePending(generation);
    }
}

/// <summary>One exact condition instance at the Unity persistence boundary.</summary>
internal sealed class ConditionApplicationSnapshot
{
    internal ConditionApplicationSnapshot(
        ActiveEffectId effectId,
        BindingId bindingId,
        RuleDefinitionId definitionId,
        GameObject sourceCreature,
        RuleSource source,
        EffectDuration duration,
        EffectStateVersion version,
        IEffectState state,
        ActiveEffectStatus status,
        long creationOrder,
        bool bindingEnabled,
        ConditionTimingSnapshot timing
    )
    {
        if (effectId.IsEmpty || bindingId.IsEmpty || definitionId.IsEmpty || source.IsEmpty)
            throw new ArgumentException("A persisted condition requires complete stable identity.");
        if (sourceCreature == null)
            throw new ArgumentNullException(nameof(sourceCreature));
        if (!ConditionRuleDefinitions.Accepts(definitionId, state))
            throw new ArgumentException(
                "Persisted state does not match its condition definition.",
                nameof(state)
            );
        if ((status == ActiveEffectStatus.Active) != bindingEnabled)
            throw new ArgumentException("Persisted condition status and binding state must agree.");
        if (
            timing != null
            && (
                status != ActiveEffectStatus.Active
                || duration.Kind == EffectDurationKind.Indefinite
            )
        )
            throw new ArgumentException("Only an active finite condition may retain timing.");
        if (
            timing != null
            && timing.ExpiresWithEncounter != (duration.Kind == EffectDurationKind.Encounter)
        )
            throw new ArgumentException("Persisted condition duration and timing disagree.");
        if (creationOrder < 0)
            throw new ArgumentOutOfRangeException(nameof(creationOrder));

        EffectId = effectId;
        BindingId = bindingId;
        DefinitionId = definitionId;
        SourceCreature = sourceCreature;
        Source = source;
        Duration = duration;
        Version = version;
        State = state;
        Status = status;
        CreationOrder = creationOrder;
        BindingEnabled = bindingEnabled;
        Timing = timing;
    }

    internal ActiveEffectId EffectId { get; }
    internal BindingId BindingId { get; }
    internal RuleDefinitionId DefinitionId { get; }
    internal GameObject SourceCreature { get; }
    internal RuleSource Source { get; }
    internal EffectDuration Duration { get; }
    internal EffectStateVersion Version { get; }
    internal IEffectState State { get; }
    internal ActiveEffectStatus Status { get; }
    internal long CreationOrder { get; }
    internal bool BindingEnabled { get; }
    internal ConditionTimingSnapshot Timing { get; }

    internal ConditionRegistration CreateRegistration(
        CreatureId owner,
        CreatureId sourceCreature,
        EncounterId encounter
    )
    {
        ActiveEffectInstance effect = new ActiveEffectInstance(
            EffectId,
            DefinitionId,
            sourceCreature,
            Source,
            Duration,
            State,
            Version,
            Status
        );
        ActiveRuleBinding binding = new ActiveRuleBinding(
            BindingId,
            DefinitionId,
            owner,
            EffectId,
            Source,
            CreationOrder,
            BindingEnabled
        );
        ActiveEffectTimingState timing =
            Timing == null
                ? null
                : new ActiveEffectTimingState(
                    EffectId,
                    encounter,
                    BindingId,
                    sourceCreature,
                    Timing.RemainingBoundaries,
                    Timing.ExpiresWithEncounter,
                    CreationOrder
                );
        return new ConditionRegistration(effect, binding, timing);
    }
}

/// <summary>Exact remaining encounter-clock state for one persisted finite condition.</summary>
internal sealed class ConditionTimingSnapshot
{
    internal ConditionTimingSnapshot(int remainingBoundaries, bool expiresWithEncounter)
    {
        if (remainingBoundaries < 0)
            throw new ArgumentOutOfRangeException(nameof(remainingBoundaries));
        RemainingBoundaries = remainingBoundaries;
        ExpiresWithEncounter = expiresWithEncounter;
    }

    internal int RemainingBoundaries { get; }
    internal bool ExpiresWithEncounter { get; }
}
