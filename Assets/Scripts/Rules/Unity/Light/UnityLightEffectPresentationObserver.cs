using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Creature;
using Game.Rules.Runtime;
using UnityEngine;

namespace Game.Rules.Unity.Light
{
    /// <summary>
    /// Projects one data-selected spell effect into child point lights and removes them idempotently.
    /// </summary>
    public sealed class UnityLightEffectPresentationObserver
        : IFactObserver<ActiveEffectCreatedFact>,
            IFactObserver<ActiveEffectExpiredFact>,
            IFactObserver<ActiveEffectRemovedFact>,
            IFactObserver<EncounterEndedFact>,
            IDisposable
    {
        private readonly RuleDefinitionId presentedDefinition;
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;
        private readonly Dictionary<ActiveEffectId, GameObject> visuals = new();

        /// <summary>Creates the Light presenter from the generic data-backed spell catalog.</summary>
        /// <param name="catalog">The catalog that owns Light's rules effect definition.</param>
        /// <param name="creatures">Encounter creatures keyed by their rules identifiers.</param>
        /// <returns>An encounter-owned presenter that recognizes Light's generic effect facts.</returns>
        public static UnityLightEffectPresentationObserver Create(
            ISpellDefinitionCatalog catalog,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures
        )
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            SpellReference light = new(new SpellId("light"), 1);
            if (
                !catalog.TryGetSpell(light, out SpellDefinition definition)
                || definition.Effects.Count != 1
            )
                throw new InvalidOperationException(
                    "Light requires exactly one active-effect directive for presentation."
                );
            return new UnityLightEffectPresentationObserver(
                definition.Effects[0].DefinitionId,
                creatures
            );
        }

        /// <summary>Creates an encounter-owned presenter for one data-derived effect definition.</summary>
        public UnityLightEffectPresentationObserver(
            RuleDefinitionId presentedDefinition,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures
        )
        {
            if (presentedDefinition.IsEmpty)
                throw new ArgumentException(
                    "A presented effect definition is required.",
                    nameof(presentedDefinition)
                );
            this.presentedDefinition = presentedDefinition;
            this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));
        }

        /// <inheritdoc/>
        public ValueTask OnFactCommitted(
            ActiveEffectCreatedFact fact,
            RulesSnapshot currentSnapshot
        )
        {
            if (
                !currentSnapshot.ActiveEffects.TryGet(
                    fact.EffectId,
                    out ActiveEffectInstance effect
                )
            )
                return default;
            if (
                effect.DefinitionId != presentedDefinition
                || visuals.ContainsKey(effect.Id)
                || effect.State is not SpellEffectState state
                || !creatures.TryGetValue(state.Target, out CreatureComponent owner)
                || owner == null
            )
                return default;

            GameObject visual = null;
            try
            {
                visual = new GameObject("Spell Effect Light");
                visual.transform.SetParent(owner.transform, false);
                visual.transform.localPosition = Vector3.up;
                UnityEngine.Light light = visual.AddComponent<UnityEngine.Light>();
                light.type = LightType.Point;
                light.range = 4f;
                light.intensity = 2f;
                light.color = new Color(1f, 0.95f, 0.8f);
                light.shadows = LightShadows.Soft;
                visuals.Add(effect.Id, visual);
            }
            catch (Exception exception)
            {
                Destroy(visual);
                Debug.LogException(exception);
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask OnFactCommitted(
            ActiveEffectExpiredFact fact,
            RulesSnapshot currentSnapshot
        )
        {
            Remove(fact.EffectId);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask OnFactCommitted(
            ActiveEffectRemovedFact fact,
            RulesSnapshot currentSnapshot
        )
        {
            Remove(fact.EffectId);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask OnFactCommitted(EncounterEndedFact fact, RulesSnapshot currentSnapshot)
        {
            List<ActiveEffectId> owned = new();
            foreach (KeyValuePair<ActiveEffectId, GameObject> pair in visuals)
            {
                if (pair.Value == null)
                    owned.Add(pair.Key);
                else if (
                    creatures.TryGetValue(fact.Creature, out CreatureComponent owner)
                    && owner != null
                    && pair.Value.transform.IsChildOf(owner.transform)
                )
                    owned.Add(pair.Key);
            }
            foreach (ActiveEffectId effect in owned)
                Remove(effect);
            return default;
        }

        /// <summary>Removes every remaining encounter-owned presentation object.</summary>
        public void Dispose()
        {
            foreach (GameObject visual in visuals.Values)
                Destroy(visual);
            visuals.Clear();
        }

        private void Remove(ActiveEffectId effect)
        {
            if (!visuals.TryGetValue(effect, out GameObject visual))
                return;
            visuals.Remove(effect);
            Destroy(visual);
        }

        private static void Destroy(GameObject value)
        {
            if (value == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(value);
            else
                UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
