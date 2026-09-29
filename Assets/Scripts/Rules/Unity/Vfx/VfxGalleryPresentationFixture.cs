using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity.Composition;
using Game.Rules.Unity.Light;
using Game.Rules.Unity.Strike;
using UnityEngine;
using RulesDegreeOfSuccess = Game.Rules.Runtime.DegreeOfSuccess;

namespace Game.Rules.Unity.Vfx
{
    /// <summary>
    /// Builds deterministic committed outcomes and Facts for the review gallery, then sends them
    /// through the same production presenters and observers used by encounters.
    /// </summary>
    public sealed class VfxGalleryPresentationFixture : IDisposable
    {
        private static readonly CreatureId SourceId = new("vfx-gallery-source");
        private static readonly CreatureId[] TargetIds =
        {
            new("vfx-gallery-target-1"),
            new("vfx-gallery-target-2"),
            new("vfx-gallery-target-3"),
        };

        private readonly UnityVfxPlayback playback;
        private readonly Transform source;
        private readonly Transform[] targets;
        private readonly Dictionary<CreatureId, CreatureComponent> creatures = new();
        private readonly RulesSnapshot snapshot = new RulesState(new RulesStateSeed()).Snapshot;
        private readonly UnitySpellDefinitionCatalog spellCatalog;
        private readonly UnitySpellActionPresenter spellPresenter;
        private readonly UnityStrikeActionPresenter strikePresenter;
        private readonly UnityPersistentVfxObserver spellEffects;
        private readonly UnityPersistentVfxObserver rageEffects;
        private readonly UnityLightEffectPresentationObserver lightEffects;
        private readonly UnityRottingAuraModule rottingAura;
        private readonly UnityActionPresentationCoordinator actionPresentation = new();
        private readonly UnityHealthProjectionModule.HealthProjectionObserver healthPresentation;
        private readonly List<string> trace = new();
        private readonly List<(string Cue, Vector3 Origin, Vector3 Destination)> transientStarts =
            new();
        private float persistentHoldSeconds = 0.8f;
        private readonly List<(
            ActiveEffectInstance Effect,
            ActiveRuleBinding Binding
        )> currentEffects = new();
        private CreatureId[] latestSelection = Array.Empty<CreatureId>();
        private int effectSequence;
        private long presentationSequence;
        private int defeatPresentationCount;
        private bool disposed;

        /// <summary>Creates one isolated gallery fixture around scene-owned review actors.</summary>
        public VfxGalleryPresentationFixture(
            UnityVfxPlayback playback,
            Transform source,
            IReadOnlyList<Transform> targets
        )
        {
            this.playback = playback ?? throw new ArgumentNullException(nameof(playback));
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            if (
                targets == null
                || targets.Count < TargetIds.Length
                || targets.Any(value => value == null)
            )
                throw new ArgumentException(
                    "The gallery requires three target anchors.",
                    nameof(targets)
                );
            this.targets = targets.Take(TargetIds.Length).ToArray();

            CreatureComponent sourceCreature = RequireCreature(source.gameObject);
            creatures.Add(SourceId, sourceCreature);
            for (int index = 0; index < TargetIds.Length; index++)
                creatures.Add(TargetIds[index], RequireCreature(this.targets[index].gameObject));

            spellCatalog = UnitySpellDefinitionCatalog.Load();
            spellPresenter = new UnitySpellActionPresenter(creatures, spellCatalog, playback);
            Dictionary<CreatureId, GameObject> attackers = new() { [SourceId] = source.gameObject };
            strikePresenter = new UnityStrikeActionPresenter(
                attackers,
                creatures,
                new GalleryStrikeCatalog(),
                playback
            );
            spellEffects = new UnityPersistentVfxObserver(
                playback,
                creatures,
                SpellPersistentVfxSelector.Select,
                actionPresentation
            );
            rageEffects = new UnityPersistentVfxObserver(
                playback,
                creatures,
                RagePersistentVfxSelector.Select,
                actionPresentation
            );
            lightEffects = UnityLightEffectPresentationObserver.Create(
                spellCatalog,
                creatures,
                playback,
                actionPresentation
            );
            healthPresentation = new UnityHealthProjectionModule.HealthProjectionObserver(
                creatures,
                actionPresentation
            );
            rottingAura = new UnityRottingAuraModule(
                creatures,
                new GridPrivate.Tile[1, 1],
                playback
            );
            playback.CueStarted += RecordCue;
            playback.TransientStarted += RecordTransient;
        }

        /// <summary>Gets the exact production cues emitted by the latest fixture timeline.</summary>
        public IReadOnlyList<string> Trace => trace;

        internal IReadOnlyList<(string Cue, Vector3 Origin, Vector3 Destination)> TransientStarts =>
            transientStarts;

        /// <summary>Gets the number of targets staged by the latest fixture timeline.</summary>
        public int TargetCount { get; private set; }

        /// <summary>Gets the health most recently projected through the production health observer.</summary>
        public int PrimaryTargetHitPoints => creatures[TargetIds[0]].Health.Current;

        /// <summary>Gets whether the primary review target remains active after presentation.</summary>
        public bool IsPrimaryTargetActive => creatures[TargetIds[0]].gameObject.activeSelf;

        internal IReadOnlyList<CreatureId> LatestSelection => latestSelection;

        internal IReadOnlyList<CreatureId> CurrentEffectOwners =>
            currentEffects.Select(value => value.Binding.Owner).ToArray();

        internal int DefeatPresentationCount => defeatPresentationCount;

        /// <summary>Shortens only review holds and prefab durations for automated verification.</summary>
        public void ConfigureTestTiming(float persistentHoldSeconds)
        {
            if (persistentHoldSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(persistentHoldSeconds));
            this.persistentHoldSeconds = persistentHoldSeconds;
        }

        /// <summary>Runs one complete deterministic production-presentation timeline.</summary>
        public IEnumerator Play(VfxCoverageEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            ThrowIfDisposed();
            trace.Clear();
            transientStarts.Clear();
            TargetCount = DetermineTargetCount(entry);
            Stage(entry);

            if (entry.id.StartsWith("spell/", StringComparison.Ordinal))
                yield return PlaySpell(entry);
            else if (entry.id.StartsWith("strike/", StringComparison.Ordinal))
                yield return PlayStrike(entry, Array.Empty<string>());
            else if (entry.id == "auxiliary/sneak-attack/hit")
                yield return PlayStrike(entry, new[] { "sneak-attack" });
            else if (entry.id == "auxiliary/infuse-vitality-strike/hit")
                yield return PlayStrike(entry, new[] { "infuse-vitality" });
            else if (entry.id.StartsWith("auxiliary/rotting-aura/", StringComparison.Ordinal))
                yield return PlayRottingAura(entry);
            else
                yield return PlayPersistentLifecycle(entry);
        }

        /// <summary>Stops all fixture-owned persistent observers and clears deterministic state.</summary>
        public void Reset()
        {
            spellEffects.Dispose();
            rageEffects.Dispose();
            lightEffects.Dispose();
            actionPresentation.Dispose();
            currentEffects.Clear();
            latestSelection = Array.Empty<CreatureId>();
            TargetCount = 0;
            trace.Clear();
            transientStarts.Clear();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            playback.CueStarted -= RecordCue;
            playback.TransientStarted -= RecordTransient;
            Reset();
        }

        private IEnumerator PlaySpell(VfxCoverageEntry entry)
        {
            string[] parts = entry.id.Split('/');
            SpellReference spell = new(new SpellId(parts[1]), 1);
            int actions = ParseActions(entry.variant);
            CreatureId[] selected = TargetIds.Take(TargetCount).ToArray();
            if (spell.Spell.Value == "bless")
                selected = new[] { SourceId };
            latestSelection = selected;
            CastSpellActionOp operation = new(
                SourceId,
                spell,
                new SpellActionVariant(actions),
                selected.Length == 0 ? SpellCastSelection.Empty : new SpellCastSelection(selected)
            );
            List<SpellAttackResolution> attacks = new();
            List<SpellTargetResolution> results = new();
            if (spell.Spell.Value == "divine-lance")
            {
                RulesDegreeOfSuccess degree = entry.outcome switch
                {
                    "critical" => RulesDegreeOfSuccess.CriticalSuccess,
                    "miss" => RulesDegreeOfSuccess.Failure,
                    _ => RulesDegreeOfSuccess.Success,
                };
                attacks.Add(
                    new SpellAttackResolution(
                        spell,
                        SourceId,
                        TargetIds[0],
                        new RollResult(
                            DiceExpressions.D20,
                            new[] { degree == RulesDegreeOfSuccess.Failure ? 3 : 18 }
                        ),
                        8,
                        18,
                        degree,
                        0,
                        degree == RulesDegreeOfSuccess.Failure
                            ? Array.Empty<TypedDamagePart>()
                            : new[] { new TypedDamagePart("spirit", 8, new[] { "divine-lance" }) }
                    )
                );
            }
            else if (spell.Spell.Value is "heal" or "haunting-hymn")
            {
                RulesDegreeOfSuccess? degree = ParseDegree(entry.outcome);
                bool living = !entry.outcome.Contains("undead", StringComparison.Ordinal);
                for (int index = 0; index < selected.Length; index++)
                {
                    bool targetLiving = living;
                    RulesDegreeOfSuccess? targetDegree = degree;
                    if (spell.Spell.Value == "heal" && selected.Length == 3 && living)
                    {
                        targetLiving = index == 0;
                        targetDegree = index switch
                        {
                            1 => RulesDegreeOfSuccess.Success,
                            2 => RulesDegreeOfSuccess.CriticalFailure,
                            _ => null,
                        };
                    }
                    creatures[selected[index]].traits = targetLiving
                        ? new List<string>()
                        : new List<string> { "undead" };
                    results.Add(
                        new SpellTargetResolution(
                            selected[index],
                            targetDegree,
                            targetLiving
                                ? Array.Empty<TypedDamagePart>()
                                : new[]
                                {
                                    new TypedDamagePart("vitality", 8, new[] { spell.Spell.Value }),
                                },
                            targetLiving ? 8 : 0,
                            targetDegree == RulesDegreeOfSuccess.CriticalFailure
                        )
                    );
                }
            }

            OpId rootId = BeginPresentation(
                operation,
                () => spellPresenter.PresentBeginning(operation, snapshot)
            );

            List<ActiveEffectId> activeEffects = new();
            if (
                spell.Spell.Value
                is "light"
                    or "shield"
                    or "guidance"
                    or "bless"
                    or "infuse-vitality"
            )
            {
                CreatureId[] owners =
                    spell.Spell.Value == "infuse-vitality"
                        ? selected
                        : new[] { selected.FirstOrDefault().IsEmpty ? SourceId : selected[0] };
                foreach (CreatureId owner in owners)
                    activeEffects.Add(CreatePersistentSpell(spell, owner, rootId));
            }
            CastSpellOutcome outcome = new(SourceId, spell, activeEffects, attacks, results);
            actionPresentation.Enqueue(
                operation,
                () => spellPresenter.PresentResolved(operation, outcome, snapshot)
            );
            yield return Drain(actionPresentation.Drain(operation));
            if (
                spell.Spell.Value
                is "light"
                    or "shield"
                    or "guidance"
                    or "bless"
                    or "infuse-vitality"
            )
                yield return new WaitForSeconds(persistentHoldSeconds);
        }

        private IEnumerator PlayStrike(VfxCoverageEntry entry, IReadOnlyList<string> sources)
        {
            string profile = entry.id.StartsWith("strike/", StringComparison.Ordinal)
                ? ProfileLabel(entry.id.Split('/')[1])
                : "Shortbow";
            ItemId item = GalleryStrikeCatalog.ItemFor(profile);
            StrikeActionOp operation = new(SourceId, item, TargetIds[0]);
            RulesDegreeOfSuccess degree =
                entry.outcome == "critical" ? RulesDegreeOfSuccess.CriticalSuccess
                : entry.outcome == "miss" ? RulesDegreeOfSuccess.Failure
                : RulesDegreeOfSuccess.Success;
            bool hit =
                degree is RulesDegreeOfSuccess.Success or RulesDegreeOfSuccess.CriticalSuccess;
            int damage =
                entry.outcome == "zero damage contact" ? 0
                : hit ? 8
                : 0;
            IReadOnlyList<TypedDamagePart> parts = hit
                ? new[] { new TypedDamagePart("piercing", damage, sources) }
                : Array.Empty<TypedDamagePart>();
            StrikeResolution resolution = new(
                new RollResult(DiceExpressions.D20, new[] { hit ? 18 : 3 }),
                8,
                0,
                0,
                18,
                0,
                false,
                degree,
                parts,
                damage
            );
            OpId rootId = BeginPresentation(
                operation,
                () => strikePresenter.PresentBeginning(operation, snapshot)
            );
            if (hit)
            {
                bool defeated = degree == RulesDegreeOfSuccess.CriticalSuccess;
                bool wasDefeated = creatures[TargetIds[0]].IsDefeated;
                RulesSnapshot healthSnapshot = new RulesState(
                    new RulesStateSeed().SeedHealth(
                        TargetIds[0],
                        new HealthState(defeated ? 0 : 2, 10)
                    )
                ).Snapshot;
                healthPresentation.OnFactCommitted(
                    new DamageAppliedFact(
                        TargetIds[0],
                        new HealthChangeOriginId("vfx-gallery-strike"),
                        damage,
                        0,
                        damage
                    ),
                    rootId,
                    healthSnapshot
                );
                if (defeated)
                    healthPresentation.OnFactCommitted(
                        new CreatureDefeatCommittedFact(TargetIds[0]),
                        rootId,
                        healthSnapshot
                    );
                if (defeated)
                    actionPresentation.TryEnqueueAfterAction(
                        rootId,
                        () => RecordDefeatPresentation(wasDefeated)
                    );
            }
            actionPresentation.Enqueue(
                operation,
                () => strikePresenter.PresentResolved(operation, resolution, snapshot)
            );
            yield return Drain(actionPresentation.Drain(operation));
        }

        private ActiveEffectId CreatePersistentSpell(
            SpellReference spell,
            CreatureId owner,
            OpId rootId
        )
        {
            RuleDefinitionId definition = spell.Spell.Value switch
            {
                "shield" => SpellFeatureRules.ShieldEffect,
                "guidance" => SpellFeatureRules.GuidanceEffect,
                "bless" => SpellFeatureRules.BlessEffect,
                "infuse-vitality" => SpellFeatureRules.InfuseVitalityEffect,
                _ => RequireLightDefinition(),
            };
            ActiveEffectInstance effect = CreateEffect(
                definition,
                new SpellEffectState(spell, owner),
                owner,
                out ActiveRuleBinding binding
            );
            RulesSnapshot effectSnapshot = new RulesState(
                new RulesStateSeed().SeedActiveEffect(effect)
            ).Snapshot;
            ActiveEffectCreatedFact fact = new(effect, binding.Id);
            if (spell.Spell.Value == "light")
                lightEffects.OnFactCommitted(fact, rootId, effectSnapshot);
            else
                spellEffects.OnFactCommitted(fact, rootId, effectSnapshot);
            return effect.Id;
        }

        private IEnumerator PlayPersistentLifecycle(VfxCoverageEntry entry)
        {
            if (entry.id.StartsWith("auxiliary/rage/", StringComparison.Ordinal))
            {
                bool quick = entry.id.Contains("quick-tempered", StringComparison.Ordinal);
                ActiveEffectInstance effect = CreateEffect(
                    RageActionDefinition.EffectDefinitionId,
                    new RageEffectState(quick),
                    SourceId,
                    out ActiveRuleBinding binding
                );
                RulesSnapshot effectSnapshot = new RulesState(
                    new RulesStateSeed().SeedActiveEffect(effect)
                ).Snapshot;
                rageEffects.OnFactCommitted(
                    new ActiveEffectCreatedFact(effect, binding.Id),
                    new OpId(effectSequence),
                    effectSnapshot
                );
                yield return new WaitForSeconds(persistentHoldSeconds);
                yield break;
            }

            SpellReference spell = new(
                new SpellId(
                    entry.id.Contains("light", StringComparison.Ordinal) ? "light"
                    : entry.id.Contains("guidance", StringComparison.Ordinal) ? "guidance"
                    : "shield"
                ),
                1
            );
            CreatePersistentSpell(spell, SourceId, new OpId(++presentationSequence));
            yield return new WaitForSeconds(persistentHoldSeconds);
            if (entry.outcome == "refresh active")
            {
                RemoveCurrent(ActiveEffectRemovalReason.Ended);
                CreatePersistentSpell(spell, SourceId, new OpId(++presentationSequence));
                yield return new WaitForSeconds(persistentHoldSeconds);
            }
            else if (entry.outcome is "remove" or "consume" or "expire")
            {
                RemoveCurrent(
                    entry.outcome == "expire"
                        ? ActiveEffectRemovalReason.Expired
                        : ActiveEffectRemovalReason.Ended
                );
                while (playback.LiveObjectCount > 0)
                    yield return null;
            }
        }

        private IEnumerator PlayRottingAura(VfxCoverageEntry entry)
        {
            if (entry.outcome == "active")
            {
                creatures[SourceId].auras = new List<CreatureAura>
                {
                    new CreatureAura
                    {
                        name = "Rotting Aura",
                        slug = RottingAuraRules.Slug,
                        radiusFeet = 15,
                    },
                };
                rottingAura.PresentActive(SourceId);
                yield return new WaitForSeconds(persistentHoldSeconds);
                yield break;
            }
            int applied = entry.outcome == "fully resisted" ? 0 : 6;
            RottingAuraResolvedFact fact = new(
                SourceId,
                TargetIds[0],
                new RollResult(new DiceExpression(1, 6), new[] { 6 }),
                new[] { new TypedDamagePart("void", applied, new[] { RottingAuraRules.Slug }) },
                Array.Empty<TypedDefenseAdjustment>(),
                Array.Empty<TypedDefenseAdjustment>(),
                new DamageOutcome(6, 0, applied)
            );
            if (applied > 0)
                rottingAura.OnFactCommitted(
                    new DamageAppliedFact(
                        TargetIds[0],
                        new HealthChangeOriginId(RottingAuraRules.Slug + "-gallery"),
                        applied,
                        0,
                        applied
                    ),
                    new OpId(1),
                    snapshot
                );
            rottingAura.OnFactCommitted(fact, new OpId(1), snapshot);
            while (playback.LiveObjectCount > 0)
                yield return null;
        }

        private ActiveEffectInstance CreateEffect(
            RuleDefinitionId definition,
            IEffectState state,
            CreatureId owner,
            out ActiveRuleBinding binding
        )
        {
            effectSequence++;
            ActiveEffectId id = new("vfx-gallery-effect-" + effectSequence);
            RuleSource sourceRule = RuleSource.FromSlug("vfx-gallery-committed-fixture");
            ActiveEffectInstance effect = new(
                id,
                definition,
                owner,
                sourceRule,
                EffectDuration.Indefinite,
                state
            );
            binding = new ActiveRuleBinding(
                new BindingId("vfx-gallery-binding-" + effectSequence),
                definition,
                owner,
                id,
                sourceRule,
                effectSequence
            );
            currentEffects.Add((effect, binding));
            return effect;
        }

        private void RemoveCurrent(ActiveEffectRemovalReason reason)
        {
            if (currentEffects.Count == 0)
                return;
            foreach ((ActiveEffectInstance effect, ActiveRuleBinding binding) in currentEffects)
            {
                ActiveEffectRemovedFact removed = new(effect, binding, reason);
                spellEffects.OnFactCommitted(removed, new OpId(effectSequence), snapshot);
                lightEffects.OnFactCommitted(removed, new OpId(effectSequence), snapshot);
                rageEffects.OnFactCommitted(removed, new OpId(effectSequence), snapshot);
            }
            currentEffects.Clear();
        }

        private void Stage(VfxCoverageEntry entry)
        {
            bool melee =
                entry.id.StartsWith("strike/", StringComparison.Ordinal)
                && !entry.id.Contains("shortbow", StringComparison.Ordinal)
                && !entry.id.Contains("sling", StringComparison.Ordinal);
            source.position = new Vector3(-1.8f, 1f, 0f);
            float firstX = melee ? -0.25f : 2.6f;
            for (int index = 0; index < targets.Length; index++)
            {
                creatures[TargetIds[index]].ResetDefeatPresentationForFixture();
                targets[index].gameObject.SetActive(index < TargetCount);
                targets[index].position = new Vector3(
                    firstX + index * 1.25f,
                    1f,
                    index == 0 ? 0f : 1.2f
                );
            }
            if (TargetCount > 0)
            {
                source.rotation = Quaternion.LookRotation(targets[0].position - source.position);
                targets[0].rotation = Quaternion.LookRotation(
                    source.position - targets[0].position
                );
            }
        }

        private static int DetermineTargetCount(VfxCoverageEntry entry)
        {
            if (entry.id.Contains("infuse-vitality/3-action", StringComparison.Ordinal))
                return 3;
            if (entry.id.Contains("infuse-vitality/2-action", StringComparison.Ordinal))
                return 2;
            if (entry.id is "spell/heal/3-action/living" or "spell/heal/3-action/area-wave")
                return 3;
            if (
                entry.id.StartsWith("spell/light/", StringComparison.Ordinal)
                || entry.id.StartsWith("spell/shield/", StringComparison.Ordinal)
                || entry.id.StartsWith("spell/bless/", StringComparison.Ordinal)
            )
                return 0;
            return 1;
        }

        private static int ParseActions(string variant)
        {
            if (!string.IsNullOrEmpty(variant) && char.IsDigit(variant[0]))
                return variant[0] - '0';
            return 1;
        }

        private static RulesDegreeOfSuccess? ParseDegree(string outcome)
        {
            if (outcome.Contains("critical-failure", StringComparison.Ordinal))
                return RulesDegreeOfSuccess.CriticalFailure;
            if (outcome.Contains("critical-success", StringComparison.Ordinal))
                return RulesDegreeOfSuccess.CriticalSuccess;
            if (outcome.Contains("failure", StringComparison.Ordinal))
                return RulesDegreeOfSuccess.Failure;
            if (outcome.Contains("success", StringComparison.Ordinal))
                return RulesDegreeOfSuccess.Success;
            return null;
        }

        private RuleDefinitionId RequireLightDefinition()
        {
            SpellReference light = new(new SpellId("light"), 1);
            if (
                !spellCatalog.TryGetSpell(light, out SpellDefinition definition)
                || definition.Effects.Count != 1
            )
                throw new InvalidOperationException(
                    "Light requires one production effect definition."
                );
            return definition.Effects[0].DefinitionId;
        }

        private static string ProfileLabel(string slug) =>
            slug switch
            {
                "unarmed" => "Unarmed Strike",
                "dogslicer" => "Dogslicer",
                "greataxe" => "Greataxe",
                "mace" => "Mace",
                "scimitar" => "Scimitar",
                "shortbow" => "Shortbow",
                "sling" => "Sling",
                "spear" => "Spear",
                "longsword" => "Longsword",
                _ => throw new KeyNotFoundException($"Unknown gallery Strike profile '{slug}'."),
            };

        private static CreatureComponent RequireCreature(GameObject owner) =>
            owner.GetComponent<CreatureComponent>() ?? owner.AddComponent<CreatureComponent>();

        private static IEnumerator Drain(IEnumerator routine)
        {
            try
            {
                while (routine.MoveNext())
                    yield return routine.Current;
            }
            finally
            {
                (routine as IDisposable)?.Dispose();
            }
        }

        private void RecordCue(VfxCueId cue) => trace.Add(cue.Value);

        private void RecordTransient(VfxCueId cue, Vector3 origin, Vector3 destination) =>
            transientStarts.Add((cue.Value, origin, destination));

        private IEnumerator RecordDefeatPresentation(bool wasDefeated)
        {
            if (!wasDefeated && creatures[TargetIds[0]].IsDefeated)
                defeatPresentationCount++;
            yield break;
        }

        private OpId BeginPresentation(object action, Func<IEnumerator> beginning)
        {
            OpId rootId = new(++presentationSequence);
            actionPresentation.Begin(action, rootId);
            actionPresentation.Enqueue(action, beginning);
            return rootId;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(VfxGalleryPresentationFixture));
        }

        private sealed class GalleryStrikeCatalog : IStrikePresentationCatalog
        {
            private readonly Dictionary<ItemId, StrikeItemDefinition> items =
                StrikeVfxCueSelector.SupportedProfiles.ToDictionary(ItemFor, Create);

            public StrikeItemDefinition GetStrikeItem(ItemId item) => items[item];

            public bool TryGetWeapon(ItemId item, out EquipmentWeapon weapon)
            {
                weapon = null;
                return false;
            }

            internal static ItemId ItemFor(string label) =>
                new("vfx-gallery-" + label.ToLowerInvariant().Replace(' ', '-'));

            private static StrikeItemDefinition Create(string label) =>
                new(
                    ItemFor(label),
                    new ItemDefinitionId(
                        "vfx-gallery-" + label.ToLowerInvariant().Replace(' ', '-')
                    ),
                    label,
                    string.Empty,
                    "gallery",
                    Array.Empty<Trait>(),
                    8,
                    new[] { new TypedDamageDice(new DiceExpression(1, 6), "piercing", "gallery") },
                    Array.Empty<TypedFlatDamage>(),
                    5,
                    label is "Shortbow" or "Sling" ? 60 : 0,
                    0,
                    StrikeAmmunitionRequirement.None
                );
        }
    }
}
