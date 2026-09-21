using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity;
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
            : base((uint)variant.Actions)
        {
            this.spell = spell;
            this.variant = variant;
            this.actionDefinition =
                actionDefinition ?? throw new ArgumentNullException(nameof(actionDefinition));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>Gets the exact installed spell identity and rank.</summary>
        public SpellReference Spell => spell;

        /// <summary>Gets the installed definition-owned action variant.</summary>
        public SpellActionVariant Variant => variant;

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

                if (!catalog.TryGetSpell(spell, out Game.Rules.Runtime.SpellDefinition definition))
                {
                    Debug.LogWarning("The rules-native spell definition is unavailable.", caster);
                    yield break;
                }
                SpellCastSelection spellSelection = SpellCastSelection.Empty;
                if (
                    catalog is ISpellActionCatalog spellCatalog
                    && spellCatalog.TryGetCastRule(spell.Spell, out ISpellCastRule feature)
                )
                {
                    SpellSelectionProfile profile = feature.GetSelection(variant);
                    List<CreatureId> selected = new();
                    if (
                        profile.Kind == SpellSelectionKind.SingleCreature
                        || profile.Kind == SpellSelectionKind.ExactCreatureCount
                    )
                    {
                        int count = profile.ExactCreatureCount > 0 ? profile.ExactCreatureCount : 1;
                        for (int index = 0; index < count; index++)
                        {
                            CoroutineResult<StrikeTargetResult> targetSelection = new();
                            yield return GridAPI
                                .GetInstance()
                                .GetStrikeTarget(
                                    caster,
                                    new StrikeTargetRequest
                                    {
                                        IsRanged = profile.RangeFeet > 5,
                                        FixedRangeFeet = profile.RangeFeet,
                                        RequiresLineOfEffect = true,
                                        IncludeSelf = true,
                                    },
                                    targetSelection
                                );
                            CreatureComponent target =
                                targetSelection.Value?.Target?.GetComponent<CreatureComponent>();
                            if (
                                target == null
                                || !bridge.TryGetCreatureId(target, out CreatureId targetId)
                            )
                                yield break;
                            selected.Add(targetId);
                        }
                    }
                    else if (
                        profile.Kind == SpellSelectionKind.Cone
                        || profile.Kind == SpellSelectionKind.Emanation
                    )
                    {
                        CoroutineResult<AreaTargetResult> area = new();
                        yield return GridAPI
                            .GetInstance()
                            .GetAreaTarget(
                                caster,
                                new AreaTargetRequest
                                {
                                    Shape =
                                        profile.Kind == SpellSelectionKind.Cone
                                            ? AreaShape.Cone
                                            : AreaShape.Emanation,
                                    SizeFeet = profile.AreaFeet,
                                    IncludeCenter = profile.IncludeCaster,
                                    RequiresLineOfEffect = true,
                                },
                                area
                            );
                        if (area.Value == null)
                            yield break;
                        foreach (
                            CreatureComponent target in area
                                .Value.Creatures.Where(value => value.IsAffected)
                                .Select(value => value.Creature.GetComponent<CreatureComponent>())
                                .Where(value => value != null)
                        )
                        {
                            if (bridge.TryGetCreatureId(target, out CreatureId targetId))
                                selected.Add(targetId);
                        }
                        if (profile.IncludeCaster && !selected.Contains(actor))
                            selected.Insert(0, actor);
                        if (profile.FriendlyOnly)
                        {
                            RulesSelectors selectors = new();
                            selected = selected
                                .Where(target => !selectors.IsEnemy(bridge.Snapshot, actor, target))
                                .ToList();
                        }
                    }
                    spellSelection =
                        selected.Count == 0
                            ? SpellCastSelection.Empty
                            : new SpellCastSelection(selected.Distinct());
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

                CastSpellActionOp operation = null;
                OpResult<CastSpellOutcome> result = null;
                Exception dispatchFailure = null;
                try
                {
                    operation = actionDefinition.CreateOp(actor, spell, variant, spellSelection);
                    result = bridge.Dispatch(operation);
                }
                catch (Exception exception)
                {
                    dispatchFailure = exception;
                }

                if (operation != null)
                    yield return bridge.DrainActionPresentation(operation);

                if (dispatchFailure != null)
                    Debug.LogException(dispatchFailure, caster);
                else if (result is InvalidOpResult<CastSpellOutcome> invalid)
                    Debug.LogWarning($"Cast a Spell was rejected: {invalid.Reason}", caster);
                else if (result is InterruptedOpResult<CastSpellOutcome>)
                    Debug.LogWarning("Cast a Spell was interrupted.", caster);
                else if (result is CancelledOpResult<CastSpellOutcome>)
                    Debug.LogWarning("Cast a Spell was cancelled.", caster);
                else if (result is not ResolvedOpResult<CastSpellOutcome>)
                    Debug.LogWarning("Cast a Spell returned an unknown structural result.", caster);
            }
            finally
            {
                if (controller != null)
                    controller.IsTakingAction = false;
                OnActionComplete.Invoke();
            }
            yield break;
        }
    }
}
