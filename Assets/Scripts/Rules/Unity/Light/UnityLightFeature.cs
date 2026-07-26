using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Game.Creature;
using Game.KayKit;
using Game.Rules.Runtime;
using UnityEngine;

namespace Game.Rules.Unity.Light
{
    /// <summary>Extracts Light preparation from the existing prepared-character representation.</summary>
    public sealed class UnityLightActorStateProvider : ILightActorStateProvider
    {
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;

        /// <summary>Creates the provider over encounter-stable rules-to-Unity mappings.</summary>
        /// <param name="creatures">The live encounter creature mapping.</param>
        public UnityLightActorStateProvider(
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures
        ) => this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));

        /// <inheritdoc/>
        public LightActorState Get(CreatureId actor) =>
            creatures.TryGetValue(actor, out CreatureComponent creature) && creature != null
                ? Extract(creature)
                : new LightActorState(false);

        /// <summary>Extracts the Light-specific preparation state from one Unity creature.</summary>
        /// <param name="creature">The creature whose prepared spell list is inspected.</param>
        /// <returns>The immutable Light actor state.</returns>
        public static LightActorState Extract(CreatureComponent creature)
        {
            bool prepared =
                creature != null
                && creature.Prepared?.Spellcasting?.PreparedSpells.Any(spell =>
                    string.Equals(spell.Slug, "light", StringComparison.OrdinalIgnoreCase)
                ) == true;
            return new LightActorState(prepared);
        }
    }

    /// <summary>Loads Light's exact action traits from its existing spell JSON definition.</summary>
    public static class UnityLightDefinitionLoader
    {
        private const string ResourcePath = "DataFiles/spells/cantrip/light";

        /// <summary>Creates the runtime Light definition from Unity data and actor mappings.</summary>
        /// <param name="actorStateProvider">The required Light preparation provider.</param>
        /// <returns>A Light definition with a two-action, data-backed immutable profile.</returns>
        public static LightActionDefinition Load(ILightActorStateProvider actorStateProvider)
        {
            if (actorStateProvider == null)
                throw new ArgumentNullException(nameof(actorStateProvider));

            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
                throw new InvalidOperationException(
                    $"Light spell data was not found at Resources/{ResourcePath}."
                );
            LightDefinitionData data = JsonUtility.FromJson<LightDefinitionData>(asset.text);
            if (
                data?.system?.time == null
                || !string.Equals(data.system.time.value, "2", StringComparison.Ordinal)
            )
            {
                throw new InvalidOperationException(
                    "Light spell data must declare exactly two actions."
                );
            }
            string[] traitSlugs = data.system.traits?.value;
            if (traitSlugs == null || traitSlugs.Length == 0)
                throw new InvalidOperationException("Light spell data must declare action traits.");

            return new LightActionDefinition(actorStateProvider, traitSlugs.Select(Trait.FromSlug));
        }

        [Serializable]
        private sealed class LightDefinitionData
        {
            public LightDefinitionSystemData system;
        }

        [Serializable]
        private sealed class LightDefinitionSystemData
        {
            public LightDefinitionTimeData time;
            public LightDefinitionTraitsData traits;
        }

        [Serializable]
        private sealed class LightDefinitionTimeData
        {
            public string value;
        }

        [Serializable]
        private sealed class LightDefinitionTraitsData
        {
            public string[] value;
        }
    }

    /// <summary>Installs exactly one rules-backed Light entry for prepared combatants.</summary>
    public static class UnityLightActionInstaller
    {
        /// <summary>
        /// Reconciles one controller's Light action with its lifecycle-safe prepared state.
        /// </summary>
        /// <param name="controller">The combat controller whose action bar is updated.</param>
        /// <remarks>
        /// Encounter composition can run before Unity invokes <c>Start</c>. The creature's generic,
        /// idempotent runtime-action initializer therefore runs first so its configured build and
        /// legacy actions are materialized before this feature inspects Light preparation.
        /// </remarks>
        public static void Install(ActionController controller)
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));

            CreatureComponent creature = controller.GetComponent<CreatureComponent>();
            if (creature != null)
                creature.InitializeRuntimeActions();

            bool prepared = UnityLightActorStateProvider.Extract(creature).IsPrepared;
            RulesLightAction[] existing = controller
                .GetActions()
                .OfType<RulesLightAction>()
                .ToArray();
            int retainedCount = prepared && existing.Length > 0 ? 1 : 0;
            for (int index = retainedCount; index < existing.Length; index++)
                controller.RemoveAction(existing[index]);
            if (prepared && existing.Length == 0)
                controller.AddAction(new RulesLightAction());
        }
    }

    /// <summary>Presents the rules-backed Light action through the shared Unity action bar.</summary>
    public sealed class RulesLightAction : EntityAction
    {
        /// <summary>Creates the two-action Light action-bar entry.</summary>
        public RulesLightAction()
            : base(2) { }

        /// <inheritdoc/>
        public override string ActionName => "Light";

        /// <inheritdoc/>
        public override bool IsAvailable(ActionController controller)
        {
            if (
                controller == null
                || !controller.TryGetCombatRules(
                    out UnityCombatRulesBridge bridge,
                    out CreatureId actor
                )
            )
            {
                return false;
            }

            LightActorState state = UnityLightActorStateProvider.Extract(
                controller.GetComponent<CreatureComponent>()
            );
            return LightRules.GetAvailability(bridge.Snapshot, actor, state)
                is AvailableActionAvailability;
        }

        /// <inheritdoc/>
        public override void Invoke(GameObject target)
        {
            ActionController controller =
                target == null ? null : target.GetComponent<ActionController>();
            try
            {
                if (
                    controller == null
                    || !controller.TryGetCombatRules(
                        out UnityCombatRulesBridge bridge,
                        out CreatureId actor
                    )
                )
                {
                    return;
                }

                bridge.Dispatch(new LightActionOp(actor));
            }
            finally
            {
                if (controller != null)
                    controller.IsTakingAction = false;
                OnActionComplete.Invoke();
                if (CombatManagerInterface.TryGetInstance(out CombatManagerInterface combatManager))
                    combatManager.CheckForEndOfGame();
            }
        }
    }

    /// <summary>
    /// Registers Light's post-resolution Unity presentation once for one encounter dispatcher.
    /// </summary>
    public sealed class UnityLightFeatureComposition
    {
        // Weak dispatcher keys coordinate independently created composition wrappers without
        // extending an encounter's lifetime. Registration values deliberately retain neither
        // the dispatcher nor its observer.
        private static readonly ConditionalWeakTable<
            RuleDispatcher,
            PresentationRegistration
        > PresentationRegistrations = new();

        private readonly RuleDispatcher dispatcher;
        private readonly UnityLightPresentationObserver observer;

        /// <summary>Creates a Light Unity composition over one dispatcher and creature mapping.</summary>
        /// <param name="dispatcher">The encounter dispatcher receiving the typed observer.</param>
        /// <param name="creatures">The live rules-to-Unity creature mapping.</param>
        public UnityLightFeatureComposition(
            RuleDispatcher dispatcher,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures
        )
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            observer = new UnityLightPresentationObserver(
                creatures ?? throw new ArgumentNullException(nameof(creatures))
            );
        }

        /// <summary>
        /// Registers the presentation observer idempotently across Light compositions for the dispatcher.
        /// </summary>
        public void RegisterPresentation() =>
            PresentationRegistrations
                .GetValue(dispatcher, _ => new PresentationRegistration())
                .Register(dispatcher, observer);

        private sealed class PresentationRegistration
        {
            private readonly object gate = new();
            private bool isRegistered;

            public void Register(RuleDispatcher dispatcher, UnityLightPresentationObserver observer)
            {
                lock (gate)
                {
                    if (isRegistered)
                        return;

                    // Set the flag only after registration succeeds so a failed attempt is retryable.
                    dispatcher.RegisterResolvedOpObserver<LightActionOp, LightCastOutcome>(
                        observer
                    );
                    isRegistered = true;
                }
            }
        }
    }

    /// <summary>Projects only committed Light resolution into animation, log, and gameplay state.</summary>
    public sealed class UnityLightPresentationObserver
        : IResolvedOpObserver<LightActionOp, LightCastOutcome>
    {
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;

        /// <summary>Creates the observer over encounter-stable rules-to-Unity mappings.</summary>
        /// <param name="creatures">The live encounter creature mapping.</param>
        public UnityLightPresentationObserver(
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures
        ) => this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));

        /// <inheritdoc/>
        public ValueTask OnOperationResolved(
            LightActionOp operation,
            LightCastOutcome result,
            RulesSnapshot currentSnapshot
        )
        {
            if (
                result.Actor != operation.Actor
                || !creatures.TryGetValue(operation.Actor, out CreatureComponent creature)
                || creature == null
            )
            {
                return default;
            }

            GameObject actor = creature.gameObject;
            PresentSafely(
                () =>
                {
                    if (!creature.IsDefeated)
                        actor
                            .GetComponent<CreaturePresentation>()
                            ?.PlayAttack(AnimationStyle.Magic);
                },
                actor
            );
            PresentSafely(
                () =>
                {
                    if (CombatLog.TryGetInstance(out CombatLogInterface log))
                        log.Log("- " + actor.name + " casts Light.");
                },
                actor
            );
            PresentSafely(OnGameplayStateCommitted.Invoke, actor);
            return default;
        }

        private static void PresentSafely(Action presentation, UnityEngine.Object context)
        {
            try
            {
                presentation();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, context);
            }
        }
    }
}
