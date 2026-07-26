using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Creature;
using Game.KayKit;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using UnityEngine;

namespace Game.Combat.Spells
{
    /// <summary>Reads required spellbooks from encounter-owned creature mappings.</summary>
    public sealed class UnitySpellBookProvider : ISpellBookProvider
    {
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;

        /// <summary>Creates an encounter-owned provider.</summary>
        /// <param name="creatures">The live Unity creatures keyed by encounter rules ID.</param>
        public UnitySpellBookProvider(
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures
        ) => this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));

        /// <inheritdoc/>
        public ISpellBook GetSpellBook(CreatureId creature) =>
            creatures.TryGetValue(creature, out CreatureComponent value) && value != null
                ? value.Prepared?.SpellBook ?? EmptySpellBook.Instance
                : EmptySpellBook.Instance;
    }

    /// <summary>Idempotently reconciles definition-backed spell actions on one controller.</summary>
    public static class UnitySpellActionInstaller
    {
        /// <summary>Installs exactly one action for every prepared reference and definition variant.</summary>
        /// <param name="controller">The caster whose action list is reconciled.</param>
        /// <param name="catalog">The data-backed definitions that own action variants.</param>
        public static void Install(ActionController controller, ISpellDefinitionCatalog catalog)
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            CreatureComponent creature = controller.GetComponent<CreatureComponent>();
            ISpellBook book = creature?.Prepared?.SpellBook ?? EmptySpellBook.Instance;
            HashSet<(SpellReference Spell, SpellActionVariant Variant)> desired = new();
            foreach (SpellReference reference in book.CastableSpells)
            {
                if (
                    !catalog.TryGetSpell(
                        reference,
                        out Game.Rules.Runtime.SpellDefinition definition
                    )
                )
                    continue;
                foreach (SpellActionVariant variant in definition.Variants)
                    desired.Add((reference, variant));
            }

            Dictionary<
                (SpellReference Spell, SpellActionVariant Variant),
                CastSpellAction
            > retained = new();
            foreach (
                CastSpellAction action in controller
                    .GetActions()
                    .OfType<CastSpellAction>()
                    .ToArray()
            )
            {
                var key = (action.Spell, action.Variant);
                if (!desired.Contains(key) || retained.ContainsKey(key))
                    controller.RemoveAction(action);
                else
                    retained.Add(key, action);
            }
            foreach (var key in desired)
            {
                if (retained.ContainsKey(key))
                    continue;
                CastSpellAction action = new(key.Spell, key.Variant, catalog);
                controller.AddAction(action);
                if (creature != null && !creature.actions.Contains(action.ActionName))
                    creature.actions.Add(action.ActionName);
            }
        }
    }

    /// <summary>Projects every resolved generic cast exactly once into shared Unity presentation.</summary>
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
            if (
                result.Actor != operation.Actor
                || !creatures.TryGetValue(operation.Actor, out CreatureComponent creature)
                || creature == null
                || !catalog.TryGetSpell(
                    operation.Spell,
                    out Game.Rules.Runtime.SpellDefinition definition
                )
            )
                return default;
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
            PresentSafely(OnGameplayStateCommitted.Invoke, actor);
            return default;
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
