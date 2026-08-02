using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Creature;
using Game.KayKit;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Attack;
using Game.Rules.Unity.Composition;
using UnityEngine;

namespace Game.Combat.Spells
{
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
    /// Idempotently replaces legacy spell actions with rules-native actions on one encounter
    /// controller.
    /// </summary>
    public static class UnitySpellActionInstaller
    {
        /// <summary>
        /// Installs exactly one rules action for every prepared, generically supported definition.
        /// </summary>
        /// <remarks>
        /// Encounter composition exclusively uses the typed rules path. Every legacy spell action
        /// is removed, so unsupported or unmigrated prepared spells are absent rather than exposed
        /// through the legacy implementation.
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
            Prepare(controller, actor, catalog).Reconcile();
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
            Dictionary<
                (SpellReference Spell, SpellActionVariant Variant),
                RulesCastSpellAction
            > retained = new();
            foreach (RulesCastSpellAction action in currentActions.OfType<RulesCastSpellAction>())
            {
                var key = (action.Spell, action.Variant);
                if (
                    desired.Contains(key)
                    && action.IsOwnedBy(catalog)
                    && !retained.ContainsKey(key)
                )
                    retained.Add(key, action);
            }
            List<EntityAction> desiredActions = new();
            List<string> desiredCreatureActionNames = new();
            foreach (
                var key in desired
                    .OrderBy(value => value.Spell.ToString(), StringComparer.Ordinal)
                    .ThenBy(value => value.Variant.Actions)
            )
            {
                RulesCastSpellAction action = retained.TryGetValue(
                    key,
                    out RulesCastSpellAction current
                )
                    ? current
                    : new RulesCastSpellAction(key.Spell, key.Variant, catalog);
                desiredActions.Add(action);
                desiredCreatureActionNames.Add(action.ActionName);
            }
            string[] managedCreatureActionNames = currentActions
                .Where(action => action is CastSpellAction || action is RulesCastSpellAction)
                .Select(action => action.ActionName)
                .Concat(desiredCreatureActionNames)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return new UnitySpellActionInstallationPlan(
                controller,
                creature,
                desiredActions,
                managedCreatureActionNames,
                desiredCreatureActionNames
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
                    && definition.Saves.Count == 0
                )
                    throw new InvalidOperationException(
                        $"Prepared rules-native spell '{reference}' has no supported effect, attack, or save."
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
        private readonly IReadOnlyList<EntityAction> desiredActions;
        private readonly IReadOnlyList<string> managedCreatureActionNames;
        private readonly IReadOnlyList<string> desiredCreatureActionNames;

        internal UnitySpellActionInstallationPlan(
            ActionController controller,
            CreatureComponent creature,
            IReadOnlyList<EntityAction> desiredActions,
            IReadOnlyList<string> managedCreatureActionNames,
            IReadOnlyList<string> desiredCreatureActionNames
        )
        {
            this.controller = controller;
            this.creature = creature;
            this.desiredActions = desiredActions;
            this.managedCreatureActionNames = managedCreatureActionNames;
            this.desiredCreatureActionNames = desiredCreatureActionNames;
        }

        /// <inheritdoc/>
        public void Reconcile()
        {
            controller.ReconcileActions(
                action => action is CastSpellAction || action is RulesCastSpellAction,
                desiredActions
            );
            if (creature == null)
                return;
            creature.actions.RemoveAll(actionName =>
                managedCreatureActionNames.Contains(actionName, StringComparer.Ordinal)
            );
            foreach (string actionName in desiredCreatureActionNames)
            {
                if (!creature.actions.Contains(actionName))
                    creature.actions.Add(actionName);
            }
        }
    }

    /// <summary>
    /// Projects a resolved generic cast at most once per dispatcher into shared Unity presentation.
    /// </summary>
    public sealed class UnityResolvedSpellCastPresentationObserver
        : IResolvedOpObserver<CastSpellActionOp, CastSpellOutcome>
    {
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;
        private readonly ISpellDefinitionCatalog catalog;

        /// <summary>Creates the shared presenter for all resolved spell casts.</summary>
        /// <param name="creatures">Live Unity creatures keyed by encounter rules ID.</param>
        /// <param name="catalog">Definitions used for player-facing spell names.</param>
        public UnityResolvedSpellCastPresentationObserver(
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            ISpellDefinitionCatalog catalog
        )
        {
            this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <inheritdoc/>
        public ValueTask OnOperationResolved(
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
            PresentSafely(
                () =>
                {
                    if (!creature.IsDefeated)
                        actor
                            .GetComponent<CreaturePresentation>()
                            ?.PlayAttack(AnimationStyle.Magic);
                },
                actor
            );
            PresentSafely(
                () =>
                {
                    if (CombatLog.TryGetInstance(out CombatLogInterface log))
                        log.Log($"- {actor.name} casts {definition.DisplayName}.");
                },
                actor
            );
            if (result.Attacks.Count == 1)
                PresentAttack(definition, result.Attacks[0], result.Actor, creature);
            return default;
        }

        private void PresentAttack(
            Game.Rules.Runtime.SpellDefinition definition,
            SpellAttackResolution attack,
            CreatureId actor,
            CreatureComponent attacker
        )
        {
            if (attack.Actor != actor)
                throw new InvalidOperationException(
                    "Resolved spell-attack presentation actor does not match the cast."
                );
            if (
                !creatures.TryGetValue(attack.Target, out CreatureComponent target)
                || target == null
            )
                return;
            UnityAttackResultPresentation.Present(
                attacker.gameObject,
                target.gameObject,
                definition.DisplayName,
                new UnityAttackResult(
                    attack.AttackRoll,
                    attack.AttackModifier,
                    attack.ArmorClass,
                    attack.Degree,
                    ToDamage(attack),
                    attack.FinalDamage,
                    attack.MultipleAttackPenalty,
                    0,
                    0
                )
            );
        }

        private static IEnumerable<UnityAttackDamagePart> ToDamage(SpellAttackResolution resolution)
        {
            foreach (TypedDamagePart part in resolution.Damage)
                yield return new UnityAttackDamagePart(part.DamageType, part.Amount);
        }

        private static void PresentSafely(Action presentation, UnityEngine.Object context)
        {
            try
            {
                presentation();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, context);
            }
        }
    }
}
