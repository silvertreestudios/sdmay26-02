using System;
using System.Collections.Generic;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity.Composition;

namespace Game.Rules.Unity.Light
{
    /// <summary>Owns Light's data-selected Unity presentation for one encounter.</summary>
    internal sealed class UnityLightEncounterModule : IUnityEncounterRuntimeModule
    {
        private readonly ISpellDefinitionCatalog catalog;
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;

        internal UnityLightEncounterModule(
            ISpellDefinitionCatalog catalog,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures
        )
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));
        }

        /// <inheritdoc/>
        public void RegisterRuntime(RuleDispatcher dispatcher, CompositeLifetime lifetime)
        {
            UnityLightEffectPresentationObserver presentation =
                UnityLightEffectPresentationObserver.Create(catalog, creatures);
            lifetime.Add(presentation);
            lifetime.Add(dispatcher.RegisterFactObserver<ActiveEffectCreatedFact>(presentation));
            lifetime.Add(dispatcher.RegisterFactObserver<ActiveEffectExpiredFact>(presentation));
            lifetime.Add(dispatcher.RegisterFactObserver<ActiveEffectRemovedFact>(presentation));
            lifetime.Add(
                dispatcher.RegisterFactObserver<EncounterOutcomeCommittedFact>(presentation)
            );
        }
    }
}
