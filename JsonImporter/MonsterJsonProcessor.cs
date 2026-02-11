using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
                    if (type == "weapon" || type == "armor" || type == "ammo")
                    {
                        var system = item["system"] as JObject;
                        var newEquip = new JObject
                        {
                            ["name"] = item["name"],
                            ["type"] = item["type"],
                            ["hands"] = system.SelectToken("equipped.handsHeld"),
                            // ensure quantity is an integer and provide a sensible default (1)
                            ["quantity"] = system?["quantity"]?.Value<int?>() ?? 1
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

                        // TODO 
                        // Refine implementation to avoid double listing in both items and conditions.
                        /*
                        string context = cloned?["descriptionContext"]?.ToString() ?? "";
                        if (context.Contains("Compendium.pf2e.conditionitems.Item."))
                        {
                            string descPar = cloned?["descriptionParagraphs"]?.ToString() ?? "";
                            string conditionName = ExtractFirstBracedText(descPar);
                            if (conditionName != null)
                            {
                                newItem["name"] = conditionName;
                            }
                            conditionsArr.Add(newItem);
                        }
                        */
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

            // Reorder items so melee/ranged (weapons) come first — convenient when consumer
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

            // Reformat skills into an array (name + base) when skills is an object
            var systemObj = obj["system"] as JObject;
            if (systemObj != null && systemObj["skills"] is JObject skillsObj)
            {
                var skillsArray = new JArray();
                foreach (var prop in skillsObj.Properties())
                {
                    var valObj = prop.Value as JObject;
                    int baseVal = 0;
                    if (valObj != null)
                    {
                        baseVal = valObj["base"]?.Value<int?>() ?? valObj["mod"]?.Value<int?>() ?? valObj["value"]?.Value<int?>() ?? 0;
                    }
                    var skillEntry = new JObject
                    {
                        ["name"] = prop.Name,
                        ["base"] = baseVal
                    };
                    skillsArray.Add(skillEntry);
                }
                systemObj["skills"] = skillsArray;
            }

            // extract and convert publicNotes HTML -> plain + paragraphs
            var detailsObj = obj["system"]?["details"] as JObject;
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
                var publicNotesProp = detailsObj.Property("publicNotes");
                if (publicNotesProp != null)
                {
                    publicNotesProp.AddAfterSelf(new JProperty("publicNotesParagraphs", paragraphs));
                    publicNotesProp.AddAfterSelf(new JProperty("publicNotesPlain", plain));
                }
                else
                {
                    detailsObj["publicNotesPlain"] = plain;
                    detailsObj["publicNotesParagraphs"] = paragraphs;
                }
            }

            // Ensure weaknesses/resistances exist as arrays (empty if not present)
            if (systemObj != null)
            {
                if (systemObj["weaknesses"] == null) systemObj["weaknesses"] = new JArray();
                if (systemObj["resistances"] == null) systemObj["resistances"] = new JArray();
            }

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
                ["conditions"] = conditionsArr
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
    }
}