using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity.Attack;
using Game.Rules.Unity.Composition;
using Game.Rules.Unity.Vfx;
using GridPrivate;
using UnityEngine;

namespace Game.Creature.Rules
{
    /// <summary>
    /// Owns Rotting Aura's encounter wiring, Unity data capture, and committed-Fact presentation.
    /// Eligibility, rolls, and damage remain in <see cref="RottingAuraRules"/>.
    /// </summary>
    internal sealed class UnityRottingAuraModule
        : IUnityEncounterDispatcherModule,
            IUnityEncounterRuntimeModule,
            IUnityEncounterTopologyModule,
            IUnityCombatantEnrollmentModule,
            IRottingAuraDataProvider,
            IFactObserver<DamageAppliedFact>,
            IFactObserver<RottingAuraResolvedFact>
    {
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;
        private Tile[,] tiles;
        private readonly UnityVfxPlayback vfx;

        internal UnityRottingAuraModule(
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            Tile[,] tiles,
            UnityVfxPlayback vfx
        )
        {
            this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));
            this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
            this.vfx = vfx ?? throw new ArgumentNullException(nameof(vfx));
        }

        /// <inheritdoc/>
        public void ConfigureDispatcher(RuleDispatcherBuilder builder) =>
            builder.UseRottingAuraRules();

        /// <inheritdoc/>
        public void RegisterRuntime(RuleDispatcher dispatcher, CompositeLifetime lifetime)
        {
            lifetime.Add(dispatcher.RegisterFactObserver<DamageAppliedFact>(this));
            lifetime.Add(dispatcher.RegisterFactObserver<RottingAuraResolvedFact>(this));
        }

        /// <inheritdoc/>
        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder) =>
            PrepareCombatantState(builder);

        private void PrepareCombatantState(UnityCombatantEnrollmentBuilder builder)
        {
            builder.AddRuleBindings(new[] { RottingAuraRules.CreateBinding(builder.CreatureId) });
            if (HasRottingAura(builder.Creature))
                builder.AddInstallation(
                    new RottingAuraPresentationInstallation(
                        vfx,
                        builder.CreatureId,
                        builder.Creature
                    )
                );
        }

        /// <summary>Creates the persistent production area for a deterministic enrolled source.</summary>
        internal void PresentActive(CreatureId source)
        {
            CreatureComponent creature = RequireCreature(source);
            if (!HasRottingAura(creature))
                throw new InvalidOperationException("The presented creature has no Rotting Aura.");
            PresentActive(vfx, source, creature);
        }

        /// <inheritdoc/>
        public RottingAuraTurnData Capture(
            RulesSnapshot snapshot,
            EncounterId encounter,
            CreatureId target
        )
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (!snapshot.Encounters.TryGet(encounter, out EncounterState encounterState))
                throw new InvalidOperationException("The Rotting Aura encounter is unavailable.");
            CreatureComponent targetCreature = RequireCreature(target);
            List<RottingAuraSource> sources = new();
            foreach (InitiativeEntry entry in encounterState.Roster)
            {
                CreatureComponent sourceCreature = RequireCreature(entry.Creature);
                if (!sourceCreature.gameObject.activeInHierarchy)
                    continue;
                foreach (CreatureAura aura in sourceCreature.auras ?? new List<CreatureAura>())
                {
                    if (
                        aura == null
                        || aura.radiusFeet <= 0
                        || !string.Equals(
                            aura.slug,
                            RottingAuraRules.Slug,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        continue;
                    if (
                        CreatureAuraArea.AffectsCreature(
                            CreatureAuraArea.EvaluateEmanation(
                                sourceCreature.gameObject,
                                aura,
                                tiles
                            ),
                            targetCreature.gameObject
                        )
                    )
                        sources.Add(new RottingAuraSource(entry.Creature, sourceCreature.level));
                }
            }

            return new RottingAuraTurnData(
                (targetCreature.traits ?? new List<string>())
                    .Where(trait => !string.IsNullOrWhiteSpace(trait))
                    .Select(Trait.FromSlug),
                UnityAttackDataAdapter.CaptureWeaknesses(targetCreature),
                UnityAttackDataAdapter.CaptureResistances(targetCreature),
                sources
            );
        }

        /// <inheritdoc/>
        public void RefreshTopology(Tile[,] replacement) =>
            tiles = replacement ?? throw new ArgumentNullException(nameof(replacement));

        /// <inheritdoc/>
        public void OnFactCommitted(
            DamageAppliedFact fact,
            OpId observationRootId,
            RulesSnapshot currentSnapshot
        )
        {
            if (
                fact == null
                || !fact.Origin.Value.StartsWith(
                    RottingAuraRules.Slug + "-",
                    StringComparison.Ordinal
                )
            )
                return;
            PlayTick(fact.Creature, fact.Applied);
        }

        /// <inheritdoc/>
        public void OnFactCommitted(
            RottingAuraResolvedFact fact,
            OpId observationRootId,
            RulesSnapshot currentSnapshot
        )
        {
            if (!Application.isPlaying || !CombatLog.TryGetInstance(out CombatLogInterface log))
            {
                if (fact.Outcome.Applied == 0)
                    PlayTick(fact.Target, 0);
                return;
            }
            log.LogEntry(BuildLogEntry(fact));
            if (fact.Outcome.Applied == 0)
                PlayTick(fact.Target, 0);
        }

        private void PlayTick(CreatureId targetId, int applied)
        {
            if (!creatures.TryGetValue(targetId, out CreatureComponent target) || target == null)
                return;
            Vector3 position = target.transform.position + Vector3.up * 0.6f;
            vfx.RunTransient(
                new VfxCueId(
                    applied == 0 ? "auxiliary/rotting-aura-resisted" : "auxiliary/rotting-aura-tick"
                ),
                position,
                position
            );
        }

        internal CombatLogEntry BuildLogEntry(RottingAuraResolvedFact fact)
        {
            if (fact == null)
                throw new ArgumentNullException(nameof(fact));
            CreatureComponent source = RequireCreature(fact.Source);
            CreatureComponent target = RequireCreature(fact.Target);
            CombatLogDamage damage = new() { Total = fact.Outcome.Requested };
            foreach (TypedDamagePart part in fact.Damage)
                damage.Parts.Add(new CombatLogDamagePart(part.DamageType, part.Amount));

            CombatLogEntry entry = new()
            {
                Kind = CombatLogEntryKind.Damage,
                Outcome = CombatLogOutcome.Damage,
                Actor = source.gameObject.name,
                Target = target.gameObject.name,
                Action = "Rotting Aura",
                Message =
                    target.gameObject.name
                    + " takes "
                    + fact.Outcome.Requested
                    + " void damage from Rotting Aura.",
                Damage = damage,
            };
            entry.Tags.Add("aura");
            entry.Tags.Add(RottingAuraRules.Slug);
            entry.Tags.Add("void");
            entry.Details.Add(
                new CombatLogDetail(
                    "Rolled",
                    fact.Roll.Total + " void (" + string.Join(", ", fact.Roll.Values) + ")"
                )
            );
            AddDefenseDetail(entry, "Weakness", "+", fact.Weaknesses);
            AddDefenseDetail(entry, "Resistance", "-", fact.Resistances);
            entry.Details.Add(new CombatLogDetail("Applied", fact.Outcome.Applied + " Hit Points"));
            return entry;
        }

        private static void AddDefenseDetail(
            CombatLogEntry entry,
            string label,
            string sign,
            IEnumerable<TypedDefenseAdjustment> defenses
        )
        {
            TypedDefenseAdjustment adjustment = defenses.FirstOrDefault(value =>
                string.Equals(value.DamageType, "void", StringComparison.OrdinalIgnoreCase)
            );
            if (adjustment != null)
                entry.Details.Add(new CombatLogDetail(label, sign + adjustment.Amount + " void"));
        }

        private static bool HasRottingAura(CreatureComponent creature) =>
            (creature.auras ?? new List<CreatureAura>()).Any(aura =>
                aura != null
                && aura.radiusFeet > 0
                && string.Equals(
                    aura.slug,
                    RottingAuraRules.Slug,
                    StringComparison.OrdinalIgnoreCase
                )
            );

        private static void PresentActive(
            UnityVfxPlayback playback,
            CreatureId source,
            CreatureComponent creature
        )
        {
            int radius = creature
                .auras.Where(aura =>
                    aura != null
                    && string.Equals(
                        aura.slug,
                        RottingAuraRules.Slug,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .Max(aura => aura.radiusFeet);
            playback.SetPersistent(
                "rotting-aura:" + source.Value,
                new VfxCueId("auxiliary/rotting-aura-active"),
                creature.transform,
                Mathf.Clamp(radius / 15f, 0.75f, 2f)
            );
        }

        private sealed class RottingAuraPresentationInstallation
            : IUnityCombatantInstallationContribution
        {
            private readonly UnityVfxPlayback playback;
            private readonly CreatureId source;
            private readonly CreatureComponent creature;

            internal RottingAuraPresentationInstallation(
                UnityVfxPlayback playback,
                CreatureId source,
                CreatureComponent creature
            )
            {
                this.playback = playback ?? throw new ArgumentNullException(nameof(playback));
                this.source = source;
                this.creature = creature ?? throw new ArgumentNullException(nameof(creature));
            }

            /// <inheritdoc/>
            public void Apply() => PresentActive(playback, source, creature);
        }

        private CreatureComponent RequireCreature(CreatureId creature)
        {
            if (
                !creatures.TryGetValue(creature, out CreatureComponent component)
                || component == null
            )
                throw new InvalidOperationException(
                    $"Rotting Aura creature '{creature.Value}' is unavailable."
                );
            return component;
        }
    }

    /// <summary>Identifies Rotting Aura visuals without requiring an active encounter module.</summary>
    internal sealed class RottingAuraVisualization : ICreatureAuraRule
    {
        /// <inheritdoc/>
        public string Slug => RottingAuraRules.Slug;

        /// <inheritdoc/>
        public bool HasVisual(CreatureAura aura) => aura != null && aura.radiusFeet > 0;
    }
}
