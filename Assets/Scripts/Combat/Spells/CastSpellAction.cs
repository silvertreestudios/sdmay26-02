using System;
using System.Collections;
using System.Collections.Generic;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using UnityEngine;

namespace Game.Combat.Spells
{
    /// <summary>Presents one exact spell reference and definition-owned action variant.</summary>
    [Serializable]
    public sealed class CastSpellAction : MultiFrameEntityAction
    {
        private readonly SpellReference spell;
        private readonly SpellActionVariant variant;
        private readonly ISpellDefinitionCatalog catalog;

        /// <summary>Creates one spell action-bar entry.</summary>
        public CastSpellAction(
            SpellReference spell,
            SpellActionVariant variant,
            ISpellDefinitionCatalog catalog
        )
            : base((uint)variant.Actions)
        {
            this.spell = spell;
            this.variant = variant;
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>Gets the exact installed spell identity.</summary>
        public SpellReference Spell => spell;

        /// <summary>Gets the selected definition-owned action variant.</summary>
        public SpellActionVariant Variant => variant;

        /// <inheritdoc/>
        public override string ActionName =>
            catalog.TryGetSpell(spell, out Game.Rules.Runtime.SpellDefinition definition)
                ? BuildActionName(definition)
                : "Cast Spell";

        /// <summary>Reconciles all spell actions on one caster without duplicating entries.</summary>
        public static void AddSpellActions(GameObject caster)
        {
            ActionController controller =
                caster != null ? caster.GetComponent<ActionController>() : null;
            if (controller == null)
                return;
            UnitySpellActionInstaller.Install(controller, UnitySpellDefinitionCatalog.Load());
        }

        /// <inheritdoc/>
        public override bool IsAvailable(ActionController controller)
        {
            if (controller == null || !catalog.TryGetSpell(spell, out _))
                return false;
            CreatureComponent creature = controller.GetComponent<CreatureComponent>();
            ISpellBook book = creature?.Prepared?.SpellBook ?? EmptySpellBook.Instance;
            if (
                controller.TryGetCombatRules(
                    out UnityCombatRulesBridge bridge,
                    out CreatureId actor
                )
            )
            {
                SpellCastAuthorization authorization = book.Authorize(
                    actor,
                    spell,
                    new SnapshotSpellSlotStateReader(bridge.Snapshot)
                );
                return authorization.IsAuthorized
                    && bridge.Snapshot.Health.TryGet(actor, out HealthState health)
                    && health.Current > 0
                    && bridge.Snapshot.ActionEconomy.TryGet(actor, out ActionEconomyState economy)
                    && economy.ActionsRemaining >= variant.Actions;
            }
            return controller.ActionPoints >= variant.Actions && book.Authorize(spell).IsAuthorized;
        }

        /// <summary>Executes a selected legacy spell directly for deterministic adapter tests.</summary>
        public CastSpellResult Cast(
            GameObject caster,
            IReadOnlyList<GameObject> targets = null,
            GridPublic.AreaTargetResult area = null
        ) =>
            SpellcastingRuntime.Cast(
                caster,
                spell,
                (uint)variant.Actions,
                targets,
                area,
                spendActions: true
            );

        /// <inheritdoc/>
        public override void Invoke(GameObject target) =>
            CoroutineRunner.Run(InvokeAndComplete(target));

        protected override IEnumerator MFInvoke(GameObject caster)
        {
            yield return InvokeCore(caster);
        }

        private IEnumerator InvokeAndComplete(GameObject caster)
        {
            ActionController controller =
                caster != null ? caster.GetComponent<ActionController>() : null;
            bool resolved = false;
            bool committed = false;
            bool notifyInAction = false;
            try
            {
                if (!catalog.TryGetSpell(spell, out Game.Rules.Runtime.SpellDefinition definition))
                    yield break;

                if (
                    definition.Effects.Count > 0
                    && controller != null
                    && controller.TryGetCombatRules(
                        out UnityCombatRulesBridge bridge,
                        out CreatureId actor
                    )
                )
                {
                    ISpellBook book =
                        controller.GetComponent<CreatureComponent>()?.Prepared?.SpellBook
                        ?? EmptySpellBook.Instance;
                    SpellCastAuthorization authorization = book.Authorize(
                        actor,
                        spell,
                        new SnapshotSpellSlotStateReader(bridge.Snapshot)
                    );
                    if (!authorization.IsAuthorized)
                        yield break;
                    OpResult<CastSpellOutcome> result = bridge.Dispatch(
                        new CastSpellActionOp(actor, spell, variant, authorization)
                    );
                    resolved = result is ResolvedOpResult<CastSpellOutcome>;
                    committed = result.Facts.Count > 0;
                    notifyInAction = committed && !resolved;
                }
                else
                {
                    if (
                        catalog is not ILegacySpellDefinitionCatalog legacyCatalog
                        || !legacyCatalog.TryGetLegacySpell(spell, out ISpellDefinition legacy)
                    )
                        yield break;
                    SpellCastContext context = new(
                        caster,
                        spell,
                        (uint)variant.Actions,
                        spendActions: true,
                        legacy
                    );
                    yield return legacy.SelectAndCast(context);
                    resolved = context.Result.Success;
                    committed = resolved;
                    notifyInAction = resolved;
                }
            }
            finally
            {
                if (controller != null)
                    controller.IsTakingAction = false;
                OnActionComplete.Invoke();
                if (committed && notifyInAction)
                    OnGameplayStateCommitted.Invoke();
                if (CombatManagerInterface.TryGetInstance(out CombatManagerInterface combatManager))
                    combatManager.CheckForEndOfGame();
            }
        }

        private IEnumerator InvokeCore(GameObject caster)
        {
            yield return InvokeAndComplete(caster);
        }

        private string BuildActionName(Game.Rules.Runtime.SpellDefinition definition) =>
            definition.Variants.Count > 1
                ? $"{definition.DisplayName} {variant}"
                : definition.DisplayName;
    }
}
