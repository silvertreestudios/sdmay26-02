using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace JsonImporter
{
    public static class MonsterJsonProcessor
    {
        // Moved from JsonProcessingFunctions.ProcessMonsterJson
        public static string ProcessMonsterJson(string jsonContent)
        {
            var obj = JObject.Parse(jsonContent);
            // TODO add fields for derived proficiencies?
            // ie: plus to hit - ability mod = proficiency bonus with that weapon type

            // Remove "_id" and "img" fields from the root object
            obj.Remove("_id");
            obj.Remove("img");

            // Prepare arrays for items and equipment
            var newItemsArr = new JArray();
            var equipmentArr = new JArray();
            var conditionsArr = new JArray();
            var reactionsArr = new JArray();
            var passivesArr = new JArray();
            //? var reactionsArr = new JArray();

            if (obj["items"] is JArray itemsArr)
            {
                foreach (var item in itemsArr.OfType<JObject>())
                {
                    // create a readily-available clone of the item's system for branches that need it
                    var clonedSystem = item["system"] != null ? item["system"].DeepClone() as JObject : null;

                    string type = item["type"]?.ToString();
                    if (type == "weapon" || type == "ammo")
                    {
                        var system = item["system"] as JObject;
                        var newEquip = new JObject
                        {
                            ["name"] = item["name"],
                            ["type"] = item["type"],
                            ["hands"] = system.SelectToken("equipped.handsHeld"),
                            ["category"] =system.SelectToken("category"),
                            ["quantity"] = system?["quantity"]?.Value<int?>() ?? 1
                        };
                        equipmentArr.Add(newEquip);
                    }
                    else if (type == "armor")
                    {
                        var system = item["system"] as JObject;
                        var newEquip = new JObject
                        {
                            ["name"] = item["name"],
                            ["type"] = item["type"],
                            ["hands"] = system.SelectToken("equipped.handsHeld"),
                            ["category"] =system.SelectToken("category"),
                            ["quantity"] = system?["quantity"]?.Value<int?>() ?? 1,
                            ["acBonus"] = system.SelectToken("acBonus"),
                            ["dexCap"] = system.SelectToken("dexCap")
                        };
                        equipmentArr.Add(newEquip);
                    }
                    else if (type == "melee" || type == "ranged")
                    {
                        // Option A: start with the full original system, then apply normalizations
                        var mergedSystem = item["system"] != null ? item["system"].DeepClone() as JObject : null;

                        // Normalize damageRolls into a JArray of { "damage", "damageType" } objects.
                        // This replaces any named properties with indexed array positions.
                        if (mergedSystem != null && mergedSystem["damageRolls"] != null)
                        {
                            var damageToken = mergedSystem["damageRolls"];
                            // If damageRolls is an object
                            if (damageToken is JObject damageRollsObj)
                            {
                                // Distinguish between a "flat" single-roll object (has damage/damageType directly)
                                // and a multi-roll object (named properties whose values are roll objects).
                                bool isFlatSingle = damageRollsObj.Properties().Any(p =>
                                    p.Name.Equals("damage", StringComparison.OrdinalIgnoreCase) ||
                                    p.Name.Equals("damageType", StringComparison.OrdinalIgnoreCase));

                                if (isFlatSingle)
                                {
                                    var elem = new JObject();
                                    if (damageRollsObj["damage"] != null) elem["damage"] = damageRollsObj["damage"];
                                    if (damageRollsObj["damageType"] != null) elem["damageType"] = damageRollsObj["damageType"];
                                    mergedSystem["damageRolls"] = new JArray(elem);
                                }
                                else
                                {
                                    var arr = new JArray();
                                    foreach (var prop in damageRollsObj.Properties())
                                    {
                                        if (prop.Value is JObject rollObj)
                                        {
                                            var elem = new JObject();
                                            if (rollObj["damage"] != null) elem["damage"] = rollObj["damage"];
                                            if (rollObj["damageType"] != null) elem["damageType"] = rollObj["damageType"];
                                            arr.Add(elem);
                                        }
                                    }
                                    if (arr.Count > 0)
                                        mergedSystem["damageRolls"] = arr;
                                    else
                                        mergedSystem.Remove("damageRolls");
                                }
                            }
                            // If damageRolls is already an array, normalize each entry
                            else if (damageToken is JArray damageArr)
                            {
                                var arr = new JArray();
                                foreach (var token in damageArr)
                                {
                                    if (token is JObject rollObj)
                                    {
                                        var elem = new JObject();
                                        if (rollObj["damage"] != null) elem["damage"] = rollObj["damage"];
                                        if (rollObj["damageType"] != null) elem["damageType"] = rollObj["damageType"];
                                        arr.Add(elem);
                                    }
                                }
                                if (arr.Count > 0)
                                    mergedSystem["damageRolls"] = arr;
                                else
                                    mergedSystem.Remove("damageRolls");
                            }
                            else
                            {
                                // Unrecognized shape -> remove to keep output consistent
                                mergedSystem.Remove("damageRolls");
                            }
                        }

                        // Omit publication if title is empty (apply to merged system)
                        var publication = mergedSystem?["publication"] as JObject;
                        if (publication != null && string.IsNullOrEmpty(publication["title"]?.ToString()))
                        {
                            mergedSystem.Remove("publication");
                        }

                        // Normalize description.value -> descriptionParagraphs only (omit descriptionPlain by default)
                        if (mergedSystem != null && mergedSystem["description"] != null)
                        {
                            var descToken = mergedSystem["description"];
                            string descHtml = descToken.Type == JTokenType.Object && descToken["value"] != null
                                ? descToken["value"].ToString()
                                : descToken.ToString();

                            // Request only paragraphs and contexts by default. If descHtml is empty, helper will return paragraphs = null.
                            HtmlUtils.ExtractPlainAndParagraphs(descHtml, out _, out JArray descParagraphs, out JArray descContexts);

                            // Remove any existing entries to avoid duplicates
                            mergedSystem.Property("descriptionPlain")?.Remove();
                            mergedSystem.Property("descriptionParagraphs")?.Remove();
                            mergedSystem.Property("descriptionContext")?.Remove();

                            if (descParagraphs != null)
                            {
                                var descProp = mergedSystem.Property("description");
                                if (descProp != null)
                                    descProp.AddAfterSelf(new JProperty("descriptionParagraphs", descParagraphs));
                                else
                                    mergedSystem["descriptionParagraphs"] = descParagraphs;

                                // Add contexts immediately after paragraphs when present
                                if (descContexts != null)
                                {
                                    var parProp = mergedSystem.Property("descriptionParagraphs");
                                    if (parProp != null)
                                        parProp.AddAfterSelf(new JProperty("descriptionContext", descContexts));
                                    else
                                        mergedSystem["descriptionContext"] = descContexts;
                                }
                            }
                            else if (descContexts != null)
                            {
                                // paragraphs missing but contexts exist: add context after description
                                var descProp = mergedSystem.Property("description");
                                if (descProp != null)
                                    descProp.AddAfterSelf(new JProperty("descriptionContext", descContexts));
                                else
                                    mergedSystem["descriptionContext"] = descContexts;
                            }
                        }

                        // Promote range and traits remain in mergedSystem if present.
                        // mergedSystem already contains bonus, damageRolls, range, traits, and other original fields.
                        // We keep the full mergedSystem (original content) but with normalized fields.

                        var newItem = new JObject
                        {
                            ["name"] = item["name"],
                            ["type"] = item["type"],
                            // put the merged (full + normalized) system into system
                            ["system"] = mergedSystem
                            // Note: systemRaw is dropped per your instruction
                        };

                        newItemsArr.Add(newItem);
                    }
                    else if (type == "action")
                    {
                        // use the clonedSystem prepared at the top of the loop
                        var cloned = clonedSystem != null ? clonedSystem.DeepClone() as JObject : null;

                        // Omit publication if title is empty
                        var pub = cloned?["publication"] as JObject;
                        if (pub != null && string.IsNullOrEmpty(pub["title"]?.ToString()))
                        {
                            cloned.Remove("publication");
                        }

                        // Normalize description.value -> descriptionParagraphs only (omit descriptionPlain)
                        if (cloned != null && cloned["description"] != null)
                        {
                            var descToken = cloned["description"];
                            string descHtml = descToken.Type == JTokenType.Object && descToken["value"] != null
                                ? descToken["value"].ToString()
                                : descToken.ToString();

                            HtmlUtils.ExtractPlainAndParagraphs(descHtml, out _, out JArray descParagraphs, out JArray descContexts);

                            cloned.Property("descriptionPlain")?.Remove();
                            cloned.Property("descriptionParagraphs")?.Remove();
                            cloned.Property("descriptionContext")?.Remove();

                            if (descParagraphs != null)
                            {
                                var descProp = cloned.Property("description");
                                if (descProp != null)
                                    descProp.AddAfterSelf(new JProperty("descriptionParagraphs", descParagraphs));
                                else
                                    cloned["descriptionParagraphs"] = descParagraphs;

                                if (descContexts != null)
                                {
                                    var parProp = cloned.Property("descriptionParagraphs");
                                    if (parProp != null)
                                        parProp.AddAfterSelf(new JProperty("descriptionContext", descContexts));
                                    else
                                        cloned["descriptionContext"] = descContexts;
                                }
                            }
                            else if (descContexts != null)
                            {
                                var descProp = cloned.Property("description");
                                if (descProp != null)
                                    descProp.AddAfterSelf(new JProperty("descriptionContext", descContexts));
                                else
                                    cloned["descriptionContext"] = descContexts;
                            }
                        }

                        var newItem = new JObject
                        {
                            ["name"] = item["name"],
                            ["type"] = item["type"],
                            // keep action system mostly intact but with description normalized
                            ["system"] = cloned
                        };


                        if (item["system"]?["actionType"]?["value"]?.ToString() == "reaction")
                        {
                            reactionsArr.Add(newItem);
                        }else if (item["system"]?["actionType"]?["value"]?.ToString() == "passive")
                        {
                            passivesArr.Add(newItem);
                        }
                        else
                        {
                            newItemsArr.Add(newItem);
                        }
                    }
                    else
                    {
                        // keep other items as-is, but normalize any description under system if present
                        var cloned = item.DeepClone() as JObject;
                        var clonedSys = cloned?["system"] as JObject;
                        if (clonedSys != null && clonedSys["description"] != null)
                        {
                            var descToken = clonedSys["description"];
                            string descHtml = descToken.Type == JTokenType.Object && descToken["value"] != null
                                ? descToken["value"].ToString()
                                : descToken.ToString();

                            HtmlUtils.ExtractPlainAndParagraphs(descHtml, out _, out JArray descParagraphs, out JArray descContexts);

                            clonedSys.Property("descriptionPlain")?.Remove();
                            clonedSys.Property("descriptionParagraphs")?.Remove();
                            clonedSys.Property("descriptionContext")?.Remove();

                            if (descParagraphs != null)
                            {
                                var descProp = clonedSys.Property("description");
                                if (descProp != null)
                                {
                                    descProp.AddAfterSelf(new JProperty("descriptionParagraphs", descParagraphs));
                                }
                                else
                                {
                                    clonedSys["descriptionParagraphs"] = descParagraphs;
                                }

                                if (descContexts != null)
                                {
                                    var parProp = clonedSys.Property("descriptionParagraphs");
                                    if (parProp != null)
                                        parProp.AddAfterSelf(new JProperty("descriptionContext", descContexts));
                                    else
                                        clonedSys["descriptionContext"] = descContexts;
                                }
                            }
                            else if (descContexts != null)
                            {
                                var descProp = clonedSys.Property("description");
                                if (descProp != null)
                                {
                                    descProp.AddAfterSelf(new JProperty("descriptionContext", descContexts));
                                }
                                else
                                {
                                    clonedSys["descriptionContext"] = descContexts;
                                }
                            }
                        }

                        newItemsArr.Add(cloned ?? item);
                    }
                }
            }

            // Reorder items so melee/ranged (weapons) come first � convenient when consumer
            // picks the "first" item for attack-related values.
            var orderedItems = new JArray(
                newItemsArr
                    .OfType<JObject>()
                    .OrderByDescending(o =>
                    {
                        var t = o["type"]?.ToString();
                        return (t == "melee" || t == "ranged") ? 1 : 0;
                    })
            );

            // Reformat skills into a flat object map: { "acrobatics": 5, ... }
            var systemObj = obj["system"] as JObject;
            if (systemObj != null && systemObj["skills"] is JObject skillsObj)
            {
                var flattenedSkills = new JObject();
                foreach (var prop in skillsObj.Properties())
                {
                    var valObj = prop.Value as JObject;
                    int baseVal = 0;
                    if (valObj != null)
                    {
                        baseVal = valObj["base"]?.Value<int?>() ?? valObj["mod"]?.Value<int?>() ?? valObj["value"]?.Value<int?>() ?? 0;
                    }
                    flattenedSkills[prop.Name] = baseVal;
                }
                systemObj["skills"] = flattenedSkills;
            }

            // Reformat speed into a unified array where base speed stays as { value: n }
            // and special movement modes become single-key objects like { Swim: n }.
            var attributesForSpeed = systemObj?["attributes"] as JObject;
            if (attributesForSpeed != null && attributesForSpeed["speed"] is JObject speedObj)
            {
                var speedEntries = new JArray();

                var baseSpeedToken = speedObj["value"] ?? speedObj["base"];
                if (baseSpeedToken != null)
                {
                    speedEntries.Add(new JObject
                    {
                        ["value"] = GetIntValue(baseSpeedToken)
                    });
                }

                if (speedObj["otherSpeeds"] is JArray otherSpeeds)
                {
                    foreach (var entry in otherSpeeds.OfType<JObject>())
                    {
                        var type = entry["type"]?.ToString();
                        if (string.IsNullOrWhiteSpace(type))
                            continue;

                        var movementName = char.ToUpperInvariant(type[0]) + type.Substring(1);
                        speedEntries.Add(new JObject
                        {
                            [movementName] = GetIntValue(entry["value"])
                        });
                    }
                }

                attributesForSpeed["speed"] = speedEntries;
            }

            // Reformat abilities from { str: { mod: 4 }, ... } to { str: 4, ... }
            if (systemObj != null && systemObj["abilities"] is JObject abilitiesObj)
            {
                var abilityOrder = new[] { "str", "dex", "con", "int", "wis", "cha" };
                var flattenedAbilities = new JObject();

                foreach (var ability in abilityOrder)
                {
                    var token = abilitiesObj[ability];
                    if (token == null)
                        continue;

                    if (token is JObject abilityObj)
                    {
                        int value = abilityObj["mod"]?.Value<int?>()
                            ?? abilityObj["value"]?.Value<int?>()
                            ?? 0;
                        flattenedAbilities[ability] = value;
                    }
                    else if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
                    {
                        flattenedAbilities[ability] = token.DeepClone();
                    }
                    else
                    {
                        flattenedAbilities[ability] = token;
                    }
                }

                foreach (var prop in abilitiesObj.Properties())
                {
                    if (flattenedAbilities[prop.Name] == null)
                    {
                        flattenedAbilities[prop.Name] = prop.Value.DeepClone();
                    }
                }

                systemObj["abilities"] = flattenedAbilities;
            }

            // Flatten details and normalize publicNotes HTML into paragraphs.
            var detailsObj = obj["system"]?["details"] as JObject;
            if (detailsObj != null)
            {
                detailsObj.Property("blurb")?.Remove();

                var levelToken = detailsObj["level"];
                if (levelToken is JObject levelObj)
                {
                    detailsObj["level"] = GetIntValue(levelObj["value"], GetIntValue(levelObj["base"], GetIntValue(levelObj["mod"])));
                }
                else if (levelToken != null)
                {
                    detailsObj["level"] = GetIntValue(levelToken);
                }
            }

            if (detailsObj != null && detailsObj["publicNotes"] != null)
            {
                var publicNotesToken = detailsObj["publicNotes"];
                // obtain html string as before
                string html = publicNotesToken.Type == JTokenType.Object && publicNotesToken["value"] != null
                    ? publicNotesToken["value"].ToString()
                    : publicNotesToken.ToString();

                // call helper
                HtmlUtils.ExtractPlainAndParagraphs(html, out string plain, out JArray paragraphs, out JArray contexts);

                // remove existing properties and insert immediately after publicNotes (existing logic)
                detailsObj.Property("publicNotesPlain")?.Remove();
                detailsObj.Property("publicNotesParagraphs")?.Remove();
                // remove privateNotes since it is an empty field in every file we've seen
                detailsObj.Property("privateNotes")?.Remove();             
                var publicNotesProp = detailsObj.Property("publicNotes");
                if (publicNotesProp != null)
                {
                    publicNotesProp.AddAfterSelf(new JProperty("publicNotesParagraphs", paragraphs));
                    //publicNotesProp.AddAfterSelf(new JProperty("publicNotesPlain", plain));
                }
                else
                {
                    //detailsObj["publicNotesPlain"] = plain;
                    detailsObj["publicNotesParagraphs"] = paragraphs;
                }
            }

            // Promote nested attribute arrays into the top-level system fields.
            if (systemObj != null)
            {
                var savesObj = systemObj["saves"] as JObject;
                if (savesObj != null)
                {
                    foreach (var key in new[] { "fortitude", "reflex", "will" })
                    {
                        var token = savesObj[key];
                        if (token is JObject tokenObj)
                        {
                            savesObj[key] = GetIntValue(tokenObj["value"], GetIntValue(tokenObj["base"], GetIntValue(tokenObj["mod"])));
                        }
                        else if (token != null)
                        {
                            savesObj[key] = GetIntValue(token);
                        }
                    }
                }

                var attributesObj = systemObj["attributes"] as JObject;
                if (attributesObj != null)
                {
                    foreach (var key in new[] { "ac", "allSaves" })
                    {
                        var token = attributesObj[key];
                        if (token is JObject tokenObj)
                        {
                            attributesObj[key] = GetIntValue(tokenObj["value"], GetIntValue(tokenObj["base"], GetIntValue(tokenObj["mod"])));
                        }
                        else if (token != null)
                        {
                            attributesObj[key] = GetIntValue(token);
                        }
                    }
                }

                var immunitiesArr = new JArray();
                if (systemObj["attributes"]?["immunities"] is JArray sourceImmunities)
                {
                    foreach (var immunity in sourceImmunities)
                    {
                        immunitiesArr.Add(immunity.DeepClone());
                    }
                }
                else if (systemObj["immunities"] is JArray existingImmunities)
                {
                    foreach (var immunity in existingImmunities)
                    {
                        immunitiesArr.Add(immunity.DeepClone());
                    }
                }

                var weaknessesArr = new JArray();
                if (systemObj["attributes"]?["weaknesses"] is JArray sourceWeaknesses)
                {
                    foreach (var weakness in sourceWeaknesses)
                    {
                        weaknessesArr.Add(weakness.DeepClone());
                    }
                }
                else if (systemObj["weaknesses"] is JArray existingWeaknesses)
                {
                    foreach (var weakness in existingWeaknesses)
                    {
                        weaknessesArr.Add(weakness.DeepClone());
                    }
                }

                var resistancesArr = new JArray();
                if (systemObj["attributes"]?["resistances"] is JArray sourceResistances)
                {
                    foreach (var resistance in sourceResistances)
                    {
                        resistancesArr.Add(resistance.DeepClone());
                    }
                }
                else if (systemObj["resistances"] is JArray existingResistances)
                {
                    foreach (var resistance in existingResistances)
                    {
                        resistancesArr.Add(resistance.DeepClone());
                    }
                }

                systemObj["immunities"] = immunitiesArr;
                systemObj["weaknesses"] = weaknessesArr;
                systemObj["resistances"] = resistancesArr;

                attributesObj?.Property("immunities")?.Remove();
                attributesObj?.Property("weaknesses")?.Remove();
                attributesObj?.Property("resistances")?.Remove();
            }

            var traitsObj = systemObj?["traits"] as JObject;
            if (traitsObj != null)
            {
                var sizeToken = traitsObj["size"];
                if (sizeToken is JObject sizeObj)
                {
                    traitsObj["size"] = sizeObj["value"]?.ToString() ?? sizeObj["base"]?.ToString() ?? sizeObj["mod"]?.ToString() ?? string.Empty;
                }
                else if (sizeToken != null)
                {
                    traitsObj["size"] = sizeToken.ToString();
                }
            }
            
            JObject weaponProfs = InferProficiencies(orderedItems, equipmentArr, obj);
            JObject armorProfs = inferArmorProficiencies(equipmentArr, obj);
            // Build the output object in the specified order and include Source if present
            var output = new JObject
            {
                ["name"] = obj["name"],
                ["type"] = obj["type"],
                ["system"] = obj["system"],
                ["equipment"] = equipmentArr,
                ["items"] = orderedItems,
                ["reactions"] = reactionsArr,
                ["passives"] = passivesArr,
                ["conditions"] = conditionsArr,
                ["weaponBonuses"] = weaponProfs,
                ["armorBonuses"] = armorProfs,
                ["playerOnlyStuff"] = new JArray()
            };

            // Preserve Source if it existed in the original file (some JSON uses capital "Source")
            if (obj["Source"] != null)
                output["Source"] = obj["Source"];
            else if (obj["source"] != null)
                output["Source"] = obj["source"];

            // Optionally: pretty-print the output
            return output.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        public static string ExtractFirstBracedText(string input)
        {
            if (string.IsNullOrEmpty(input))
                return null;

            int start = input.IndexOf('{');
            int end = input.IndexOf('}', start + 1);

            if (start == -1 || end == -1 || end <= start)
                return null;

            return input.Substring(start + 1, end - start - 1);
        }

        private static int GetIntValue(JToken? token, int defaultValue = 0)
        {
            if (token == null)
                return defaultValue;

            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
                return token.Value<int>();

            var text = token.ToString();
            return int.TryParse(text, out int parsed) ? parsed : defaultValue;
        }

        private static int GetAbilityModifier(JObject character, string abilityKey)
        {
            var abilityToken = character["system"]?["abilities"]?[abilityKey];
            if (abilityToken == null)
                return 0;

            if (abilityToken.Type == JTokenType.Integer || abilityToken.Type == JTokenType.Float)
                return abilityToken.Value<int>();

            if (abilityToken is JObject abilityObj)
                return GetIntValue(abilityObj["mod"], GetIntValue(abilityObj["value"]));

            return 0;
        }

        private static int GetAttributeValue(JObject character, string attributeKey)
        {
            var attributeToken = character["system"]?["attributes"]?[attributeKey];
            if (attributeToken == null)
                return 0;

            if (attributeToken.Type == JTokenType.Integer || attributeToken.Type == JTokenType.Float)
                return attributeToken.Value<int>();

            if (attributeToken is JObject attributeObj)
                return GetIntValue(attributeObj["value"], GetIntValue(attributeObj["base"], GetIntValue(attributeObj["mod"])));

            return 0;
        }

        // Rough method for inferring monster weapon proficiencies based on their action bonuses
        public static JObject InferProficiencies(JArray actions, JArray equipment, JObject character)
        {
            int unarmedBonus = 0;
            int simpleBonus = 0;
            int martialBonus = 0;
            int advancedBonus = 0;

            // Search equipment for weapons
            foreach (var equip in equipment.OfType<JObject>())
            {
                string type = equip["type"]?.ToString();
                if (type == "weapon")
                {
                    // Check actions for a name that matches the weapon
                    string weaponName = equip["name"]?.ToString() ?? "";
                    foreach (var action in actions.OfType<JObject>())
                    {
                        string actionName = action["name"]?.ToString() ?? "";
                        if (weaponName == actionName)
                        {
                            // get item attack bonus, weapon's category, and character's str/dex mods
                            int bonus = action["system"]?["bonus"]?["value"]?.Value<int>() ?? 0;
                            string category = equip["category"]?.ToString() ?? "";
                            int strMod = GetAbilityModifier(character, "str");
                            int dexMod = GetAbilityModifier(character, "dex");
                            // assume creature uses weapons better for their stats
                            // TODO retrieve weapon traits to check for finesse or ranged, etc. instead of assuming based on higher mod
                            int proficiency = bonus - Math.Max(strMod, dexMod);
                            // use higher if a value has already been found for that category
                            if (category == "unarmed")
                                unarmedBonus = Math.Max(proficiency, unarmedBonus);
                            else if (category == "simple")
                                simpleBonus = Math.Max(proficiency, simpleBonus);
                            else if (category == "martial")
                                martialBonus = Math.Max(proficiency, martialBonus);
                            else if (category == "advanced")
                                advancedBonus = Math.Max(proficiency, advancedBonus);
                        }   
                    }
                }
            }
            // assuming proficiencies scale downward...
            martialBonus = Math.Max(martialBonus, advancedBonus);
            simpleBonus = Math.Max(simpleBonus, martialBonus);
            unarmedBonus = Math.Max(unarmedBonus, simpleBonus);

            // assign values as flattened object
            var profs = new JObject
            {
                ["unarmed"] = unarmedBonus,
                ["simple"] = simpleBonus,
                ["martial"] = martialBonus,
                ["advanced"] = advancedBonus
            };
            return profs;
        }

        public static JObject inferArmorProficiencies(JArray equipment, JObject character)
        {
            int unarmoredBonus = 0;
            int lightBonus = 0;
            int mediumBonus = 0;
            int heavyBonus = 0;

            foreach (var equip in equipment.OfType<JObject>())
            {
                string type = equip["type"]?.ToString();
                if (type == "armor")
                {
                    string category = equip["category"]?.ToString() ?? "";
                    int acBonus = equip["acBonus"]?.Value<int>() ?? 0;
                    int dexCap = equip["dexCap"]?.Value<int>() ?? 0;
                    int ac = GetAttributeValue(character, "ac");
                    int dexMod = GetAbilityModifier(character, "dex");
                    int level = GetIntValue(character["system"]?["details"]?["level"]);

                    int prof = ac - 10 -acBonus - Math.Min(dexMod, dexCap);

                    if (category == "light")
                        lightBonus = Math.Max(prof, lightBonus);
                    else if (category == "medium")
                        mediumBonus = Math.Max(prof, mediumBonus);
                    else if (category == "heavy")
                        heavyBonus = Math.Max(prof, heavyBonus);
                }
            }

            // assume armor proficiencies scale downward
            mediumBonus = Math.Max(mediumBonus, heavyBonus);
            lightBonus = Math.Max(lightBonus, mediumBonus);
            unarmoredBonus = Math.Max(unarmoredBonus, lightBonus);

            var profs = new JObject
            {
                ["unarmored"] = unarmoredBonus,
                ["light"] = lightBonus,
                ["medium"] = mediumBonus,
                ["heavy"] = heavyBonus
            };
            return profs;
        }
    }
}