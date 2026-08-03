using System;
using System.Collections.Generic;
using System.Linq;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;
using UnityEngine;

/// <summary>Owns one-shot condition input and read-only live or detached persistence projection.</summary>
/// <remarks>
/// Live reads use the attached rules store. Encounter release projects one final immutable set
/// before detachment; that detached set is valid persistence and next-enrollment input even when
/// empty. It is consumed only after a complete enrollment batch succeeds.
/// </remarks>
public sealed class Conditions : MonoBehaviour
{
    private static readonly IReadOnlyList<ConditionApplicationSnapshot> EmptyApplications =
        Array.AsReadOnly(Array.Empty<ConditionApplicationSnapshot>());
    private readonly PendingImmutableValue<IReadOnlyList<ConditionApplicationSnapshot>> detached =
        new(EmptyApplications, "Condition");

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
            return detached.ReadDetached();

        return CaptureAuthoritative(bridge, owner);
    }

    internal void ProjectDetachedApplications(
        UnityCombatRulesBridge expectedBridge,
        CreatureId expectedOwner
    )
    {
        if (
            !TryGetAuthority(out UnityCombatRulesBridge bridge, out CreatureId owner)
            || !ReferenceEquals(bridge, expectedBridge)
            || owner != expectedOwner
        )
            throw new InvalidOperationException(
                "Condition detachment projection requires the exact attached combat rules."
            );
        detached.Replace(CaptureAuthoritative(bridge, owner));
    }

    private static IReadOnlyList<ConditionApplicationSnapshot> CaptureAuthoritative(
        UnityCombatRulesBridge bridge,
        CreatureId owner
    )
    {
        string durableOwner = bridge.GetDurableActorId(owner);
        if (string.IsNullOrEmpty(durableOwner))
            return EmptyApplications;
        if (!DurableActorSourceIdentity.IsCanonical(durableOwner))
            throw new InvalidOperationException(
                $"Condition owner {owner.Value} has noncanonical durable actor provenance."
            );

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
            string sourceActorId = bridge.GetDurableActorId(effect.SourceCreature);
            if (!DurableActorSourceIdentity.IsCanonical(sourceActorId))
                throw new InvalidOperationException(
                    $"Condition {effect.Id.Value} has no canonical durable source actor provenance."
                );
            captured.Add(
                new ConditionApplicationSnapshot(
                    effect.Id,
                    binding.Id,
                    effect.DefinitionId,
                    sourceActorId,
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
        detached.Replace(Array.AsReadOnly(copied));
    }

    internal bool TryPrepareRestore(
        CreatureId owner,
        EncounterId encounter,
        Func<string, DurableActorSourceResolution> resolveSource,
        out PendingImmutableValueLease<IReadOnlyList<ConditionApplicationSnapshot>> lease,
        out IReadOnlyList<ActiveEffectRegistration> registrations
    )
    {
        if (owner.IsEmpty || encounter.IsEmpty)
            throw new ArgumentException("Condition enrollment requires complete stable identity.");
        if (resolveSource == null)
            throw new ArgumentNullException(nameof(resolveSource));
        if (!detached.TryLease(out lease))
        {
            registrations = Array.Empty<ActiveEffectRegistration>();
            return false;
        }

        registrations = Array.AsReadOnly(
            lease
                .Value.Select(application =>
                    application.CreateRegistration(
                        owner,
                        resolveSource(application.SourceActorId),
                        encounter
                    )
                )
                .ToArray()
        );
        return true;
    }

    internal IUnityCombatantBatchFinalizationContribution CreateEnrollmentFinalization() =>
        detached.CreateEnrollmentFinalization();

    internal bool HasPendingRestore => detached.HasPending;

    private bool TryGetAuthority(out UnityCombatRulesBridge bridge, out CreatureId owner)
    {
        ActionController controller = GetComponent<ActionController>();
        if (controller != null && controller.TryGetCombatRules(out bridge, out owner))
            return true;
        bridge = null;
        owner = default;
        return false;
    }
}

/// <summary>One exact condition instance at the Unity persistence boundary.</summary>
internal sealed class ConditionApplicationSnapshot
{
    internal ConditionApplicationSnapshot(
        ActiveEffectId effectId,
        BindingId bindingId,
        RuleDefinitionId definitionId,
        string sourceActorId,
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
        SourceActorId = DurableActorSourceIdentity.RequireCanonical(
            sourceActorId,
            nameof(sourceActorId)
        );
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
    internal string SourceActorId { get; }
    internal RuleSource Source { get; }
    internal EffectDuration Duration { get; }
    internal EffectStateVersion Version { get; }
    internal IEffectState State { get; }
    internal ActiveEffectStatus Status { get; }
    internal long CreationOrder { get; }
    internal bool BindingEnabled { get; }
    internal ConditionTimingSnapshot Timing { get; }

    internal ActiveEffectRegistration CreateRegistration(
        CreatureId owner,
        DurableActorSourceResolution sourceResolution,
        EncounterId encounter
    )
    {
        CreatureId sourceCreature = sourceResolution.SourceCreature;
        EffectStateVersion version = Version;
        ActiveEffectStatus status = Status;
        bool bindingEnabled = BindingEnabled;
        ConditionTimingSnapshot timingSnapshot = Timing;
        if (!sourceResolution.IsPresent)
        {
            timingSnapshot = null;
            if (
                status == ActiveEffectStatus.Active
                && Duration.Kind != EffectDurationKind.Indefinite
            )
            {
                version = version.Next();
                status = ActiveEffectStatus.Expired;
                bindingEnabled = false;
            }
        }
        ActiveEffectInstance effect = new ActiveEffectInstance(
            EffectId,
            DefinitionId,
            sourceCreature,
            Source,
            Duration,
            State,
            version,
            status
        );
        ActiveRuleBinding binding = new ActiveRuleBinding(
            BindingId,
            DefinitionId,
            owner,
            EffectId,
            Source,
            CreationOrder,
            bindingEnabled
        );
        ActiveEffectTimingState timing =
            timingSnapshot == null
                ? null
                : new ActiveEffectTimingState(
                    EffectId,
                    encounter,
                    BindingId,
                    sourceCreature,
                    timingSnapshot.RemainingBoundaries,
                    timingSnapshot.ExpiresWithEncounter,
                    CreationOrder
                );
        return new ActiveEffectRegistration(effect, binding, timing);
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
