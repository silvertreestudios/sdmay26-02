using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.KayKit;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Attack;
using Game.Rules.Unity.Composition;
using Game.Rules.Unity.Vfx;
using UnityEngine;

namespace Game.Combat.Spells
{
    /// <summary>Captures Unity creature traits and typed defenses for spell resolution.</summary>
    public sealed class UnitySpellCreatureDataProvider : ISpellCreatureDataProvider
    {
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;

        /// <summary>Creates an encounter-owned spell creature-data adapter.</summary>
        public UnitySpellCreatureDataProvider(
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures
        ) => this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));

        /// <inheritdoc/>
        public bool IsUndead(CreatureId creature)
        {
            CreatureComponent value = Require(creature);
            return value.traits != null
                && value.traits.Any(trait =>
                    string.Equals(trait, "undead", StringComparison.OrdinalIgnoreCase)
                );
        }

        /// <inheritdoc/>
        public IReadOnlyList<TypedDefenseAdjustment> GetWeaknesses(CreatureId creature) =>
            UnityAttackDataAdapter.CaptureWeaknesses(Require(creature));

        /// <inheritdoc/>
        public IReadOnlyList<TypedDefenseAdjustment> GetResistances(CreatureId creature) =>
            UnityAttackDataAdapter.CaptureResistances(Require(creature));

        private CreatureComponent Require(CreatureId creature)
        {
            if (!creatures.TryGetValue(creature, out CreatureComponent value) || value == null)
                throw new InvalidOperationException(
                    $"Encounter creature '{creature.Value}' has no live Unity spell data."
                );
            return value;
        }
    }

    /// <summary>Reads spellbooks from required encounter-owned creature mappings.</summary>
    public sealed class UnitySpellBookProvider : ISpellBookProvider
    {
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;

        /// <summary>Creates an encounter-owned provider.</summary>
        /// <param name="creatures">The live Unity creatures keyed by encounter rules ID.</param>
        public UnitySpellBookProvider(
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures
        ) => this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));

        /// <inheritdoc/>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the encounter creature has no live Unity mapping.
        /// </exception>
        public ISpellBook GetSpellBook(CreatureId creature)
        {
            if (!creatures.TryGetValue(creature, out CreatureComponent value) || value == null)
                throw new InvalidOperationException(
                    $"Encounter creature '{creature.Value}' has no live Unity mapping for its spellbook."
                );
            return value.Prepared?.SpellBook ?? EmptySpellBook.Instance;
        }
    }

    /// <summary>
    /// Idempotently binds rules-native spell actions to one encounter controller.
    /// </summary>
    public static class UnitySpellActionInstaller
    {
        /// <summary>
        /// Installs exactly one rules action for every prepared, generically supported definition.
        /// </summary>
        /// <remarks>
        /// Existing rules-native actions are replaced so every installed entry owns the current
        /// encounter's catalog and spellbook dependencies. Unsupported prepared spells are absent.
        /// </remarks>
        /// <param name="controller">The caster whose action list is reconciled.</param>
        /// <param name="actor">The caster's encounter-stable rules identity.</param>
        /// <param name="catalog">The encounter rules catalog that owns definitions and spellbooks.</param>
        public static void Install(
            ActionController controller,
            CreatureId actor,
            ISpellActionCatalog catalog
        )
        {
            Prepare(controller, actor, catalog).Apply();
        }

        /// <summary>Prepares all spell action-list reads and action construction for later apply.</summary>
        internal static UnitySpellActionInstallationPlan Prepare(
            ActionController controller,
            CreatureId actor,
            ISpellActionCatalog catalog
        )
        {
            HashSet<(SpellReference Spell, SpellActionVariant Variant)> desired = GetDesired(
                controller,
                actor,
                catalog
            );
            CreatureComponent creature = controller.GetComponent<CreatureComponent>();
            List<EntityAction> currentActions = controller.GetActions();
            List<EntityAction> removals = currentActions
                .OfType<RulesCastSpellAction>()
                .Cast<EntityAction>()
                .ToList();
            List<EntityAction> additions = new();
            List<string> creatureActionNames = new();
            foreach (var key in desired)
            {
                RulesCastSpellAction action = new(
                    key.Spell,
                    key.Variant,
                    new CastSpellActionDefinition(catalog),
                    catalog
                );
                additions.Add(action);
                if (creature != null && !creature.actions.Contains(action.ActionName))
                    creatureActionNames.Add(action.ActionName);
            }
            return new UnitySpellActionInstallationPlan(
                controller,
                creature,
                removals,
                additions,
                creatureActionNames
            );
        }

        /// <summary>Preflights every rules-native spell definition before encounter ownership commits.</summary>
        /// <param name="controller">The caster whose prepared definitions are validated.</param>
        /// <param name="actor">The caster's proposed encounter-stable rules identity.</param>
        /// <param name="catalog">The encounter rules catalog that owns definitions and spellbooks.</param>
        public static void Validate(
            ActionController controller,
            CreatureId actor,
            ISpellActionCatalog catalog
        ) => _ = Prepare(controller, actor, catalog);

        private static HashSet<(SpellReference Spell, SpellActionVariant Variant)> GetDesired(
            ActionController controller,
            CreatureId actor,
            ISpellActionCatalog catalog
        )
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));
            if (actor.IsEmpty)
                throw new ArgumentException("A spell actor is required.", nameof(actor));
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (controller.GetComponent<CreatureComponent>() == null)
                throw new InvalidOperationException(
                    "A spell action controller requires a creature component."
                );
            HashSet<(SpellReference Spell, SpellActionVariant Variant)> desired = new();
            foreach (SpellReference reference in catalog.GetSpellBook(actor).CastableSpells)
            {
                if (
                    !catalog.TryGetSpell(
                        reference,
                        out Game.Rules.Runtime.SpellDefinition definition
                    )
                )
                    throw new InvalidOperationException(
                        $"Prepared spell '{reference}' for encounter creature '{actor.Value}' has no catalog definition."
                    );
                if (
                    definition.Effects.Count == 0
                    && definition.Attacks.Count == 0
                    && !catalog.TryGetCastRule(reference.Spell, out _)
                )
                    throw new InvalidOperationException(
                        $"Prepared rules-native spell '{reference}' has no supported effect or attack."
                    );
                foreach (SpellActionVariant variant in definition.Variants)
                    desired.Add((reference, variant));
            }
            return desired;
        }
    }

    /// <summary>Applies a fully prepared spell action reconciliation without querying Unity again.</summary>
    internal sealed class UnitySpellActionInstallationPlan : IUnityCombatantInstallationContribution
    {
        private readonly ActionController controller;
        private readonly CreatureComponent creature;
        private readonly IReadOnlyList<EntityAction> removals;
        private readonly IReadOnlyList<EntityAction> additions;
        private readonly IReadOnlyList<string> creatureActionNames;

        internal UnitySpellActionInstallationPlan(
            ActionController controller,
            CreatureComponent creature,
            IReadOnlyList<EntityAction> removals,
            IReadOnlyList<EntityAction> additions,
            IReadOnlyList<string> creatureActionNames
        )
        {
            this.controller = controller;
            this.creature = creature;
            this.removals = removals;
            this.additions = additions;
            this.creatureActionNames = creatureActionNames;
        }

        /// <inheritdoc/>
        public void Apply()
        {
            foreach (EntityAction action in removals)
                controller.RemoveAction(action);
            foreach (EntityAction action in additions)
                controller.AddAction(action);
            foreach (string actionName in creatureActionNames)
            {
                if (!creature.actions.Contains(actionName))
                    creature.actions.Add(actionName);
            }
        }
    }

    /// <summary>Projects every committed resolved cast through feature-owned Unity presentation.</summary>
    public sealed class UnitySpellActionPresenter
        : IUnityActionPresenter<CastSpellActionOp, CastSpellOutcome>
    {
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;
        private readonly ISpellDefinitionCatalog catalog;
        private readonly UnityVfxPlayback vfx;

        /// <summary>Creates the shared presenter for all resolved spell casts.</summary>
        /// <param name="creatures">Live Unity creatures keyed by encounter rules ID.</param>
        /// <param name="catalog">Definitions used for player-facing spell names.</param>
        public UnitySpellActionPresenter(
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            ISpellDefinitionCatalog catalog,
            UnityVfxPlayback vfx
        )
        {
            this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.vfx = vfx ?? throw new ArgumentNullException(nameof(vfx));
        }

        /// <inheritdoc/>
        public IEnumerator PresentBeginning(
            CastSpellActionOp operation,
            RulesSnapshot currentSnapshot
        )
        {
            if (
                !creatures.TryGetValue(operation.Actor, out CreatureComponent creature)
                || creature == null
            )
                throw new InvalidOperationException(
                    $"Encounter spell actor {operation.Actor.Value} has no Unity mapping."
                );
            if (
                !catalog.TryGetSpell(
                    operation.Spell,
                    out Game.Rules.Runtime.SpellDefinition definition
                )
            )
                throw new InvalidOperationException(
                    $"Resolved spell {operation.Spell} has no presentation definition."
                );
            GameObject actor = creature.gameObject;
            CreatureAnimationController animation = creature
                .GetComponent<CreaturePresentation>()
                ?.AnimationController;
            bool animationStarted =
                !creature.IsDefeated
                && actor.GetComponent<CreaturePresentation>()?.PlayAttack(AnimationStyle.Magic)
                    == true;
            if (CombatLog.TryGetInstance(out CombatLogInterface log))
                log.Log($"- {actor.name} casts {definition.DisplayName}.");
            while (
                animationStarted
                && animation != null
                && animation.isActiveAndEnabled
                && animation.IsActionPlaying
            )
                yield return null;
            IEnumerator castVfx = vfx.PlayTransient(
                SpellVfxCueSelector.GetCast(operation.Spell.Spell),
                actor.transform.position + Vector3.up * 0.6f,
                actor.transform.position + Vector3.up * 0.6f,
                lifetimeOwner: actor.transform
            );
            using (castVfx as IDisposable)
            {
                while (castVfx.MoveNext())
                    yield return castVfx.Current;
            }
        }

        /// <inheritdoc/>
        public IEnumerator PresentResolved(
            CastSpellActionOp operation,
            CastSpellOutcome result,
            RulesSnapshot currentSnapshot
        )
        {
            if (result.Actor != operation.Actor)
                throw new InvalidOperationException(
                    "Resolved spell presentation actor does not match its operation."
                );
            if (
                !creatures.TryGetValue(operation.Actor, out CreatureComponent creature)
                || creature == null
            )
                yield break;
            if (!catalog.TryGetSpell(operation.Spell, out var definition))
                yield break;
            foreach (SpellAttackResolution attack in result.AttackResolutions)
            {
                if (
                    !creatures.TryGetValue(attack.Target, out CreatureComponent attackTarget)
                    || attackTarget == null
                )
                    continue;
                IEnumerator projectile = vfx.PlayTransient(
                    SpellVfxCueSelector.GetResult(operation.Spell.Spell, operation.Variant.Actions),
                    creature.transform.position + Vector3.up * 0.6f,
                    attackTarget.transform.position + Vector3.up * 0.6f,
                    lifetimeOwner: creature.transform
                );
                using (projectile as IDisposable)
                {
                    while (projectile.MoveNext())
                        yield return projectile.Current;
                }
                if (attack.Hit)
                {
                    IEnumerator impact = vfx.PlayTransient(
                        SpellVfxCueSelector.GetAttackImpact(operation.Spell.Spell, attack.Degree),
                        attackTarget.transform.position + Vector3.up * 0.6f,
                        attackTarget.transform.position + Vector3.up * 0.6f,
                        attack.Degree == Game.Rules.Runtime.DegreeOfSuccess.CriticalSuccess
                            ? 1.5f
                            : 1f,
                        attackTarget.transform
                    );
                    using (impact as IDisposable)
                    {
                        while (impact.MoveNext())
                            yield return impact.Current;
                    }
                }
                yield return UnityActionPresentationCoordinator.ReactionBarrier;
                PresentAttack(definition, creature, attack);
            }

            bool areaHeal = operation.Spell.Spell.Value == "heal" && operation.Variant.Actions == 3;
            if (
                areaHeal
                && result.TargetResolutions.Count > 0
                && SpellVfxCueSelector.TryGetHealDelivery(
                    operation.Variant.Actions,
                    out VfxCueId areaWave
                )
            )
            {
                Vector3 casterPosition = creature.transform.position + Vector3.up * 0.6f;
                IEnumerator wave = vfx.PlayTransient(
                    areaWave,
                    casterPosition,
                    casterPosition,
                    lifetimeOwner: creature.transform
                );
                using (wave as IDisposable)
                {
                    while (wave.MoveNext())
                        yield return wave.Current;
                }
            }

            List<IEnumerator> simultaneousTargetResults = new();
            foreach (SpellTargetResolution targetResult in result.TargetResolutions)
            {
                if (
                    !creatures.TryGetValue(targetResult.Target, out CreatureComponent target)
                    || target == null
                )
                    continue;
                bool living =
                    target.traits == null
                    || !target.traits.Any(trait =>
                        string.Equals(trait, "undead", StringComparison.OrdinalIgnoreCase)
                    );
                Vector3 targetPosition = target.transform.position + Vector3.up * 0.6f;
                if (
                    operation.Spell.Spell.Value == "heal"
                    && operation.Variant.Actions == 2
                    && SpellVfxCueSelector.TryGetHealDelivery(
                        operation.Variant.Actions,
                        out VfxCueId rangedDelivery
                    )
                )
                {
                    IEnumerator delivery = vfx.PlayTransient(
                        rangedDelivery,
                        creature.transform.position + Vector3.up * 0.6f,
                        targetPosition,
                        lifetimeOwner: creature.transform
                    );
                    using (delivery as IDisposable)
                    {
                        while (delivery.MoveNext())
                            yield return delivery.Current;
                    }
                }
                IEnumerator resultVfx = vfx.PlayTransient(
                    SpellVfxCueSelector.GetResult(
                        operation.Spell.Spell,
                        operation.Variant.Actions,
                        targetResult.Degree,
                        living
                    ),
                    targetPosition,
                    targetPosition,
                    lifetimeOwner: target.transform
                );
                if (areaHeal)
                    simultaneousTargetResults.Add(resultVfx);
                else
                {
                    using (resultVfx as IDisposable)
                    {
                        while (resultVfx.MoveNext())
                            yield return resultVfx.Current;
                    }
                }
            }
            if (simultaneousTargetResults.Count > 0)
            {
                IEnumerator simultaneous = PlaySimultaneously(simultaneousTargetResults);
                using (simultaneous as IDisposable)
                {
                    while (simultaneous.MoveNext())
                        yield return simultaneous.Current;
                }
            }

            if (result.AttackResolutions.Count == 0 && result.TargetResolutions.Count == 0)
            {
                IReadOnlyList<CreatureId> selected = operation.Selection.Creatures;
                if (selected.Count == 0)
                {
                    IEnumerator selfResult = vfx.PlayTransient(
                        SpellVfxCueSelector.GetResult(
                            operation.Spell.Spell,
                            operation.Variant.Actions
                        ),
                        creature.transform.position + Vector3.up * 0.6f,
                        creature.transform.position + Vector3.up * 0.6f,
                        lifetimeOwner: creature.transform
                    );
                    using (selfResult as IDisposable)
                    {
                        while (selfResult.MoveNext())
                            yield return selfResult.Current;
                    }
                }
                List<IEnumerator> selectedTimelines = new();
                foreach (CreatureId selectedTarget in selected)
                {
                    if (
                        !creatures.TryGetValue(selectedTarget, out CreatureComponent target)
                        || target == null
                    )
                        continue;
                    selectedTimelines.Add(
                        vfx.PlayTransient(
                            SpellVfxCueSelector.GetResult(
                                operation.Spell.Spell,
                                operation.Variant.Actions
                            ),
                            creature.transform.position + Vector3.up * 0.6f,
                            target.transform.position + Vector3.up * 0.6f,
                            lifetimeOwner: creature.transform
                        )
                    );
                }
                IEnumerator simultaneous = PlaySimultaneously(selectedTimelines);
                using (simultaneous as IDisposable)
                {
                    while (simultaneous.MoveNext())
                        yield return simultaneous.Current;
                }
            }
        }

        private static IEnumerator PlaySimultaneously(List<IEnumerator> timelines)
        {
            try
            {
                while (timelines.Count > 0)
                {
                    for (int index = 0; index < timelines.Count; )
                    {
                        IEnumerator timeline = timelines[index];
                        if (timeline.MoveNext())
                        {
                            index++;
                            continue;
                        }
                        (timeline as IDisposable)?.Dispose();
                        timelines.RemoveAt(index);
                    }
                    if (timelines.Count > 0)
                        yield return null;
                }
            }
            finally
            {
                foreach (IEnumerator timeline in timelines)
                    (timeline as IDisposable)?.Dispose();
                timelines.Clear();
            }
        }

        private void PresentAttack(
            Game.Rules.Runtime.SpellDefinition definition,
            CreatureComponent attacker,
            SpellAttackResolution resolution
        )
        {
            if (
                !creatures.TryGetValue(resolution.Target, out CreatureComponent target)
                || target == null
            )
                return;
            UnityAttackResultPresentation.Present(
                attacker.gameObject,
                target.gameObject,
                definition.DisplayName,
                new UnityAttackResult(
                    resolution.AttackRoll,
                    resolution.AttackModifier,
                    resolution.ArmorClass,
                    resolution.Degree,
                    resolution.Damage.Select(part => new UnityAttackDamagePart(
                        part.DamageType,
                        part.Amount
                    )),
                    resolution.FinalDamage,
                    resolution.MultipleAttackPenalty,
                    0,
                    0
                )
            );
        }
    }
}
