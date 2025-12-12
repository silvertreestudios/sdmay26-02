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
                "baseItem", "bonus", "bonusDamage", "bulk", "category", "damage", "description",
                "group", "material", "price", "publication", "quantity", "range", "reload", "rules",
                "runes", "size", "splashDamage", "traits", "usage"
            };

            var newSystem = new JObject();
            if (system != null)
            {
                foreach (var prop in system.Properties())
                {
                    if (allowedSystemFields.Contains(prop.Name))
                    {
                        newSystem[prop.Name] = prop.Value;
                    }
                }
            }
            output["system"] = newSystem;

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