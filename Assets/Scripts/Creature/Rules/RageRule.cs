using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace Game.Creature.Rules
{
    /// <summary>
    /// Supplies all Unity-free inputs needed to decide whether Rage can start and which side effects should result.
    /// </summary>
    public sealed class RageRequest
    {
        public CreatureRulesState Creature { get; set; }
        public uint ActionCost { get; set; }
        public Pf2eItemCatalog Catalog { get; set; }
    }

    /// <summary>
    /// Describes the outcome of evaluating Rage, separating PF2e rule decisions from Unity-side mutation.
    /// </summary>
    public sealed class RageRuleResult
    {
        public bool Applied { get; }
        public string FailureReason { get; }
        public IReadOnlyList<RuleEffect> Effects { get; }

        private RageRuleResult(bool applied, string failureReason, IReadOnlyList<RuleEffect> effects)
        {
            Applied = applied;
            FailureReason = failureReason;
            Effects = effects ?? Array.Empty<RuleEffect>();
        }

        /// <summary>
        /// Creates a successful Rage result with the generic side effects that the Unity layer should apply.
        /// </summary>
        /// <param name="effects">The ordered side effects produced by the rule.</param>
        /// <returns>A successful rule result.</returns>
        public static RageRuleResult Success(IReadOnlyList<RuleEffect> effects)
        {
            return new RageRuleResult(true, null, effects);
        }

        /// <summary>
        /// Creates a blocked Rage result with a diagnostic reason and no side effects.
        /// </summary>
        /// <param name="reason">The rules reason Rage could not be applied.</param>
        /// <returns>A blocked rule result.</returns>
        public static RageRuleResult Blocked(string reason)
        {
            return new RageRuleResult(false, reason, Array.Empty<RuleEffect>());
        }
    }

    /// <summary>
    /// Owns PF2e Rage behavior while remaining independent of GameObject, MonoBehaviour, and UI state.
    /// </summary>
    public static class RageRule
    {
        private const string RageSource = "rage";
        private const string RageTempHpImmunitySource = "rage-temp-hp-immunity";

        /// <summary>
        /// Checks Rage eligibility using prepared rule state and current creature facts.
        /// </summary>
        /// <param name="request">The Unity-free Rage inputs.</param>
        /// <returns>True when Rage can currently be applied.</returns>
        public static bool CanApply(RageRequest request)
        {
            return GetBlockReason(request) == null;
        }

        /// <summary>
        /// Applies the Rage rule to prepared state and returns generic side effects for the host layer to perform.
        /// </summary>
        /// <param name="request">The Unity-free Rage inputs.</param>
        /// <returns>The rule result, including any side effects needed to reflect successful Rage in Unity.</returns>
        public static RageRuleResult Apply(RageRequest request)
        {
            string blockReason = GetBlockReason(request);
            if (blockReason != null)
                return RageRuleResult.Blocked(blockReason);

            CreatureRulesState creature = request.Creature;
            PreparedCharacter prepared = creature.Prepared;
            Pf2eItemCatalog catalog = request.Catalog ?? Pf2eItemCatalog.Instance;
            Pf2eItem rageAction = catalog.Resolve("Rage");
            string effectUuid = rageAction?.System?.SelectToken("selfEffect.uuid")?.Value<string>();
            Pf2eItem rageEffect = catalog.Resolve(effectUuid) ?? catalog.Resolve("Effect: Rage");
            prepared.AddActiveEffect(rageEffect, RageSource);

            List<RuleEffect> effects = new()
            {
                RuleEffect.SpendActions(request.ActionCost),
                RuleEffect.SetTakingActionFalse()
            };

            int tempHp = Math.Max(0, creature.Level + creature.ConstitutionModifier);
            if (!creature.HasTempHpImmunity(RageSource) && tempHp > 0)
                effects.Add(RuleEffect.GainSourceTempHp(RageSource, tempHp));

            return RageRuleResult.Success(effects);
        }

        /// <summary>
        /// Ends active Rage in prepared state and emits cleanup effects, including temporary Hit Point immunity.
        /// </summary>
        /// <param name="creature">The Unity-free creature facts and prepared state.</param>
        /// <param name="catalog">Optional catalog override for resolving the immunity effect in tests.</param>
        /// <returns>A rule result containing cleanup effects, or a blocked result when the creature is not raging.</returns>
        public static RageRuleResult End(CreatureRulesState creature, Pf2eItemCatalog catalog = null)
        {
            if (creature?.Prepared == null || !creature.Prepared.HasActiveEffect(RageSource))
                return RageRuleResult.Blocked("Creature is not raging.");

            creature.Prepared.RemoveActiveEffect(RageSource);
            catalog ??= Pf2eItemCatalog.Instance;
            Pf2eItem immunity = catalog.Resolve("Effect: Rage Temporary Hit Points Immunity");
            creature.Prepared.AddActiveEffect(immunity, RageTempHpImmunitySource);

            return RageRuleResult.Success(new List<RuleEffect>
            {
                RuleEffect.RemoveSourceTempHp(RageSource),
                RuleEffect.AddTempHpImmunity(RageSource)
            });
        }

        private static string GetBlockReason(RageRequest request)
        {
            CreatureRulesState creature = request?.Creature;
            if (creature?.Prepared == null)
                return "Creature is not prepared for PF2e rules.";

            if (creature.Prepared.HasActiveEffect(RageSource))
                return "Creature is already raging.";

            if (creature.HasCondition("Fatigued"))
                return "Creature is fatigued.";

            if (creature.HasCondition("Encumbered"))
                return "Creature is encumbered.";

            if (string.Equals(creature.ArmorCategory, "heavy", StringComparison.OrdinalIgnoreCase)
                && !creature.Prepared.RollOptions.Contains("feat:invulnerable-rager"))
                return "Creature is wearing heavy armor.";

            return null;
        }
    }
}
