using System.Linq;
using Newtonsoft.Json.Linq;

namespace Game.DungeonPersistence.Repository
{
    internal static partial class DungeonSaveJsonDocument
    {
        internal static JObject FromCreatureEnvelope(DungeonCreatureSaveState creature) =>
            new()
            {
                ["documentVersion"] = DungeonSaveSchema.CreatureStateVersion,
                ["creature"] = FromCreature(creature),
            };

        internal static JObject FromCreature(DungeonCreatureSaveState creature) =>
            new()
            {
                ["instanceId"] = creature.InstanceId,
                ["creatureContentId"] = creature.CreatureContentId,
                ["cell"] = FromCell(creature.Cell),
                ["health"] = FromHealth(creature.Health),
                ["isDefeated"] = creature.IsDefeated,
                ["conditions"] = new JArray(creature.Conditions.Select(FromCondition)),
                ["timedEffects"] = new JArray(creature.TimedEffects.Select(FromTimedEffect)),
                ["preparedRules"] = FromPreparedRules(creature.PreparedRules),
                ["equipment"] = FromEquipment(creature.Equipment),
            };

        private static JObject FromHealth(DungeonHealthSaveState health) =>
            new()
            {
                ["currentHitPoints"] = health.CurrentHitPoints,
                ["maximumHitPoints"] = health.MaximumHitPoints,
                ["temporaryHitPoints"] = health.TemporaryHitPoints,
                ["temporaryHitPointSourceId"] = health.TemporaryHitPointSourceId,
                ["temporaryHitPointImmunitySourceIds"] = new JArray(
                    health.TemporaryHitPointImmunitySourceIds
                ),
            };

        private static JObject FromCondition(DungeonConditionSaveState condition) =>
            new()
            {
                ["applicationId"] = condition.ApplicationId,
                ["conditionId"] = condition.ConditionId,
                ["sourceInstanceId"] = condition.SourceInstanceId,
                ["value"] = condition.Value,
            };

        private static JObject FromTimedEffect(DungeonTimedEffectSaveState effect) =>
            new()
            {
                ["instanceId"] = effect.InstanceId,
                ["kind"] = effect.Kind,
                ["stateDiscriminator"] = effect.StateDiscriminator,
                ["sourceCreatureId"] = effect.SourceCreatureId,
                ["ownerCreatureId"] = effect.OwnerCreatureId,
                ["targetCreatureId"] = effect.TargetCreatureId,
                ["bindingCreationOrder"] = effect.BindingCreationOrder,
                ["remainingTargetTurnStarts"] = effect.RemainingTargetTurnStarts,
                ["stateJson"] = effect.StateJson,
            };

        private static JObject FromPreparedRules(DungeonPreparedRuleSaveState prepared) =>
            new()
            {
                ["rollOptions"] = new JArray(prepared.RollOptions),
                ["activeEffects"] = new JArray(
                    prepared.ActiveEffects.Select(effect => new JObject
                    {
                        ["effectId"] = effect.EffectId,
                        ["name"] = effect.Name,
                        ["slug"] = effect.Slug,
                        ["sourceSlug"] = effect.SourceSlug,
                    })
                ),
                ["spellPools"] = new JArray(
                    prepared.SpellPools.Select(pool => new JObject
                    {
                        ["poolId"] = pool.PoolId,
                        ["remainingUses"] = pool.RemainingUses,
                        ["maximumUses"] = pool.MaximumUses,
                    })
                ),
            };

        private static JObject FromEquipment(DungeonEquipmentSaveState equipment) =>
            new()
            {
                ["items"] = new JArray(
                    equipment.Items.Select(item => new JObject
                    {
                        ["entryId"] = item.EntryId,
                        ["itemDefinitionId"] = item.ItemDefinitionId,
                        ["quantity"] = item.Quantity,
                        ["slot"] = Slot(item.Slot),
                        ["isLoaded"] = item.IsLoaded,
                    })
                ),
                ["ammunition"] = new JArray(
                    equipment.Ammunition.Select(ammunition => new JObject
                    {
                        ["ammunitionId"] = ammunition.AmmunitionId,
                        ["quantity"] = ammunition.Quantity,
                    })
                ),
            };
    }
}
