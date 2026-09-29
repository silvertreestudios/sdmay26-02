using System;
using System.Collections;
using System.Collections.Generic;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity.Composition;
using Game.Rules.Unity.Vfx;
using UnityEngine;

namespace Game.Rules.Unity.Light
{
    /// <summary>Owns Light's data-selected Unity presentation for one encounter.</summary>
    internal sealed class UnityLightModule : IUnityEncounterRuntimeModule
    {
        private readonly ISpellDefinitionCatalog catalog;
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;
        private readonly UnityVfxPlayback vfx;
        private readonly UnityActionPresentationCoordinator actionPresentation;

        internal UnityLightModule(
            ISpellDefinitionCatalog catalog,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            UnityVfxPlayback vfx,
            UnityActionPresentationCoordinator actionPresentation
        )
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));
            this.vfx = vfx ?? throw new ArgumentNullException(nameof(vfx));
            this.actionPresentation =
                actionPresentation ?? throw new ArgumentNullException(nameof(actionPresentation));
        }

        /// <inheritdoc/>
        public void RegisterRuntime(RuleDispatcher dispatcher, CompositeLifetime lifetime)
        {
            UnityLightEffectPresentationObserver presentation =
                UnityLightEffectPresentationObserver.Create(
                    catalog,
                    creatures,
                    vfx,
                    actionPresentation
                );
            lifetime.Add(presentation);
            lifetime.Add(dispatcher.RegisterFactObserver<ActiveEffectCreatedFact>(presentation));
            lifetime.Add(dispatcher.RegisterFactObserver<ActiveEffectRemovedFact>(presentation));
            lifetime.Add(
                dispatcher.RegisterFactObserver<EncounterOutcomeCommittedFact>(presentation)
            );
        }
    }

    /// <summary>
    /// Projects one data-selected spell effect into child point lights and removes them idempotently.
    /// </summary>
    public sealed class UnityLightEffectPresentationObserver
        : IFactObserver<ActiveEffectCreatedFact>,
            IFactObserver<ActiveEffectRemovedFact>,
            IFactObserver<EncounterOutcomeCommittedFact>,
            IDisposable
    {
        private readonly RuleDefinitionId presentedDefinition;
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;
        private readonly HashSet<ActiveEffectId> owned = new();
        private readonly Dictionary<ActiveEffectId, GameObject> visuals = new();
        private readonly UnityVfxPlayback vfx;
        private readonly UnityActionPresentationCoordinator actionPresentation;

        /// <summary>Creates the Light presenter from the generic data-backed spell catalog.</summary>
        /// <param name="catalog">The catalog that owns Light's rules effect definition.</param>
        /// <param name="creatures">Encounter creatures keyed by their rules identifiers.</param>
        /// <returns>An encounter-owned presenter that recognizes Light's generic effect facts.</returns>
        public static UnityLightEffectPresentationObserver Create(
            ISpellDefinitionCatalog catalog,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            UnityVfxPlayback vfx
        ) => Create(catalog, creatures, vfx, new UnityActionPresentationCoordinator());

        internal static UnityLightEffectPresentationObserver Create(
            ISpellDefinitionCatalog catalog,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            UnityVfxPlayback vfx,
            UnityActionPresentationCoordinator actionPresentation
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
                creatures,
                vfx,
                actionPresentation
            );
        }

        /// <summary>Creates an encounter-owned presenter for one data-derived effect definition.</summary>
        public UnityLightEffectPresentationObserver(
            RuleDefinitionId presentedDefinition,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            UnityVfxPlayback vfx
        )
            : this(presentedDefinition, creatures, vfx, new UnityActionPresentationCoordinator())
        { }

        internal UnityLightEffectPresentationObserver(
            RuleDefinitionId presentedDefinition,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            UnityVfxPlayback vfx,
            UnityActionPresentationCoordinator actionPresentation
        )
        {
            if (presentedDefinition.IsEmpty)
                throw new ArgumentException(
                    "A presented effect definition is required.",
                    nameof(presentedDefinition)
                );
            this.presentedDefinition = presentedDefinition;
            this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));
            this.vfx = vfx ?? throw new ArgumentNullException(nameof(vfx));
            this.actionPresentation =
                actionPresentation ?? throw new ArgumentNullException(nameof(actionPresentation));
        }

        /// <inheritdoc/>
        public void OnFactCommitted(
            ActiveEffectCreatedFact fact,
            OpId rootId,
            RulesSnapshot currentSnapshot
        )
        {
            if (
                !currentSnapshot.ActiveEffects.TryGet(
                    fact.EffectId,
                    out ActiveEffectInstance effect
                )
            )
                return;
            if (
                effect.DefinitionId != presentedDefinition
                || owned.Contains(effect.Id)
                || effect.State is not SpellEffectState state
                || !creatures.TryGetValue(state.Target, out CreatureComponent owner)
                || owner == null
            )
                return;

            owned.Add(effect.Id);
            if (!actionPresentation.TryEnqueueAfterResult(rootId, () => Present(effect.Id, owner)))
                PresentNow(effect.Id, owner);
        }

        private IEnumerator Present(ActiveEffectId effect, CreatureComponent owner)
        {
            PresentNow(effect, owner);
            yield break;
        }

        private void PresentNow(ActiveEffectId effect, CreatureComponent owner)
        {
            if (!owned.Contains(effect) || owner == null)
                return;

            GameObject visual = new("Spell Effect Light");
            try
            {
                visual.transform.SetParent(owner.transform, false);
                visual.transform.localPosition = Vector3.up;
                UnityEngine.Light light = visual.AddComponent<UnityEngine.Light>();
                light.type = LightType.Point;
                light.range = 4f;
                light.intensity = 2f;
                light.color = new Color(1f, 0.95f, 0.8f);
                light.shadows = LightShadows.Soft;
                visuals.Add(effect, visual);
                vfx.SetPersistent(
                    effect.Value + ":orb",
                    new VfxCueId("spell/light/persistent"),
                    owner.transform
                );
            }
            catch
            {
                owned.Remove(effect);
                visuals.Remove(effect);
                vfx.RemovePersistent(effect.Value + ":orb");
                Destroy(visual);
                throw;
            }
        }

        /// <inheritdoc/>
        public void OnFactCommitted(
            ActiveEffectRemovedFact fact,
            OpId rootId,
            RulesSnapshot currentSnapshot
        )
        {
            if (!owned.Remove(fact.EffectId))
                return;
            if (!actionPresentation.TryEnqueue(rootId, () => RemovePresentation(fact.EffectId)))
                Remove(fact.EffectId);
        }

        /// <inheritdoc/>
        public void OnFactCommitted(
            EncounterOutcomeCommittedFact fact,
            OpId rootId,
            RulesSnapshot currentSnapshot
        )
        {
            List<ActiveEffectId> effects = new(owned);
            foreach (ActiveEffectId effect in effects)
                Remove(effect);
        }

        /// <summary>Removes every remaining encounter-owned presentation object.</summary>
        public void Dispose()
        {
            foreach (ActiveEffectId effect in new List<ActiveEffectId>(owned))
                Remove(effect);
        }

        private void Remove(ActiveEffectId effect)
        {
            owned.Remove(effect);
            if (visuals.TryGetValue(effect, out GameObject visual))
            {
                visuals.Remove(effect);
                Destroy(visual);
            }
            vfx.RemovePersistent(effect.Value + ":orb");
        }

        private IEnumerator RemovePresentation(ActiveEffectId effect)
        {
            Remove(effect);
            yield break;
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
