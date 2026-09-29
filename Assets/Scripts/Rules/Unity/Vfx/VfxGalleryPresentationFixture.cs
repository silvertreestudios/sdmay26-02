using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
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
        private readonly List<string> trace = new();
        private float persistentHoldSeconds = 0.8f;
        private ActiveEffectInstance currentEffect;
        private ActiveRuleBinding currentBinding;
        private int effectSequence;
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
                SpellPersistentVfxSelector.Select
            );
            rageEffects = new UnityPersistentVfxObserver(
                playback,
                creatures,
                RagePersistentVfxSelector.Select
            );
            lightEffects = UnityLightEffectPresentationObserver.Create(
                spellCatalog,
                creatures,
                playback
            );
            rottingAura = new UnityRottingAuraModule(
                creatures,
                new GridPrivate.Tile[1, 1],
                playback
            );
            playback.CueStarted += RecordCue;
        }

        /// <summary>Gets the exact production cues emitted by the latest fixture timeline.</summary>
        public IReadOnlyList<string> Trace => trace;

        /// <summary>Gets the number of targets staged by the latest fixture timeline.</summary>
        public int TargetCount { get; private set; }

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
            currentEffect = null;
            currentBinding = null;
            TargetCount = 0;
            trace.Clear();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            playback.CueStarted -= RecordCue;
            Reset();
        }

        private IEnumerator PlaySpell(VfxCoverageEntry entry)
        {
            string[] parts = entry.id.Split('/');
            SpellReference spell = new(new SpellId(parts[1]), 1);
            int actions = ParseActions(entry.variant);
            CreatureId[] selected = TargetIds.Take(TargetCount).ToArray();
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
                creatures[TargetIds[0]].traits = living
                    ? new List<string>()
                    : new List<string> { "undead" };
                results.Add(
                    new SpellTargetResolution(
                        TargetIds[0],
                        degree,
                        living
                            ? Array.Empty<TypedDamagePart>()
                            : new[]
                            {
                                new TypedDamagePart("vitality", 8, new[] { spell.Spell.Value }),
                            },
                        living ? 8 : 0,
                        entry.outcome == "critical-failure"
                    )
                );
            }

            CastSpellOutcome outcome = new(
                SourceId,
                spell,
                Array.Empty<ActiveEffectId>(),
                attacks,
                results
            );
            yield return Drain(spellPresenter.PresentBeginning(operation, snapshot));
            yield return Drain(spellPresenter.PresentResolved(operation, outcome, snapshot));

            if (
                spell.Spell.Value
                is "light"
                    or "shield"
                    or "guidance"
                    or "bless"
                    or "infuse-vitality"
            )
            {
                CreatureId owner = selected.FirstOrDefault();
                if (owner.IsEmpty)
                    owner = SourceId;
                yield return CreatePersistentSpell(spell, owner);
            }
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
            yield return Drain(strikePresenter.PresentBeginning(operation, snapshot));
            yield return Drain(strikePresenter.PresentResolved(operation, resolution, snapshot));
        }

        private IEnumerator CreatePersistentSpell(SpellReference spell, CreatureId owner)
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
                owner
            );
            RulesSnapshot effectSnapshot = new RulesState(
                new RulesStateSeed().SeedActiveEffect(effect)
            ).Snapshot;
            ActiveEffectCreatedFact fact = new(effect, currentBinding.Id);
            if (spell.Spell.Value == "light")
                lightEffects.OnFactCommitted(fact, new OpId(effectSequence), effectSnapshot);
            else
                spellEffects.OnFactCommitted(fact, new OpId(effectSequence), effectSnapshot);
            yield return new WaitForSeconds(persistentHoldSeconds);
        }

        private IEnumerator PlayPersistentLifecycle(VfxCoverageEntry entry)
        {
            if (entry.id.StartsWith("auxiliary/rage/", StringComparison.Ordinal))
            {
                bool quick = entry.id.Contains("quick-tempered", StringComparison.Ordinal);
                ActiveEffectInstance effect = CreateEffect(
                    RageActionDefinition.EffectDefinitionId,
                    new RageEffectState(quick),
                    SourceId
                );
                RulesSnapshot effectSnapshot = new RulesState(
                    new RulesStateSeed().SeedActiveEffect(effect)
                ).Snapshot;
                rageEffects.OnFactCommitted(
                    new ActiveEffectCreatedFact(effect, currentBinding.Id),
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
            yield return CreatePersistentSpell(spell, SourceId);
            if (entry.outcome == "refresh active")
            {
                RemoveCurrent(ActiveEffectRemovalReason.Ended);
                yield return CreatePersistentSpell(spell, SourceId);
            }
            else if (entry.outcome is "remove" or "consume" or "expire")
            {
                RemoveCurrent(
                    entry.outcome == "expire"
                        ? ActiveEffectRemovalReason.Expired
                        : ActiveEffectRemovalReason.Ended
                );
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
            rottingAura.OnFactCommitted(fact, new OpId(1), snapshot);
            while (playback.LiveObjectCount > 0)
                yield return null;
        }

        private ActiveEffectInstance CreateEffect(
            RuleDefinitionId definition,
            IEffectState state,
            CreatureId owner
        )
        {
            effectSequence++;
            ActiveEffectId id = new("vfx-gallery-effect-" + effectSequence);
            RuleSource sourceRule = RuleSource.FromSlug("vfx-gallery-committed-fixture");
            currentEffect = new ActiveEffectInstance(
                id,
                definition,
                owner,
                sourceRule,
                EffectDuration.Indefinite,
                state
            );
            currentBinding = new ActiveRuleBinding(
                new BindingId("vfx-gallery-binding-" + effectSequence),
                definition,
                owner,
                id,
                sourceRule,
                effectSequence
            );
            return currentEffect;
        }

        private void RemoveCurrent(ActiveEffectRemovalReason reason)
        {
            if (currentEffect == null || currentBinding == null)
                return;
            ActiveEffectRemovedFact removed = new(currentEffect, currentBinding, reason);
            spellEffects.OnFactCommitted(removed, new OpId(effectSequence), snapshot);
            lightEffects.OnFactCommitted(removed, new OpId(effectSequence), snapshot);
            rageEffects.OnFactCommitted(removed, new OpId(effectSequence), snapshot);
            currentEffect = null;
            currentBinding = null;
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
            if (entry.id is "spell/light/2-action/create" or "spell/shield/1-action/create")
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
            while (routine.MoveNext())
                yield return routine.Current;
        }

        private void RecordCue(VfxCueId cue) => trace.Add(cue.Value);

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
