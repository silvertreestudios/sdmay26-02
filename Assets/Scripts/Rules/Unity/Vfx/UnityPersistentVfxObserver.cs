using System;
using System.Collections.Generic;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity.Composition;
using UnityEngine;

namespace Game.Rules.Unity.Vfx
{
    /// <summary>Pairs a feature-selected persistent cue with its authoritative Unity owner.</summary>
    public readonly struct PersistentVfxSelection
    {
        /// <summary>Creates a complete persistent presentation selection.</summary>
        public PersistentVfxSelection(CreatureId owner, VfxCueId cue)
        {
            if (owner.IsEmpty)
                throw new ArgumentException("A persistent VFX owner is required.", nameof(owner));
            Owner = owner;
            Cue = cue;
        }

        /// <summary>Gets the creature whose live transform anchors the effect.</summary>
        public CreatureId Owner { get; }

        /// <summary>Gets the exact production cue.</summary>
        public VfxCueId Cue { get; }
    }

    /// <summary>Owns an encounter playback service and releases every visual with the encounter.</summary>
    internal sealed class UnityVfxEncounterLifetimeModule : IUnityEncounterRuntimeModule
    {
        private readonly UnityVfxPlayback playback;

        internal UnityVfxEncounterLifetimeModule(UnityVfxPlayback playback) =>
            this.playback = playback ?? throw new ArgumentNullException(nameof(playback));

        /// <inheritdoc/>
        public void RegisterRuntime(RuleDispatcher dispatcher, CompositeLifetime lifetime) =>
            lifetime.Add(playback);
    }

    /// <summary>
    /// Projects selected active effects into objects keyed by exact <see cref="ActiveEffectId"/>.
    /// Feature modules supply selection so this lifecycle helper never learns feature names.
    /// </summary>
    public sealed class UnityPersistentVfxObserver
        : IFactObserver<ActiveEffectCreatedFact>,
            IFactObserver<ActiveEffectRemovedFact>,
            IFactObserver<EncounterOutcomeCommittedFact>,
            IDisposable
    {
        private readonly UnityVfxPlayback playback;
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;
        private readonly Func<ActiveEffectInstance, PersistentVfxSelection?> select;
        private readonly HashSet<ActiveEffectId> owned = new();

        /// <summary>Creates a feature-configured persistent-effect observer.</summary>
        public UnityPersistentVfxObserver(
            UnityVfxPlayback playback,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            Func<ActiveEffectInstance, PersistentVfxSelection?> select
        )
        {
            this.playback = playback ?? throw new ArgumentNullException(nameof(playback));
            this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));
            this.select = select ?? throw new ArgumentNullException(nameof(select));
        }

        /// <inheritdoc/>
        public void OnFactCommitted(
            ActiveEffectCreatedFact fact,
            OpId rootId,
            RulesSnapshot currentSnapshot
        )
        {
            if (
                !currentSnapshot.ActiveEffects.TryGet(
                    fact.EffectId,
                    out ActiveEffectInstance effect
                )
            )
                return;
            PersistentVfxSelection? selection = select(effect);
            if (!selection.HasValue)
                return;
            if (
                !creatures.TryGetValue(selection.Value.Owner, out CreatureComponent owner)
                || owner == null
            )
                return;
            playback.SetPersistent(effect.Id.Value, selection.Value.Cue, owner.transform);
            owned.Add(effect.Id);
        }

        /// <inheritdoc/>
        public void OnFactCommitted(
            ActiveEffectRemovedFact fact,
            OpId rootId,
            RulesSnapshot currentSnapshot
        ) => Remove(fact.EffectId);

        /// <inheritdoc/>
        public void OnFactCommitted(
            EncounterOutcomeCommittedFact fact,
            OpId rootId,
            RulesSnapshot currentSnapshot
        ) => Dispose();

        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (ActiveEffectId effect in new List<ActiveEffectId>(owned))
                Remove(effect);
        }

        private void Remove(ActiveEffectId effect)
        {
            if (!owned.Remove(effect))
                return;
            playback.RemovePersistent(effect.Value);
        }
    }
}
