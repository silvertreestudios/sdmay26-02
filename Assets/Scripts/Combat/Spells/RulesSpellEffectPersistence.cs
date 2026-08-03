using System;
using System.Collections.Generic;
using System.Linq;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;
using UnityEngine;

namespace Game.Combat.Spells
{
    /// <summary>
    /// Owns detached persistence input for catalog-backed rules-native spell effects.
    /// </summary>
    /// <remarks>
    /// Live rules state remains authoritative while attached. Encounter release stores an exact
    /// immutable registration projection for save capture and the next owning encounter. This
    /// component never mirrors effects into <see cref="SpellEffectController"/>.
    /// </remarks>
    [DisallowMultipleComponent]
    internal sealed class RulesSpellEffectPersistence : MonoBehaviour
    {
        private static readonly IReadOnlyList<RulesSpellEffectSnapshot> EmptyEffects =
            Array.AsReadOnly(Array.Empty<RulesSpellEffectSnapshot>());
        private readonly PendingImmutableValue<IReadOnlyList<RulesSpellEffectSnapshot>> detached =
            new(EmptyEffects, "Rules-native spell effect");

        internal IReadOnlyList<RulesSpellEffectSnapshot> CaptureEffects(
            ISpellDefinitionCatalog catalog
        )
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (!TryGetAuthority(out UnityCombatRulesBridge bridge, out CreatureId owner))
            {
                IReadOnlyList<RulesSpellEffectSnapshot> captured = detached.ReadDetached();
                foreach (RulesSpellEffectSnapshot effect in captured)
                    effect.ValidateCatalog(catalog);
                return captured;
            }
            return CaptureAuthoritative(bridge, owner, catalog);
        }

        internal void ProjectDetachedEffects(
            UnityCombatRulesBridge expectedBridge,
            CreatureId expectedOwner,
            ISpellDefinitionCatalog catalog
        )
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (
                !TryGetAuthority(out UnityCombatRulesBridge bridge, out CreatureId owner)
                || !ReferenceEquals(bridge, expectedBridge)
                || owner != expectedOwner
            )
                throw new InvalidOperationException(
                    "Rules-native spell-effect detachment projection requires the exact attached combat rules."
                );
            detached.Replace(CaptureAuthoritative(bridge, owner, catalog));
        }

        internal void RestoreEffects(IEnumerable<RulesSpellEffectSnapshot> effects)
        {
            if (effects == null)
                throw new ArgumentNullException(nameof(effects));
            if (TryGetAuthority(out _, out _))
                throw new InvalidOperationException(
                    "Restore input cannot replace rules-native spell effects while authoritative combat rules are attached."
                );
            RulesSpellEffectSnapshot[] copied = effects.ToArray();
            if (copied.Any(effect => effect == null))
                throw new ArgumentException(
                    "Restored rules-native spell effects cannot contain null.",
                    nameof(effects)
                );
            HashSet<ActiveEffectId> effectIds = new();
            HashSet<BindingId> bindingIds = new();
            RulesSpellEffectSnapshot previous = null;
            foreach (RulesSpellEffectSnapshot effect in copied)
            {
                if (!effectIds.Add(effect.EffectId) || !bindingIds.Add(effect.BindingId))
                    throw new ArgumentException(
                        "Restored rules-native spell effects require unique stable identities.",
                        nameof(effects)
                    );
                if (previous != null && RulesSpellEffectSnapshot.CompareOrder(previous, effect) > 0)
                    throw new ArgumentException(
                        "Restored rules-native spell effects require canonical creation order.",
                        nameof(effects)
                    );
                previous = effect;
            }
            detached.Replace(Array.AsReadOnly(copied));
        }

        internal bool TryPrepareRestore(
            CreatureId owner,
            EncounterId encounter,
            Func<string, DurableActorSourceResolution> resolveActor,
            ISpellDefinitionCatalog catalog,
            out PendingImmutableValueLease<IReadOnlyList<RulesSpellEffectSnapshot>> lease,
            out IReadOnlyList<ActiveEffectRegistration> registrations
        )
        {
            if (owner.IsEmpty || encounter.IsEmpty)
                throw new ArgumentException(
                    "Rules-native spell-effect enrollment requires complete stable identity."
                );
            if (resolveActor == null)
                throw new ArgumentNullException(nameof(resolveActor));
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (!detached.TryLease(out lease))
            {
                registrations = Array.Empty<ActiveEffectRegistration>();
                return false;
            }
            registrations = Array.AsReadOnly(
                lease
                    .Value.Select(effect =>
                        effect.CreateRegistration(owner, encounter, resolveActor, catalog)
                    )
                    .ToArray()
            );
            return true;
        }

        internal IUnityCombatantBatchFinalizationContribution CreateEnrollmentFinalization() =>
            detached.CreateEnrollmentFinalization();

        internal bool HasPendingRestore => detached.HasPending;

        private static IReadOnlyList<RulesSpellEffectSnapshot> CaptureAuthoritative(
            UnityCombatRulesBridge bridge,
            CreatureId owner,
            ISpellDefinitionCatalog catalog
        )
        {
            string durableOwner = bridge.GetDurableActorId(owner);
            if (string.IsNullOrEmpty(durableOwner))
                return EmptyEffects;
            if (!DurableActorSourceIdentity.IsCanonical(durableOwner))
                throw new InvalidOperationException(
                    $"Rules-native spell-effect owner {owner.Value} has noncanonical durable actor provenance."
                );

            List<RulesSpellEffectSnapshot> captured = new();
            foreach (
                ActiveRuleBinding binding in bridge
                    .Snapshot.RuleBindings.Select(pair => pair.Value)
                    .Where(binding => binding.Owner == owner && binding.EffectId.HasValue)
                    .OrderBy(binding => binding.CreationOrder)
                    .ThenBy(binding => binding.Id.Value, StringComparer.Ordinal)
                    .ThenBy(binding => binding.EffectId.Value.Value, StringComparer.Ordinal)
            )
            {
                if (
                    !bridge.Snapshot.ActiveEffects.TryGet(
                        binding.EffectId.Value,
                        out ActiveEffectInstance effect
                    )
                    || effect.State is not SpellEffectState state
                    || effect.DefinitionId
                        == UnitySpellcastingEncounterModule.RestoredTimedEffectDefinitionId
                )
                    continue;
                if (
                    !binding.EffectId.HasValue
                    || binding.EffectId.Value != effect.Id
                    || binding.DefinitionId != effect.DefinitionId
                    || binding.Source != effect.Source
                )
                    throw new InvalidOperationException(
                        $"Rules-native spell-effect binding {binding.Id.Value} does not match its authoritative effect."
                    );
                RulesSpellEffectSnapshot.ValidateCatalog(
                    catalog,
                    effect.DefinitionId,
                    effect.Source,
                    effect.Duration,
                    effect.SourceCreature,
                    state,
                    owner
                );
                string sourceActorId = bridge.GetDurableActorId(effect.SourceCreature);
                if (!DurableActorSourceIdentity.IsCanonical(sourceActorId))
                    throw new InvalidOperationException(
                        $"Rules-native spell effect {effect.Id.Value} has no canonical durable source actor provenance."
                    );
                string targetActorId = bridge.GetDurableActorId(state.Target);
                if (!DurableActorSourceIdentity.IsCanonical(targetActorId))
                    throw new InvalidOperationException(
                        $"Rules-native spell effect {effect.Id.Value} has no canonical durable target actor provenance."
                    );
                bridge.Snapshot.ActiveEffectTimings.TryGet(
                    effect.Id,
                    out ActiveEffectTimingState timing
                );
                _ = new ActiveEffectRegistration(effect, binding, timing);
                if (timing != null && timing.Encounter != bridge.EncounterId)
                    throw new InvalidOperationException(
                        $"Rules-native spell effect {effect.Id.Value} has timing for a different encounter."
                    );
                captured.Add(
                    new RulesSpellEffectSnapshot(
                        effect.Id,
                        binding.Id,
                        effect.DefinitionId,
                        sourceActorId,
                        targetActorId,
                        effect.Source,
                        effect.Duration,
                        effect.EffectStateVersion,
                        effect.Status,
                        binding.CreationOrder,
                        binding.IsEnabled,
                        state.Spell,
                        timing == null
                            ? null
                            : new RulesSpellEffectTimingSnapshot(
                                timing.RemainingBoundaries,
                                timing.ExpiresWithEncounter
                            )
                    )
                );
            }
            return Array.AsReadOnly(captured.ToArray());
        }

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

    /// <summary>One exact catalog-backed spell-effect registration at the Unity save boundary.</summary>
    internal sealed class RulesSpellEffectSnapshot
    {
        internal RulesSpellEffectSnapshot(
            ActiveEffectId effectId,
            BindingId bindingId,
            RuleDefinitionId definitionId,
            string sourceActorId,
            string targetActorId,
            RuleSource source,
            EffectDuration duration,
            EffectStateVersion version,
            ActiveEffectStatus status,
            long creationOrder,
            bool bindingEnabled,
            SpellReference spell,
            RulesSpellEffectTimingSnapshot timing
        )
        {
            if (
                effectId.IsEmpty
                || bindingId.IsEmpty
                || definitionId.IsEmpty
                || source.IsEmpty
                || spell.Spell.IsEmpty
            )
                throw new ArgumentException(
                    "A persisted rules-native spell effect requires complete stable identity."
                );
            if (!Enum.IsDefined(typeof(ActiveEffectStatus), status))
                throw new ArgumentOutOfRangeException(nameof(status));
            if ((status == ActiveEffectStatus.Active) != bindingEnabled)
                throw new ArgumentException(
                    "Persisted rules-native spell-effect status and binding state must agree."
                );
            bool requiresTiming =
                status == ActiveEffectStatus.Active
                && duration.Kind != EffectDurationKind.Indefinite;
            if ((timing != null) != requiresTiming)
                throw new ArgumentException(
                    "Persisted rules-native spell-effect timing must exactly match active finite duration."
                );
            if (
                timing != null
                && timing.ExpiresWithEncounter != (duration.Kind == EffectDurationKind.Encounter)
            )
                throw new ArgumentException(
                    "Persisted rules-native spell-effect duration and timing disagree."
                );
            if (creationOrder < 0)
                throw new ArgumentOutOfRangeException(nameof(creationOrder));

            EffectId = effectId;
            BindingId = bindingId;
            DefinitionId = definitionId;
            SourceActorId = DurableActorSourceIdentity.RequireCanonical(
                sourceActorId,
                nameof(sourceActorId)
            );
            TargetActorId = DurableActorSourceIdentity.RequireCanonical(
                targetActorId,
                nameof(targetActorId)
            );
            Source = source;
            Duration = duration;
            Version = version;
            Status = status;
            CreationOrder = creationOrder;
            BindingEnabled = bindingEnabled;
            Spell = spell;
            Timing = timing;
        }

        internal ActiveEffectId EffectId { get; }
        internal BindingId BindingId { get; }
        internal RuleDefinitionId DefinitionId { get; }
        internal string SourceActorId { get; }
        internal string TargetActorId { get; }
        internal RuleSource Source { get; }
        internal EffectDuration Duration { get; }
        internal EffectStateVersion Version { get; }
        internal ActiveEffectStatus Status { get; }
        internal long CreationOrder { get; }
        internal bool BindingEnabled { get; }
        internal SpellReference Spell { get; }
        internal RulesSpellEffectTimingSnapshot Timing { get; }

        internal ActiveEffectRegistration CreateRegistration(
            CreatureId owner,
            EncounterId encounter,
            Func<string, DurableActorSourceResolution> resolveActor,
            ISpellDefinitionCatalog catalog
        )
        {
            DurableActorSourceResolution source = resolveActor(SourceActorId);
            if (!source.IsPresent)
                throw new InvalidOperationException(
                    $"Rules-native spell effect {EffectId.Value} requires source actor '{SourceActorId}' in the owning encounter."
                );
            if (source.SourceCreature != owner)
                throw new InvalidOperationException(
                    $"Rules-native spell effect {EffectId.Value} requires its source actor to be the effect owner."
                );
            DurableActorSourceResolution target = resolveActor(TargetActorId);
            if (!target.IsPresent)
                throw new InvalidOperationException(
                    $"Rules-native spell effect {EffectId.Value} requires target actor '{TargetActorId}' in the owning encounter."
                );
            if (target.SourceCreature != owner)
                throw new InvalidOperationException(
                    $"Rules-native spell effect {EffectId.Value} requires its target actor to be the effect owner."
                );
            SpellEffectState state = new(Spell, target.SourceCreature);
            ValidateCatalog(
                catalog,
                DefinitionId,
                Source,
                Duration,
                source.SourceCreature,
                state,
                owner
            );
            ActiveEffectInstance effect = new(
                EffectId,
                DefinitionId,
                source.SourceCreature,
                Source,
                Duration,
                state,
                Version,
                Status
            );
            ActiveRuleBinding binding = new(
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
                        source.SourceCreature,
                        Timing.RemainingBoundaries,
                        Timing.ExpiresWithEncounter,
                        CreationOrder
                    );
            return new ActiveEffectRegistration(effect, binding, timing);
        }

        internal void ValidateCatalog(ISpellDefinitionCatalog catalog)
        {
            SpellDefinition definition = ValidateCatalogDefinition(
                catalog,
                DefinitionId,
                Source,
                Duration,
                Spell
            );
            int targets = definition.Effects.Count(directive =>
                directive.DefinitionId == DefinitionId
                && directive.Duration == Duration
                && string.Equals(directive.Target, "self", StringComparison.Ordinal)
            );
            if (
                targets != 1
                || !string.Equals(SourceActorId, TargetActorId, StringComparison.Ordinal)
            )
                throw new InvalidOperationException(
                    $"Rules-native spell effect {DefinitionId.Value} does not exactly match the catalog target contract for {Spell}."
                );
        }

        internal static int CompareOrder(
            RulesSpellEffectSnapshot left,
            RulesSpellEffectSnapshot right
        )
        {
            int byCreation = left.CreationOrder.CompareTo(right.CreationOrder);
            if (byCreation != 0)
                return byCreation;
            int byBinding = StringComparer.Ordinal.Compare(
                left.BindingId.Value,
                right.BindingId.Value
            );
            return byBinding != 0
                ? byBinding
                : StringComparer.Ordinal.Compare(left.EffectId.Value, right.EffectId.Value);
        }

        internal static void ValidateCatalog(
            ISpellDefinitionCatalog catalog,
            RuleDefinitionId definitionId,
            RuleSource source,
            EffectDuration duration,
            CreatureId sourceCreature,
            SpellEffectState state,
            CreatureId owner
        )
        {
            SpellDefinition definition = ValidateCatalogDefinition(
                catalog,
                definitionId,
                source,
                duration,
                state.Spell
            );
            int targets = definition.Effects.Count(directive =>
                directive.DefinitionId == definitionId
                && directive.Duration == duration
                && string.Equals(directive.Target, "self", StringComparison.Ordinal)
                && sourceCreature == owner
                && state.Target == owner
            );
            if (targets != 1)
                throw new InvalidOperationException(
                    $"Rules-native spell effect {definitionId.Value} does not exactly match the catalog target contract for {state.Spell}."
                );
        }

        private static SpellDefinition ValidateCatalogDefinition(
            ISpellDefinitionCatalog catalog,
            RuleDefinitionId definitionId,
            RuleSource source,
            EffectDuration duration,
            SpellReference spell
        )
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (!catalog.TryGetSpell(spell, out SpellDefinition definition))
                throw new InvalidOperationException(
                    $"Rules-native spell effect references unavailable spell {spell}."
                );
            int matches = definition.Effects.Count(directive =>
                directive.DefinitionId == definitionId && directive.Duration == duration
            );
            if (matches != 1)
                throw new InvalidOperationException(
                    $"Rules-native spell effect {definitionId.Value} does not exactly match the catalog contract for {spell}."
                );
            RuleSource expectedSource = RuleSource.FromSlug(spell.Spell.Value);
            if (source != expectedSource)
                throw new InvalidOperationException(
                    $"Rules-native spell effect {definitionId.Value} has noncanonical spell provenance."
                );
            return definition;
        }
    }

    /// <summary>Exact remaining encounter-clock state for one rules-native spell effect.</summary>
    internal sealed class RulesSpellEffectTimingSnapshot
    {
        internal RulesSpellEffectTimingSnapshot(int remainingBoundaries, bool expiresWithEncounter)
        {
            if (remainingBoundaries < 0)
                throw new ArgumentOutOfRangeException(nameof(remainingBoundaries));
            RemainingBoundaries = remainingBoundaries;
            ExpiresWithEncounter = expiresWithEncounter;
        }

        internal int RemainingBoundaries { get; }
        internal bool ExpiresWithEncounter { get; }
    }

    /// <summary>Installs the detached persistence owner after combat authority is attached.</summary>
    internal sealed class RulesSpellEffectPersistenceInstallation
        : IUnityCombatantInstallationContribution
    {
        private readonly ActionController controller;
        private RulesSpellEffectPersistence persistence;

        internal RulesSpellEffectPersistenceInstallation(ActionController controller)
        {
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
            persistence = controller.GetComponent<RulesSpellEffectPersistence>();
        }

        internal RulesSpellEffectPersistence PreparedPersistence => persistence;

        internal RulesSpellEffectPersistence Persistence => persistence;

        internal IUnityCombatantBatchFinalizationContribution CreateEnrollmentFinalization() =>
            new CompleteInstalledRulesSpellEffectPersistenceEnrollment(this);

        /// <inheritdoc/>
        public void Reconcile()
        {
            if (persistence == null)
                persistence = controller.gameObject.AddComponent<RulesSpellEffectPersistence>();
        }

        private sealed class CompleteInstalledRulesSpellEffectPersistenceEnrollment
            : IUnityCombatantBatchFinalizationContribution
        {
            private readonly RulesSpellEffectPersistenceInstallation installation;
            private IUnityCombatantBatchFinalizationContribution finalization;

            internal CompleteInstalledRulesSpellEffectPersistenceEnrollment(
                RulesSpellEffectPersistenceInstallation installation
            ) => this.installation = installation;

            public void Validate()
            {
                RulesSpellEffectPersistence installed =
                    installation.Persistence
                    ?? throw new InvalidOperationException(
                        "Rules-native spell-effect persistence was not installed before enrollment finalization."
                    );
                finalization = installed.CreateEnrollmentFinalization();
                finalization.Validate();
            }

            public void Apply() => finalization.Apply();
        }
    }
}
