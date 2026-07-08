using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Creature.Rules
{
    /// <summary>
    /// Captures durable player build choices that select PF2e data items; runtime rule state is prepared separately from these saved choices.
    /// </summary>
    public sealed class CharacterBuild
    {
        public string ClassName;
        public string SubclassName;
        public string ClassFeatName;
        public readonly Dictionary<string, string> RuleSelections = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> TrainedSkills = new();

        /// <summary>
        /// Reads the current creature JSON shape into build choices without requiring Foundry-derived data to be rewritten.
        /// </summary>
        /// <param name="json">Serialized creature JSON from the existing character data pipeline.</param>
        /// <returns>A build with any recognizable class, subclass, and feat choices populated.</returns>
        public static CharacterBuild FromCreatureJson(string json)
        {
            CharacterBuild build = new();
            if (string.IsNullOrWhiteSpace(json))
                return build;

            try
            {
                JObject root = JObject.Parse(json);
                JObject firstPlayerBlock = root["playerOnlyStuff"]?.First as JObject;
                if (firstPlayerBlock != null)
                {
                    build.ClassName = firstPlayerBlock.Value<string>("className");
                    build.SubclassName = firstPlayerBlock.Value<string>("subclass");
                    build.ClassFeatName = firstPlayerBlock.Value<string>("classFeat");

                    if (firstPlayerBlock["ruleSelections"] is JObject selections)
                    {
                        foreach (JProperty selection in selections.Properties())
                        {
                            string value = selection.Value?.Value<string>();
                            if (!string.IsNullOrWhiteSpace(value))
                                build.RuleSelections[selection.Name] = value;
                        }
                    }

                    if (firstPlayerBlock["trainedSkills"] is JArray trainedSkills)
                    {
                        foreach (JToken skill in trainedSkills)
                        {
                            string value = skill.Value<string>();
                            if (!string.IsNullOrWhiteSpace(value))
                                build.TrainedSkills.Add(value);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"PF2e build data could not be parsed: {ex.Message}");
            }

            return build;
        }
    }
}
