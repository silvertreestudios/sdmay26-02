using System;
using System.Collections;
using System.Collections.Generic;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity.Composition;
using UnityEngine;

namespace Game.Rules.Unity.Vfx
{
    /// <summary>Describes how one persistent effect follows a live Unity presentation anchor.</summary>
    public readonly struct PersistentVfxAnchor
    {
        /// <summary>Creates a validated persistent attachment.</summary>
        public PersistentVfxAnchor(Transform transform, Vector3 localOffset, bool followRotation)
        {
            Transform =
                transform != null ? transform : throw new ArgumentNullException(nameof(transform));
            LocalOffset = localOffset;
            FollowRotation = followRotation;
        }

        /// <summary>Gets the live transform followed by the visual.</summary>
        public Transform Transform { get; }

        /// <summary>Gets the offset expressed in the anchor's local space.</summary>
        public Vector3 LocalOffset { get; }

        /// <summary>Gets whether the visual rotates with the anchor.</summary>
        public bool FollowRotation { get; }
    }

    /// <summary>Pairs a feature-selected persistent cue with its authoritative Unity owner.</summary>
    public readonly struct PersistentVfxSelection
    {
        private readonly Func<CreatureComponent, PersistentVfxAnchor> resolveAnchor;

        /// <summary>Creates a complete persistent presentation selection.</summary>
        public PersistentVfxSelection(CreatureId owner, VfxCueId cue)
            : this(owner, cue, default, ResolveOwnerCenter) { }

        /// <summary>Creates a selection with a transient cue for an explicit early removal.</summary>
        public PersistentVfxSelection(CreatureId owner, VfxCueId cue, VfxCueId removalCue)
            : this(owner, cue, removalCue, ResolveOwnerCenter) { }

        /// <summary>Creates a selection with a feature-owned presentation anchor resolver.</summary>
        public PersistentVfxSelection(
            CreatureId owner,
            VfxCueId cue,
            Func<CreatureComponent, PersistentVfxAnchor> resolveAnchor
        )
            : this(owner, cue, default, resolveAnchor) { }

        /// <summary>Creates a selection with explicit removal and attachment presentation.</summary>
        public PersistentVfxSelection(
            CreatureId owner,
            VfxCueId cue,
            VfxCueId removalCue,
            Func<CreatureComponent, PersistentVfxAnchor> resolveAnchor
        )
        {
            if (owner.IsEmpty)
                throw new ArgumentException("A persistent VFX owner is required.", nameof(owner));
            Owner = owner;
            Cue = cue;
            RemovalCue = removalCue;
            this.resolveAnchor =
                resolveAnchor ?? throw new ArgumentNullException(nameof(resolveAnchor));
        }

        /// <summary>Gets the creature whose live transform anchors the effect.</summary>
        public CreatureId Owner { get; }

        /// <summary>Gets the exact production cue.</summary>
        public VfxCueId Cue { get; }

        /// <summary>Gets the transient cue played when the effect is explicitly consumed.</summary>
        public VfxCueId RemovalCue { get; }

        /// <summary>Gets whether this feature selected a transient removal cue.</summary>
        public bool HasRemovalCue => !string.IsNullOrWhiteSpace(RemovalCue.Value);

        internal PersistentVfxAnchor ResolveAnchor(CreatureComponent owner) =>
            resolveAnchor(owner ?? throw new ArgumentNullException(nameof(owner)));

        private static PersistentVfxAnchor ResolveOwnerCenter(CreatureComponent owner) =>
            new(owner.transform, Vector3.up * 0.55f, false);
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
        private readonly UnityActionPresentationCoordinator coordinator;
        private readonly HashSet<ActiveEffectId> owned = new();

        /// <summary>Creates a feature-configured persistent-effect observer.</summary>
        public UnityPersistentVfxObserver(
            UnityVfxPlayback playback,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            Func<ActiveEffectInstance, PersistentVfxSelection?> select
        )
            : this(playback, creatures, select, new UnityActionPresentationCoordinator()) { }

        internal UnityPersistentVfxObserver(
            UnityVfxPlayback playback,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            Func<ActiveEffectInstance, PersistentVfxSelection?> select,
            UnityActionPresentationCoordinator coordinator
        )
        {
            this.playback = playback ?? throw new ArgumentNullException(nameof(playback));
            this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));
            this.select = select ?? throw new ArgumentNullException(nameof(select));
            this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
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
            owned.Add(effect.Id);
            PersistentVfxAnchor anchor = selection.Value.ResolveAnchor(owner);
            if (
                !coordinator.TryEnqueueAfterResult(
                    rootId,
                    () => SetPersistent(effect.Id, selection.Value.Cue, anchor)
                )
            )
                SetPersistentNow(effect.Id, selection.Value.Cue, anchor);
        }

        /// <inheritdoc/>
        public void OnFactCommitted(
            ActiveEffectRemovedFact fact,
            OpId rootId,
            RulesSnapshot currentSnapshot
        )
        {
            if (!owned.Remove(fact.EffectId))
                return;
            PersistentVfxSelection? selection = select(fact.Effect);
            if (
                fact.Reason == ActiveEffectRemovalReason.Ended
                && selection.HasValue
                && selection.Value.HasRemovalCue
                && creatures.TryGetValue(selection.Value.Owner, out CreatureComponent removalOwner)
                && removalOwner != null
            )
            {
                PersistentVfxAnchor removalAnchor = selection.Value.ResolveAnchor(removalOwner);
                if (
                    coordinator.TryEnqueue(
                        rootId,
                        () =>
                            RemovePersistentWithCue(
                                fact.EffectId,
                                selection.Value.RemovalCue,
                                removalAnchor
                            )
                    )
                )
                    return;
                playback.RemovePersistent(fact.EffectId.Value);
                VfxCoroutineHost.Run(
                    removalOwner,
                    playback.PlayTransient(
                        selection.Value.RemovalCue,
                        removalAnchor.Transform.TransformPoint(removalAnchor.LocalOffset),
                        removalAnchor.Transform.TransformPoint(removalAnchor.LocalOffset),
                        lifetimeOwner: removalAnchor.Transform
                    )
                );
                return;
            }

            if (coordinator.TryEnqueue(rootId, () => RemovePersistent(fact.EffectId)))
                return;
            playback.RemovePersistent(fact.EffectId.Value);
        }

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
            {
                owned.Remove(effect);
                playback.RemovePersistent(effect.Value);
            }
        }

        private IEnumerator SetPersistent(
            ActiveEffectId effect,
            VfxCueId cue,
            PersistentVfxAnchor anchor
        )
        {
            SetPersistentNow(effect, cue, anchor);
            yield break;
        }

        private void SetPersistentNow(
            ActiveEffectId effect,
            VfxCueId cue,
            PersistentVfxAnchor anchor
        ) =>
            playback.SetPersistent(
                effect.Value,
                cue,
                anchor.Transform,
                anchor.LocalOffset,
                anchor.FollowRotation
            );

        private IEnumerator RemovePersistent(ActiveEffectId effect)
        {
            playback.RemovePersistent(effect.Value);
            yield break;
        }

        private IEnumerator RemovePersistentWithCue(
            ActiveEffectId effect,
            VfxCueId removalCue,
            PersistentVfxAnchor anchor
        )
        {
            playback.RemovePersistent(effect.Value);
            IEnumerator transient = playback.PlayTransient(
                removalCue,
                anchor.Transform.TransformPoint(anchor.LocalOffset),
                anchor.Transform.TransformPoint(anchor.LocalOffset),
                lifetimeOwner: anchor.Transform
            );
            using (transient as IDisposable)
            {
                while (transient.MoveNext())
                    yield return transient.Current;
            }
        }
    }
}
