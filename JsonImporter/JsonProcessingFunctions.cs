using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace JsonImporter
{
    public static class JsonProcessingFunctions
    {
        public static string ProcessWeaponJson(string jsonContent)
        {
            var input = JObject.Parse(jsonContent);

            // Prepare the output root
            var output = new JObject();

            // Always keep "type" at root
            output["type"] = input["type"];

            // Move "name" from system to root (if present)
            var system = input["system"] as JObject;
            if (system != null && system["name"] != null)
            {
                output["name"] = system["name"];
                system.Remove("name");
            }
            else if (input["name"] != null)
            {
                output["name"] = input["name"];
            }

            // Build the new "system" object with only the allowed fields
            var allowedSystemFields = new HashSet<string>
            {
                "baseItem", "bonus", "bonusDamage", "bulk", "category", "damage", "description", "equipped",
                "group", "material", "price", "publication", "quantity", "range", "reload", "rules",
                "runes", "size", "splashDamage", "traits", "usage"
            };

            //REFERENCE ["hands"] = system.SelectToken("equipped.handsHeld"),
            // var newSystem = new JObject();
            if(system.SelectToken("group") != null)
                output["group"] = system.SelectToken("group");
            if(system.SelectToken("category") != null)
                output["category"] = system.SelectToken("category");
            if(system.SelectToken("usage") != null)
                // TODO make sure this applies in all cases
                if(system.SelectToken("usage.value").ToString().Equals("held-in-one-hand", StringComparison.OrdinalIgnoreCase))
                    output["hands"] = 1;
                else
                    output["hands"] = 2;
            if(system.SelectToken("damage") != null)
                output["damageDice"] = system.SelectToken("damage.dice");
                // remove 'd' to make damage die to int, e.g., "1d6" -> 6
                output["damageDie"] = system.SelectToken("damage.die")?.ToString().TrimStart('d');
                output["damageType"] = system.SelectToken("damage.damageType");
            if(system.SelectToken("description") != null){
                // output["description"] = system.SelectToken("description.value");
                HtmlUtils.ExtractPlainAndParagraphs(input.SelectToken("description.value")?.ToString(), out string plainDesc, out JArray plainText, out JArray contexts);
                output["description"] = plainText;
            }
            if(system.SelectToken("traits") != null)
                foreach (var trait in system.SelectToken("traits.value")?.OfType<JValue>() ?? Enumerable.Empty<JValue>())
                {
                    if (trait != null)
                    {
                        if (output["traits"] == null)
                            output["traits"] = new JArray();
                        ((JArray)output["traits"]).Add(trait);
                    }
                }
            if(system.SelectToken("material") != null){
                output["materialType"] = system.SelectToken("material.type");
                output["materialGrade"] = system.SelectToken("material.grade");
            }
            if(system.SelectToken("runes") != null)
                // TODO rework to match rune implementation if needed
                foreach (var rune in system.SelectToken("runes.value")?.OfType<JValue>() ?? Enumerable.Empty<JValue>())
                {
                    if (rune != null)
                    {
                        if (output["runes"] == null)
                            output["runes"] = new JArray();
                        ((JArray)output["runes"]).Add(rune);
                    }
                }
            if(system.SelectToken("price") != null){
                // Assumes gold is standard currency.
                double goldValue = 0.0;
                if (system.SelectToken("price.value.cp") != null)
                    goldValue += system.SelectToken("price.value.cp").Value<double>() / 100.0; // 100 cp = 1 gp
                if (system.SelectToken("price.value.sp") != null)
                    goldValue += system.SelectToken("price.value.sp").Value<double>() / 10.0; // 10 sp = 1 gp
                if (system.SelectToken("price.value.gp") != null)
                    goldValue += system.SelectToken("price.value.gp").Value<double>();
                if (system.SelectToken("price.value.pp") != null)
                    goldValue += system.SelectToken("price.value.pp").Value<double>() * 10.0; // 1 pp = 10 gp
                output["price_GP"] = goldValue;
            }
            if(system.SelectToken("range") != null)
                output["range"] = system.SelectToken("range.value");
            if(system.SelectToken("reload") != null)
                output["reload"] = system.SelectToken("reload.value");
            if(system.SelectToken("ammo") != null)
                output["ammo"] = system.SelectToken("ammo.value");
            if(system.SelectToken("bulk") != null)
                output["bulk"] = system.SelectToken("bulk.value");
            if(system.SelectToken("size") != null)
                output["size"] = system.SelectToken("size.value");
            if(system.SelectToken("baseItem") != null)
                output["baseItem"] = system.SelectToken("baseItem.value");
            if(system.SelectToken("bonus.value") != null)
                output["bonus"] = system.SelectToken("bonus.value");
            if(system.SelectToken("bonusDamage.value") != null)
                output["bonusDamage"] = system.SelectToken("bonusDamage.value");
            if(system.SelectToken("splashDamage") != null)
                output["splashDamage"] = system.SelectToken("splashDamage.value");
            if(system.SelectToken("rules") != null)
                // TODO rework as needed, as of yet rules[] has only been empty
                foreach (var rule in system.SelectToken("rules")?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                {
                    if (rule != null)
                    {
                        if (output["rules"] == null)
                            output["rules"] = new JArray();
                        ((JArray)output["rules"]).Add(rule);
                    }
                }
            if(system.SelectToken("publication") != null)
                 output["publication"] = system.SelectToken("publication");
            // var newSystem = new JObject();
            // if (system != null)
            // {
            //     foreach (var prop in system.Properties())
            //     {
            //         if (allowedSystemFields.Contains(prop.Name))
            //         {
            //             newSystem[prop.Name] = prop.Value;
            //         }
            //     }
            // }
            // output["system"] = newSystem;

            return output.ToString(Newtonsoft.Json.Formatting.Indented);
        }
        public static string ProcessArmorJson(string jsonContent)
        {
            var input = JToken.Parse(jsonContent);

            // Prepare the output root
            var output = new JObject();

            // Always keep "type" at root
            output["type"] = input["type"];
            output["name"] = input["name"];
            // Assumes gold is standard currency.
            double goldValue = 0.0;
            if (input.SelectToken("system.price.value.cp") != null)
                goldValue += input.SelectToken("system.price.value.cp").Value<double>() / 100.0; // 100 cp = 1 gp
            if (input.SelectToken("system.price.value.sp") != null)
                goldValue += input.SelectToken("system.price.value.sp").Value<double>() / 10.0; // 10 sp = 1 gp
            if (input.SelectToken("system.price.value.gp") != null)
                goldValue += input.SelectToken("system.price.value.gp").Value<double>();
            if (input.SelectToken("system.price.value.pp") != null)
                goldValue += input.SelectToken("system.price.value.pp").Value<double>() * 10.0; // 1 pp = 10 gp
            output["price_GP"] = goldValue;
            output["acBonus"] = input.SelectToken("system.acBonus");
            output["dexCap"] = input.SelectToken("system.dexCap");
            output["checkPenalty"] = input.SelectToken("system.checkPenalty");
            output["speedPenalty"] = input.SelectToken("system.speedPenalty");
            output["strengthRequirement"] = input.SelectToken("system.strength");
            //output["description"] = input.SelectToken("system.description.value");
            HtmlUtils.ExtractPlainAndParagraphs(input.SelectToken("system.description.value")?.ToString(), out string plainDesc, out JArray plainText, out JArray contexts);
            output["description"] = plainText;
            output["bulk"] = input.SelectToken("system.bulk.value").Value<double>();
            output["group"] = input.SelectToken("system.group");
            output["armorTraits"] = new JArray();
            foreach (var trait in input.SelectToken("system.traits.value")?.OfType<JValue>() ?? Enumerable.Empty<JValue>())
            {
                if (trait != null)
                {
                    ((JArray)output["armorTraits"]).Add(trait);
                }
            }
            output["runes"] = new JArray();
            foreach (var trait in input.SelectToken("system.runes")?.OfType<JValue>() ?? Enumerable.Empty<JValue>())
            {
                if (trait != null)
                {
                    ((JArray)output["runes"]).Add(trait);
                }
            }
            output["materialType"] = input.SelectToken("system.material.type");
            output["materialGrade"] = input.SelectToken("system.material.grade");
            output["publication"] = input.SelectToken("system.publication");

/*
        string name { get; set; }          // armor name
        string type { get; set; }          // e.g., weapon, armor.  Remove??
        double price { get; set; }         // <int> <currency> --or-- decimal with 1.0=1gp
        int acBonus { get; set; }          // armor class bonus provided by the armor
        int dexCap { get; set; }           // maximum Dexterity modifier that can be applied to AC when wearing this armor
        int checkPenalty { get; set; }      // penalty to certain checks (e.g., stealth) when wearing this armor
        int speedPenalty { get; set; }      // penalty to movement speed when wearing this armor
        int strengthRequirement { get; set; } // minimum Strength score required to wear this armor without penalty
        string description { get; set; }   // text description
        double bulk { get; set; }              // look up uses
        string group { get; set; }         // such as light, medium, heavy, shield, etc.
        List<string> armorTraits { get; set; }  // list of traits
*/

            // TODO FINISH
            return output.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        public static string ProcessEquipmentJson(string jsonContent)
        {
            var obj = JToken.Parse(jsonContent);
            var type = obj["type"]?.ToString();
            if (type != null && type.Equals("weapon", StringComparison.OrdinalIgnoreCase))
            {
                return ProcessWeaponJson(jsonContent);
            }
            else if (type != null && type.Equals("armor", StringComparison.OrdinalIgnoreCase))
            {
                return ProcessArmorJson(jsonContent);
            }
            // Add more type checks here for other equipment types as needed
            // For now, just pretty-print for non-weapon equipment
            return obj.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        public static string ProcessAncestryJson(string jsonContent)
        {
            var obj = JObject.Parse(jsonContent);

            // Example: Move "name" from system to root if present
            var system = obj["system"] as JObject;
            if (system != null && system["name"] != null)
            {
                obj["name"] = system["name"];
                system.Remove("name");
            }

            // Example: Only keep certain fields at root (customize as needed)
            var allowedRootFields = new HashSet<string>
            {
                "name", "system", "type"
            };

            var output = new JObject();
            foreach (var prop in obj.Properties())
            {
                if (allowedRootFields.Contains(prop.Name))
                {
                    output[prop.Name] = prop.Value;
                }
            }

            // Optionally: pretty-print the output
            return output.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        public static string ProcessBackgroundJson(string jsonContent)
        {
            var obj = JObject.Parse(jsonContent);
            var system = obj["system"] as JObject;
            if (system != null)
            {
                JObject foundFeat = null;
                bool foundInRules = false;

                // Check system.items
                var items = system["items"] as JObject;
                if (items != null)
                {
                    foreach (var itemProp in items.Properties())
                    {
                        var itemObj = itemProp.Value as JObject;
                        var uuid = itemObj?["uuid"]?.ToString();
                        if (uuid != null && uuid.Contains("feats"))
                        {
                            foundFeat = itemObj;
                            break;
                        }
                    }
                }

                // If not found in items, check system.rules (array)
                if (foundFeat == null && system["rules"] is JArray rulesArr)
                {
                    foreach (var ruleObj in rulesArr.OfType<JObject>())
                    {
                        var uuid = ruleObj["uuid"]?.ToString();
                        if (uuid != null && uuid.Contains("feats"))
                        {
                            foundFeat = ruleObj;
                            foundInRules = true;
                            break;
                        }
                    }
                }

                if (foundFeat != null)
                {
                    string uuid = foundFeat["uuid"]?.ToString() ?? "";
                    string name = foundFeat["name"]?.ToString();
                    if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(uuid))
                    {
                        var parts = uuid.Split('.');
                        name = parts.Length > 0 ? parts[parts.Length - 1] : uuid;
                    }

                    JObject preselectedChoices = new JObject();
                    if (foundInRules && foundFeat["preselectChoices"] != null)
                    {
                        preselectedChoices = (JObject)foundFeat["preselectChoices"];
                    }

                    var backgroundFeat = new JObject
                    {
                        ["name"] = name ?? "",
                        ["uuid"] = uuid,
                        ["preselectedChoices"] = preselectedChoices
                    };
                    system["backgroundFeat"] = backgroundFeat;
                }

                // Move "name" from system to root if present
                if (system["name"] != null)
                {
                    obj["name"] = system["name"];
                    system.Remove("name");
                }
            }

            // Only keep certain fields at root
            var allowedRootFields = new HashSet<string>
            {
                "name", "system", "type"
            };

            var output = new JObject();
            foreach (var prop in obj.Properties())
            {
                if (allowedRootFields.Contains(prop.Name))
                {
                    output[prop.Name] = prop.Value;
                }
            }

            return output.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        public static string ProcessClassJson(string jsonContent)
        {
            var obj = JObject.Parse(jsonContent);
            var system = obj["system"] as JObject;
            if (system != null && system["items"] is JObject itemsObj)
            {
                // Prepare a list to sort items
                var sortedItems = new List<JObject>();

                foreach (var itemProp in itemsObj.Properties())
                {
                    var itemObj = itemProp.Value as JObject;
                    if (itemObj != null)
                    {
                        // Remove "img"
                        itemObj.Remove("img");
                        // Add to list for sorting
                        sortedItems.Add(itemObj);
                    }
                }

                // Sort by "level"
                sortedItems.Sort((a, b) =>
                {
                    int levelA = a["level"]?.Value<int>() ?? 0;
                    int levelB = b["level"]?.Value<int>() ?? 0;
                    return levelA.CompareTo(levelB);
                });

                // Build new items object with "name" as key
                var newItemsObj = new JObject();
                foreach (var item in sortedItems)
                {
                    string name = item["name"]?.ToString() ?? "";
                    newItemsObj[name] = item;
                }

                system["items"] = newItemsObj;
            }

            // Move "name" from system to root if present
            if (system != null && system["name"] != null)
            {
                obj["name"] = system["name"];
                system.Remove("name");
            }

            // Only keep certain fields at root
            var allowedRootFields = new HashSet<string>
            {
                "name", "system", "type"
            };

            var output = new JObject();
            foreach (var prop in obj.Properties())
            {
                if (allowedRootFields.Contains(prop.Name))
                {
                    output[prop.Name] = prop.Value;
                }
            }

            return output.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        public static string ProcessFeatJson(string jsonContent)
        {
            var obj = JObject.Parse(jsonContent);

            // Example: Move "name" from system to root if present
            var system = obj["system"] as JObject;
            if (system != null && system["name"] != null)
            {
                obj["name"] = system["name"];
                system.Remove("name");
            }

            // Only keep certain fields at root (customize as needed)
            var allowedRootFields = new HashSet<string>
            {
                "name", "system", "type"
            };

            var output = new JObject();
            foreach (var prop in obj.Properties())
            {
                if (allowedRootFields.Contains(prop.Name))
                {
                    output[prop.Name] = prop.Value;
                }
            }

            // Optionally: pretty-print the output
            return output.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        public static string ProcessHeritageJson(string jsonContent)
        {
            var obj = JObject.Parse(jsonContent);

            // Example: Move "name" from system to root if present
            var system = obj["system"] as JObject;
            if (system != null && system["name"] != null)
            {
                obj["name"] = system["name"];
                system.Remove("name");
            }

            // Only keep certain fields at root (customize as needed)
            var allowedRootFields = new HashSet<string>
            {
                "name", "system", "type"
            };

            var output = new JObject();
            foreach (var prop in obj.Properties())
            {
                if (allowedRootFields.Contains(prop.Name))
                {
                    output[prop.Name] = prop.Value;
                }
            }

            // Optionally: pretty-print the output
            return output.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        // Delegate to the relocated implementation
        public static string ProcessMonsterJson(string jsonContent)
        {
            return MonsterJsonProcessor.ProcessMonsterJson(jsonContent);
        }

        public static string ProcessSpellJson(string jsonContent)
        {
            var obj = JObject.Parse(jsonContent);

            // Example: Move "name" from system to root if present
            var system = obj["system"] as JObject;
            if (system != null && system["name"] != null)
            {
                obj["name"] = system["name"];
                system.Remove("name");
            }

            // Only keep certain fields at root (customize as needed)
            var allowedRootFields = new HashSet<string>
            {
                "name", "system", "type"
            };

            var output = new JObject();
            foreach (var prop in obj.Properties())
            {
                if (allowedRootFields.Contains(prop.Name))
                {
                    output[prop.Name] = prop.Value;
                }
            }

            // Optionally: pretty-print the output
            return output.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        // Move all other processing functions here...
    }
}