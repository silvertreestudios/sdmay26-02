using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Game.DungeonPersistence.Repository
{
    internal static partial class DungeonSaveJsonDocument
    {
        internal static DungeonCreatureSaveState ReadCreatureEnvelope(JObject source)
        {
            const string path = "creature";
            ValidateProperties(source, path, "documentVersion", "creature");
            RequireVersion(
                RequiredInt(source, "documentVersion", path),
                DungeonSaveSchema.CreatureStateVersion,
                path + ".documentVersion"
            );
            return ReadCreature(RequiredObject(source, "creature", path), path + ".creature");
        }

        internal static DungeonCreatureSaveState ReadCreature(JObject source, string path)
        {
            ValidateProperties(
                source,
                path,
                "instanceId",
                "creatureContentId",
                "cell",
                "health",
                "isDefeated",
                "conditions",
                "timedEffects",
                "preparedRules",
                "equipment"
            );
            return new DungeonCreatureSaveState(
                RequiredString(source, "instanceId", path),
                RequiredString(source, "creatureContentId", path),
                ReadCell(RequiredObject(source, "cell", path), path + ".cell"),
                ReadHealth(RequiredObject(source, "health", path), path + ".health"),
                RequiredBool(source, "isDefeated", path),
                ReadObjects(
                    RequiredArray(source, "conditions", path),
                    path + ".conditions",
                    ReadCondition
                ),
                ReadObjects(
                    RequiredArray(source, "timedEffects", path),
                    path + ".timedEffects",
                    ReadTimedEffect
                ),
                ReadPreparedRules(
                    RequiredObject(source, "preparedRules", path),
                    path + ".preparedRules"
                ),
                ReadEquipment(RequiredObject(source, "equipment", path), path + ".equipment")
            );
        }

        private static DungeonHealthSaveState ReadHealth(JObject source, string path)
        {
            ValidateProperties(
                source,
                path,
                "currentHitPoints",
                "maximumHitPoints",
                "temporaryHitPoints",
                "temporaryHitPointSourceId",
                "temporaryHitPointImmunitySourceIds"
            );
            return new DungeonHealthSaveState(
                RequiredInt(source, "currentHitPoints", path),
                RequiredInt(source, "maximumHitPoints", path),
                RequiredInt(source, "temporaryHitPoints", path),
                RequiredString(source, "temporaryHitPointSourceId", path),
                ReadStrings(
                    RequiredArray(source, "temporaryHitPointImmunitySourceIds", path),
                    path + ".temporaryHitPointImmunitySourceIds"
                )
            );
        }

        private static DungeonConditionSaveState ReadCondition(JObject source, string path)
        {
            ValidateProperties(
                source,
                path,
                "applicationId",
                "conditionId",
                "sourceInstanceId",
                "value"
            );
            return new DungeonConditionSaveState(
                RequiredString(source, "applicationId", path),
                RequiredString(source, "conditionId", path),
                RequiredString(source, "sourceInstanceId", path),
                RequiredInt(source, "value", path)
            );
        }

        private static DungeonTimedEffectSaveState ReadTimedEffect(JObject source, string path)
        {
            ValidateProperties(
                source,
                path,
                "instanceId",
                "kind",
                "stateDiscriminator",
                "sourceCreatureId",
                "ownerCreatureId",
                "targetCreatureId",
                "bindingCreationOrder",
                "remainingTargetTurnStarts",
                "stateJson"
            );
            return new DungeonTimedEffectSaveState(
                RequiredString(source, "instanceId", path),
                RequiredString(source, "kind", path),
                RequiredString(source, "stateDiscriminator", path),
                RequiredString(source, "sourceCreatureId", path),
                RequiredString(source, "ownerCreatureId", path),
                RequiredString(source, "targetCreatureId", path),
                RequiredLong(source, "bindingCreationOrder", path),
                RequiredInt(source, "remainingTargetTurnStarts", path),
                RequiredString(source, "stateJson", path)
            );
        }

        private static DungeonPreparedRuleSaveState ReadPreparedRules(JObject source, string path)
        {
            ValidateProperties(source, path, "rollOptions", "activeEffects", "spellPools");
            return new DungeonPreparedRuleSaveState(
                ReadStrings(RequiredArray(source, "rollOptions", path), path + ".rollOptions"),
                ReadObjects(
                    RequiredArray(source, "activeEffects", path),
                    path + ".activeEffects",
                    ReadPreparedEffect
                ),
                ReadObjects(
                    RequiredArray(source, "spellPools", path),
                    path + ".spellPools",
                    ReadSpellPool
                )
            );
        }

        private static DungeonPreparedEffectSaveState ReadPreparedEffect(
            JObject source,
            string path
        )
        {
            ValidateProperties(source, path, "effectId", "name", "slug", "sourceSlug");
            return new DungeonPreparedEffectSaveState(
                RequiredString(source, "effectId", path),
                RequiredString(source, "name", path),
                RequiredString(source, "slug", path),
                RequiredString(source, "sourceSlug", path)
            );
        }

        private static DungeonSpellPoolSaveState ReadSpellPool(JObject source, string path)
        {
            ValidateProperties(source, path, "poolId", "remainingUses", "maximumUses");
            return new DungeonSpellPoolSaveState(
                RequiredString(source, "poolId", path),
                RequiredInt(source, "remainingUses", path),
                RequiredInt(source, "maximumUses", path)
            );
        }

        private static DungeonEquipmentSaveState ReadEquipment(JObject source, string path)
        {
            ValidateProperties(source, path, "items", "ammunition");
            return new DungeonEquipmentSaveState(
                ReadObjects(
                    RequiredArray(source, "items", path),
                    path + ".items",
                    ReadInventoryItem
                ),
                ReadObjects(
                    RequiredArray(source, "ammunition", path),
                    path + ".ammunition",
                    ReadAmmunition
                )
            );
        }

        private static DungeonInventoryItemSaveState ReadInventoryItem(JObject source, string path)
        {
            ValidateProperties(
                source,
                path,
                "entryId",
                "itemDefinitionId",
                "quantity",
                "slot",
                "isLoaded"
            );
            return new DungeonInventoryItemSaveState(
                RequiredString(source, "entryId", path),
                RequiredString(source, "itemDefinitionId", path),
                RequiredInt(source, "quantity", path),
                ReadSlot(RequiredString(source, "slot", path), path + ".slot"),
                RequiredBool(source, "isLoaded", path)
            );
        }

        private static DungeonAmmunitionSaveState ReadAmmunition(JObject source, string path)
        {
            ValidateProperties(source, path, "ammunitionId", "quantity");
            return new DungeonAmmunitionSaveState(
                RequiredString(source, "ammunitionId", path),
                RequiredInt(source, "quantity", path)
            );
        }
    }
}
