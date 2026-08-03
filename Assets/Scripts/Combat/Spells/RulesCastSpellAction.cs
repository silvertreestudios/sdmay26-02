using System;
using System.Collections;
using System.Collections.Generic;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Spells;
using GridPublic;
using UnityEngine;

namespace Game.Combat.Spells
{
    /// <summary>
    /// Presents one migrated spell while rules own availability, authorization, costs, and effects.
    /// </summary>
    public sealed class RulesCastSpellAction : MultiFrameEntityAction
    {
        private readonly SpellReference spell;
        private readonly SpellActionVariant variant;
        private readonly CastSpellActionDefinition actionDefinition;
        private readonly ISpellDefinitionCatalog catalog;
        private readonly ISpellActionCatalog ownerCatalog;
        private CastSpellActionOp pendingOperation;

        /// <summary>Creates one rules-native action-bar entry for an exact spell variant.</summary>
        /// <param name="spell">The exact installed spell identity and rank.</param>
        /// <param name="variant">The installed definition-owned action variant.</param>
        /// <param name="actionDefinition">The pure rules action definition.</param>
        /// <param name="catalog">Definition data used only for action-bar presentation.</param>
        public RulesCastSpellAction(
            SpellReference spell,
            SpellActionVariant variant,
            CastSpellActionDefinition actionDefinition,
            ISpellDefinitionCatalog catalog
        )
            : this(spell, variant, actionDefinition, catalog, null) { }

        internal RulesCastSpellAction(
            SpellReference spell,
            SpellActionVariant variant,
            ISpellActionCatalog catalog
        )
            : this(spell, variant, new CastSpellActionDefinition(catalog), catalog, catalog) { }

        private RulesCastSpellAction(
            SpellReference spell,
            SpellActionVariant variant,
            CastSpellActionDefinition actionDefinition,
            ISpellDefinitionCatalog catalog,
            ISpellActionCatalog ownerCatalog
        )
            : base((uint)variant.Actions)
        {
            this.spell = spell;
            this.variant = variant;
            this.actionDefinition =
                actionDefinition ?? throw new ArgumentNullException(nameof(actionDefinition));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.ownerCatalog = ownerCatalog;
        }

        /// <summary>Gets the exact installed spell identity and rank.</summary>
        public SpellReference Spell => spell;

        /// <summary>Gets the installed definition-owned action variant.</summary>
        public SpellActionVariant Variant => variant;

        internal bool IsOwnedBy(ISpellActionCatalog candidate) =>
            ReferenceEquals(ownerCatalog, candidate);

        /// <inheritdoc/>
        public override string ActionName
        {
            get
            {
                if (!catalog.TryGetSpell(spell, out Game.Rules.Runtime.SpellDefinition definition))
                    throw new InvalidOperationException(
                        $"Installed rules-native spell '{spell}' no longer has a catalog definition."
                    );
                return definition.Variants.Count > 1
                    ? $"{definition.DisplayName} {variant}"
                    : definition.DisplayName;
            }
        }

        /// <inheritdoc/>
        public override bool IsAvailable(ActionController controller)
        {
            if (
                controller == null
                || !controller.TryGetCombatRules(
                    out UnityCombatRulesBridge bridge,
                    out CreatureId actor
                )
            )
                return false;
            if (TryGetPendingOperation(out CastSpellActionOp pending))
            {
                return pending.Actor == actor
                    && bridge.HasTurnAuthority(actor)
                    && actionDefinition.HasExactReplayableReceipt(bridge.Snapshot, pending);
            }
            return actionDefinition.GetAvailability(bridge.Snapshot, actor, spell, variant)
                is AvailableActionAvailability;
        }

        /// <inheritdoc/>
        protected override IEnumerator MFInvoke(GameObject caster)
        {
            ActionController controller =
                caster != null ? caster.GetComponent<ActionController>() : null;
            try
            {
                if (
                    controller == null
                    || !controller.TryGetCombatRules(
                        out UnityCombatRulesBridge bridge,
                        out CreatureId actor
                    )
                )
                {
                    Debug.LogWarning(
                        "Rules-native spellcasting requires active combat rules authority.",
                        caster
                    );
                    yield break;
                }

                if (TryGetPendingOperation(out _))
                {
                    try
                    {
                        Report(DispatchPending(bridge), caster);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, caster);
                    }
                    yield break;
                }

                if (!catalog.TryGetSpell(spell, out Game.Rules.Runtime.SpellDefinition definition))
                {
                    Debug.LogWarning("The rules-native spell definition is unavailable.", caster);
                    yield break;
                }
                SpellCastSelection spellSelection = SpellCastSelection.Empty;
                if (definition.Saves.Count > 0)
                {
                    if (definition.Saves.Count != 1)
                    {
                        Debug.LogWarning(
                            "The rules-native spell save target structure is unsupported.",
                            caster
                        );
                        yield break;
                    }
                    SpellAreaTarget target = definition.Saves[0].Target;
                    CoroutineResult<AreaTargetResult> selection = new();
                    yield return GridAPI
                        .GetInstance()
                        .GetAreaTarget(
                            caster,
                            UnitySpellAreaAdapter.ToGridRequest(target),
                            selection
                        );
                    if (selection.Value == null)
                        yield break;
                    if (
                        !SpellcastingRuntime.TryCreateRulesAreaSelection(
                            bridge,
                            selection.Value,
                            out spellSelection,
                            out string reason
                        )
                    )
                    {
                        Debug.LogWarning($"Cast a Spell was rejected: {reason}", caster);
                        yield break;
                    }
                }
                else if (definition.Attacks.Count > 0)
                {
                    if (
                        definition.Attacks.Count != 1
                        || definition.Attacks[0].Target
                            is not OneCreatureSpellAttackTarget oneCreature
                    )
                    {
                        Debug.LogWarning(
                            "The rules-native spell target structure is unsupported.",
                            caster
                        );
                        yield break;
                    }
                    CoroutineResult<StrikeTargetResult> selection = new();
                    yield return GridAPI
                        .GetInstance()
                        .GetStrikeTarget(
                            caster,
                            new StrikeTargetRequest
                            {
                                IsRanged = true,
                                FixedRangeFeet = oneCreature.RangeFeet,
                                RequiresLineOfEffect = true,
                            },
                            selection
                        );
                    if (selection.Value?.Target == null)
                        yield break;
                    CreatureComponent target =
                        selection.Value.Target.GetComponent<CreatureComponent>();
                    if (target == null)
                        yield break;
                    if (!bridge.TryGetCreatureId(target, out CreatureId targetId))
                    {
                        Debug.LogWarning(
                            "Cast a Spell was rejected: Selected target is not registered in the active combat encounter.",
                            caster
                        );
                        yield break;
                    }
                    spellSelection = new SpellCastSelection(new[] { targetId });
                }

                try
                {
                    RetainPendingOperation(actor, spellSelection);
                    Report(DispatchPending(bridge), caster);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, caster);
                }
            }
            finally
            {
                if (controller != null)
                    controller.IsTakingAction = false;
                OnActionComplete.Invoke();
            }
            yield break;
        }

        internal bool TryGetPendingOperation(out CastSpellActionOp operation)
        {
            operation = pendingOperation;
            return operation != null;
        }

        internal CastSpellActionOp RetainPendingOperation(
            CreatureId actor,
            SpellCastSelection selection
        )
        {
            if (pendingOperation != null)
                return pendingOperation;
            pendingOperation = actionDefinition.CreateOp(
                new ActionInvocationId($"unity-cast-{Guid.NewGuid():N}"),
                actor,
                spell,
                variant,
                selection
            );
            return pendingOperation;
        }

        internal void ClearPendingOperation() => pendingOperation = null;

        private OpResult<CastSpellOutcome> DispatchPending(UnityCombatRulesBridge bridge)
        {
            CastSpellActionOp operation =
                pendingOperation
                ?? throw new InvalidOperationException("There is no pending spell cast to replay.");
            OpResult<CastSpellOutcome> result = bridge.Dispatch(operation);
            ClearPendingOperation();
            return result;
        }

        private static void Report(OpResult<CastSpellOutcome> result, GameObject caster)
        {
            if (result is InvalidOpResult<CastSpellOutcome> invalid)
                Debug.LogWarning($"Cast a Spell was rejected: {invalid.Reason}", caster);
            else if (result is InterruptedOpResult<CastSpellOutcome>)
                Debug.LogWarning("Cast a Spell was interrupted.", caster);
            else if (result is CancelledOpResult<CastSpellOutcome>)
                Debug.LogWarning("Cast a Spell was cancelled.", caster);
            else if (result is not ResolvedOpResult<CastSpellOutcome>)
                Debug.LogWarning("Cast a Spell returned an unknown structural result.", caster);
        }
    }
}
