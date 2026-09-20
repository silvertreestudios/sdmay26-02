using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.DungeonPersistence.Repository;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;
using UnityEngine;

namespace Game.DungeonPersistence.Actors
{
    /// <summary>Identifies whether a persisted rules actor belongs to the run or one floor.</summary>
    [Serializable]
    internal enum DungeonRulesActorScope
    {
        Party,
        Floor,
    }

    /// <summary>
    /// Preserves an actor identity across dungeon floors without conflating reused floor-local IDs.
    /// </summary>
    [Serializable]
    internal struct DungeonRulesActorReference : IEquatable<DungeonRulesActorReference>
    {
        public DungeonRulesActorScope Scope;
        public int FloorDepth;
        public string ActorId;

        internal static DungeonRulesActorReference Party(string actorId) =>
            Create(DungeonRulesActorScope.Party, -1, actorId);

        internal static DungeonRulesActorReference Floor(int floorDepth, string actorId) =>
            Create(DungeonRulesActorScope.Floor, floorDepth, actorId);

        internal bool IsValid =>
            !string.IsNullOrWhiteSpace(ActorId)
            && (
                (Scope == DungeonRulesActorScope.Party && FloorDepth == -1)
                || (Scope == DungeonRulesActorScope.Floor && FloorDepth >= 0)
            );

        internal string StableKey =>
            Scope == DungeonRulesActorScope.Party
                ? $"party:{ActorId.Length}:{ActorId}"
                : $"floor:{FloorDepth.ToString(CultureInfo.InvariantCulture)}:{ActorId.Length}:{ActorId}";

        public bool Equals(DungeonRulesActorReference other) =>
            Scope == other.Scope
            && FloorDepth == other.FloorDepth
            && string.Equals(ActorId, other.ActorId, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is DungeonRulesActorReference other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                (int)Scope,
                FloorDepth,
                ActorId == null ? 0 : StringComparer.Ordinal.GetHashCode(ActorId)
            );

        public override string ToString() => StableKey;

        private static DungeonRulesActorReference Create(
            DungeonRulesActorScope scope,
            int floorDepth,
            string actorId
        )
        {
            DungeonRulesActorReference reference = new()
            {
                Scope = scope,
                FloorDepth = floorDepth,
                ActorId = actorId,
            };
            if (!reference.IsValid)
                throw new ArgumentException("A persisted rules actor reference is invalid.");
            return reference;
        }
    }

    /// <summary>
    /// Converts one concrete immutable effect-state type to and from its stable dungeon-save payload.
    /// </summary>
    /// <remarks>
    /// Features that introduce a new <see cref="IEffectState"/> type register one codec in the
    /// explicit persistence catalog. The generic effect envelope preserves effect and binding
    /// identity, provenance, duration, and timing without knowing the feature's name or type.
    /// </remarks>
    internal interface IDungeonEffectStateCodec
    {
        string Kind { get; }
        Type StateType { get; }
        IReadOnlyList<RuleDefinitionId> Definitions { get; }
        string Capture(
            IEffectState state,
            Func<CreatureId, DungeonRulesActorReference> identifyCreature
        );
        IEffectState Restore(
            string payload,
            Func<DungeonRulesActorReference, CreatureId> resolveCreature
        );
        IReadOnlyList<DungeonRulesActorReference> GetReferencedActors(string payload);
    }

    /// <summary>Stores the explicit codecs used by the current encounter composition.</summary>
    internal sealed class DungeonEffectStateCodecCatalog
    {
        private readonly IReadOnlyDictionary<string, IDungeonEffectStateCodec> byKind;
        private readonly IReadOnlyDictionary<Type, IDungeonEffectStateCodec> byType;
        private readonly IReadOnlyDictionary<
            RuleDefinitionId,
            IDungeonEffectStateCodec
        > byDefinition;

        internal DungeonEffectStateCodecCatalog(IEnumerable<IDungeonEffectStateCodec> codecs)
        {
            IDungeonEffectStateCodec[] copied =
                codecs?.ToArray() ?? throw new ArgumentNullException(nameof(codecs));
            if (
                copied.Any(codec =>
                    codec == null
                    || string.IsNullOrWhiteSpace(codec.Kind)
                    || codec.StateType == null
                    || codec.Definitions == null
                    || codec.Definitions.Count == 0
                    || codec.Definitions.Any(definition => definition.IsEmpty)
                    || codec.Definitions.Distinct().Count() != codec.Definitions.Count
                )
                || copied.Select(codec => codec.Kind).Distinct(StringComparer.Ordinal).Count()
                    != copied.Length
                || copied.Select(codec => codec.StateType).Distinct().Count() != copied.Length
            )
                throw new ArgumentException(
                    "Effect persistence codecs require unique stable kinds and state types.",
                    nameof(codecs)
                );
            byKind = copied.ToDictionary(codec => codec.Kind, StringComparer.Ordinal);
            byType = copied.ToDictionary(codec => codec.StateType);
            RuleDefinitionId[] definitions = copied
                .SelectMany(codec => codec.Definitions)
                .ToArray();
            if (definitions.Distinct().Count() != definitions.Length)
                throw new ArgumentException(
                    "An effect definition can use only one persistence codec.",
                    nameof(codecs)
                );
            byDefinition = copied
                .SelectMany(codec => codec.Definitions.Select(definition => (definition, codec)))
                .ToDictionary(pair => pair.definition, pair => pair.codec);
        }

        internal static DungeonEffectStateCodecCatalog CreateProduction()
        {
            RuleDefinitionId[] spellDefinitions = UnitySpellDefinitionCatalog
                .Load()
                .Definitions.SelectMany(definition => definition.Effects)
                .Select(effect => effect.DefinitionId)
                .Distinct()
                .ToArray();
            return new DungeonEffectStateCodecCatalog(
                new IDungeonEffectStateCodec[]
                {
                    new ConditionEffectStateCodec(),
                    new SpellEffectStateCodec(spellDefinitions),
                    new RageEffectStateCodec(),
                }
            );
        }

        internal (string Kind, string Payload) Capture(
            RuleDefinitionId definition,
            IEffectState state,
            Func<CreatureId, DungeonRulesActorReference> identifyCreature
        )
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (!byType.TryGetValue(state.GetType(), out IDungeonEffectStateCodec codec))
                throw new InvalidOperationException(
                    $"Effect state type '{state.GetType().Name}' has no dungeon persistence codec."
                );
            Require(definition, codec.Kind);
            return (codec.Kind, codec.Capture(state, identifyCreature));
        }

        internal IEffectState Restore(
            RuleDefinitionId definition,
            string kind,
            string payload,
            Func<DungeonRulesActorReference, CreatureId> resolveCreature
        ) => Require(definition, kind).Restore(payload, resolveCreature);

        internal IReadOnlyList<DungeonRulesActorReference> GetReferencedActors(
            RuleDefinitionId definition,
            string kind,
            string payload
        ) => Require(definition, kind).GetReferencedActors(payload);

        private IDungeonEffectStateCodec Require(RuleDefinitionId definition, string kind)
        {
            if (string.IsNullOrWhiteSpace(kind) || !byKind.TryGetValue(kind, out var codec))
                throw new ArgumentException($"Unknown effect-state persistence kind '{kind}'.");
            if (
                definition.IsEmpty
                || !byDefinition.TryGetValue(definition, out IDungeonEffectStateCodec expected)
            )
                throw new InvalidOperationException(
                    $"Rule definition '{definition.Value}' has no effect-state persistence codec."
                );
            if (!ReferenceEquals(codec, expected))
                throw new InvalidOperationException(
                    $"Rule definition '{definition.Value}' requires effect-state kind "
                        + $"'{expected.Kind}', not '{kind}'."
                );
            return codec;
        }
    }

    /// <summary>
    /// Carries one immutable rules effect across the detached exploration boundary.
    /// </summary>
    internal sealed class DungeonRulesEffectProjection
    {
        internal DungeonRulesEffectProjection(
            ActiveEffectInstance effect,
            ActiveRuleBinding binding,
            ActiveEffectTimingState timing
        )
        {
            Effect = effect ?? throw new ArgumentNullException(nameof(effect));
            Binding = binding ?? throw new ArgumentNullException(nameof(binding));
            Timing = timing;
        }

        internal ActiveEffectInstance Effect { get; }
        internal ActiveRuleBinding Binding { get; }
        internal ActiveEffectTimingState Timing { get; }
    }

    /// <summary>
    /// Carries validated save envelopes or a detached immutable projection until combatant
    /// enrollment commits them to the new encounter's authoritative store.
    /// </summary>
    internal sealed class DungeonRulesEffectSeed : MonoBehaviour
    {
        private DungeonRulesEffectSaveState[] effects = Array.Empty<DungeonRulesEffectSaveState>();
        private DungeonRulesEffectProjection[] projections =
            Array.Empty<DungeonRulesEffectProjection>();
        private Func<DungeonRulesActorReference, GameObject> resolveActor = _ =>
            throw new InvalidOperationException("The rules-effect seed is not initialized.");
        private IReadOnlyDictionary<CreatureId, GameObject> projectedActors =
            new Dictionary<CreatureId, GameObject>();
        private readonly Dictionary<
            CreatureId,
            DungeonRulesActorReference
        > projectedActorReferences = new();

        internal IReadOnlyList<DungeonRulesEffectSaveState> Effects => effects;
        internal IReadOnlyList<DungeonRulesEffectProjection> Projections => projections;

        internal void Initialize(
            IEnumerable<DungeonRulesEffectSaveState> restoredEffects,
            Func<DungeonRulesActorReference, GameObject> actorResolver
        )
        {
            DungeonRulesEffectSaveState[] copied =
                restoredEffects?.ToArray()
                ?? throw new ArgumentNullException(nameof(restoredEffects));
            Func<DungeonRulesActorReference, GameObject> copiedResolver =
                actorResolver ?? throw new ArgumentNullException(nameof(actorResolver));
            effects = copied;
            projections = Array.Empty<DungeonRulesEffectProjection>();
            resolveActor = copiedResolver;
            projectedActors = new Dictionary<CreatureId, GameObject>();
            projectedActorReferences.Clear();
            GetComponent<ConditionSeed>()?.Clear();
        }

        internal CreatureId ResolveCreature(
            DungeonRulesActorReference actor,
            UnityCombatRulesBridge owner,
            Func<DungeonRulesActorReference, CreatureId> resolveExternal
        )
        {
            GameObject actorObject = resolveActor(actor);
            if (
                actorObject != null
                && actorObject.TryGetComponent(out CreatureComponent creature)
                && owner.TryGetCreatureId(creature, out CreatureId id)
            )
                return id;
            return resolveExternal(actor);
        }

        internal CreatureId ResolveProjectedCreature(
            CreatureId projectedCreature,
            UnityCombatRulesBridge owner,
            Func<DungeonRulesActorReference, CreatureId> resolveExternal
        )
        {
            if (
                projectedActors.TryGetValue(projectedCreature, out GameObject actorObject)
                && actorObject != null
                && actorObject.TryGetComponent(out CreatureComponent creature)
                && owner.TryGetCreatureId(creature, out CreatureId id)
            )
                return id;
            if (
                projectedActorReferences.TryGetValue(
                    projectedCreature,
                    out DungeonRulesActorReference actor
                )
            )
                return resolveExternal(actor);
            throw new InvalidOperationException(
                $"Detached rules-effect creature '{projectedCreature.Value}' is unavailable."
            );
        }

        internal IReadOnlyList<DungeonRulesEffectSaveState> Capture(
            Func<GameObject, DungeonRulesActorReference> identifyActor
        )
        {
            if (identifyActor == null)
                throw new ArgumentNullException(nameof(identifyActor));
            if (projections.Length == 0)
                return effects;

            DungeonRulesActorReference IdentifyCreature(CreatureId creature)
            {
                if (
                    projectedActorReferences.TryGetValue(
                        creature,
                        out DungeonRulesActorReference actorReference
                    )
                )
                    return actorReference;
                if (projectedActors.TryGetValue(creature, out GameObject actor) && actor != null)
                {
                    DungeonRulesActorReference identified = identifyActor(actor);
                    RememberActorReference(creature, identified);
                    return identified;
                }
                throw new InvalidOperationException(
                    $"Detached rules effect references unavailable creature '{creature.Value}'."
                );
            }

            return projections
                .Select(projection =>
                    DungeonRulesEffectPersistence.Capture(projection, IdentifyCreature)
                )
                .ToArray();
        }

        internal void Project(
            IEnumerable<DungeonRulesEffectProjection> detachedEffects,
            IReadOnlyDictionary<CreatureId, GameObject> actors,
            IReadOnlyDictionary<CreatureId, DungeonRulesActorReference> actorReferences
        )
        {
            projections =
                detachedEffects?.ToArray()
                ?? throw new ArgumentNullException(nameof(detachedEffects));
            projectedActors = actors ?? throw new ArgumentNullException(nameof(actors));
            if (actorReferences == null)
                throw new ArgumentNullException(nameof(actorReferences));
            foreach (KeyValuePair<CreatureId, DungeonRulesActorReference> actor in actorReferences)
                RememberActorReference(actor.Key, actor.Value);
            effects = Array.Empty<DungeonRulesEffectSaveState>();
            GetComponent<ConditionSeed>()?.Clear();
        }

        internal void ConsumeRestoredEffects(
            IReadOnlyDictionary<CreatureId, DungeonRulesActorReference> actorReferences
        )
        {
            if (actorReferences == null)
                throw new ArgumentNullException(nameof(actorReferences));
            effects = Array.Empty<DungeonRulesEffectSaveState>();
            projections = Array.Empty<DungeonRulesEffectProjection>();
            resolveActor = _ =>
                throw new InvalidOperationException("The rules-effect seed was consumed.");
            projectedActors = new Dictionary<CreatureId, GameObject>();
            // Creature IDs are encounter-local. Keep only identities resolved into the committed
            // encounter so a reused ID cannot inherit provenance from the detached encounter.
            projectedActorReferences.Clear();
            foreach (KeyValuePair<CreatureId, DungeonRulesActorReference> actor in actorReferences)
                RememberActorReference(actor.Key, actor.Value);
        }

        internal void RememberActorReference(CreatureId creature, DungeonRulesActorReference actor)
        {
            if (!actor.IsValid)
                throw new ArgumentException("A stable rules actor reference is required.");
            if (
                projectedActorReferences.TryGetValue(creature, out var existing)
                && !existing.Equals(actor)
            )
                throw new InvalidOperationException(
                    $"Rules creature '{creature.Value}' cannot represent both '{existing}' and '{actor}'."
                );
            projectedActorReferences[creature] = actor;
        }

        internal bool TryGetActorReference(
            CreatureId creature,
            out DungeonRulesActorReference actor
        ) => projectedActorReferences.TryGetValue(creature, out actor);
    }

    /// <summary>Enrolls saved generic effect envelopes through the common combatant addition.</summary>
    internal sealed class DungeonRulesEffectPersistenceModule
        : IUnityEncounterRuntimeModule,
            IUnityCombatantEnrollmentModule
    {
        private readonly UnityCombatRulesBridge owner;
        private readonly DungeonEffectStateCodecCatalog codecs;
        private readonly Dictionary<DungeonRulesActorReference, CreatureId> externalCreatures =
            new();
        private readonly Dictionary<CreatureId, DungeonRulesActorReference> externalActors = new();
        private readonly Dictionary<CreatureId, DungeonRulesActorReference> actorReferences = new();

        internal DungeonRulesEffectPersistenceModule(
            UnityCombatRulesBridge owner,
            DungeonEffectStateCodecCatalog codecs
        )
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.codecs = codecs ?? throw new ArgumentNullException(nameof(codecs));
        }

        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder)
        {
            DungeonRulesEffectSeed seed = builder.Controller.GetComponent<DungeonRulesEffectSeed>();
            if (seed == null)
                return;

            HashSet<CreatureId> references = new();
            CreatureId Resolve(DungeonRulesActorReference actor)
            {
                CreatureId id = seed.ResolveCreature(actor, owner, ResolveExternal);
                RememberActorReference(id, actor);
                if (externalActors.ContainsKey(id))
                    references.Add(id);
                return id;
            }

            CreatureId ResolveProjected(CreatureId projectedCreature)
            {
                bool hasActorReference = seed.TryGetActorReference(
                    projectedCreature,
                    out DungeonRulesActorReference actor
                );
                CreatureId id = seed.ResolveProjectedCreature(
                    projectedCreature,
                    owner,
                    ResolveExternal
                );
                if (hasActorReference)
                    RememberActorReference(id, actor);
                if (externalActors.ContainsKey(id))
                    references.Add(id);
                return id;
            }

            void Add(
                ActiveEffectInstance effect,
                ActiveRuleBinding binding,
                ActiveEffectTimingRestore? timing
            )
            {
                if (binding.Owner != builder.CreatureId)
                    throw new InvalidOperationException(
                        $"Saved effect '{effect.Id.Value}' is enrolled through the wrong binding owner."
                    );
                builder.AddActiveEffects(new[] { effect });
                builder.AddRuleBindings(new[] { binding });
                if (timing.HasValue)
                    builder.AddActiveEffectTimings(new[] { timing.Value });
            }

            foreach (DungeonRulesEffectSaveState saved in seed.Effects)
            {
                CreatureId bindingOwner = Resolve(saved.BindingOwnerActor);
                CreatureId source = Resolve(saved.SourceActor);
                ActiveEffectId effectId = new(saved.EffectId);
                RuleDefinitionId definitionId = new(saved.DefinitionId);
                RuleSource ruleSource = RuleSource.FromSlug(saved.RuleSource);
                ActiveEffectInstance effect = new(
                    effectId,
                    definitionId,
                    source,
                    ruleSource,
                    DungeonRulesEffectPersistence.RestoreDuration(
                        saved.DurationKind,
                        saved.DurationAmount
                    ),
                    codecs.Restore(definitionId, saved.StateKind, saved.StatePayload, Resolve),
                    new EffectStateVersion(saved.EffectStateVersion)
                );
                ActiveRuleBinding binding = new(
                    new BindingId(saved.BindingId),
                    definitionId,
                    bindingOwner,
                    effectId,
                    ruleSource,
                    saved.CreationOrder,
                    saved.BindingEnabled
                );
                Add(
                    effect,
                    binding,
                    saved.HasTiming
                        ? new ActiveEffectTimingRestore(
                            effectId,
                            saved.RemainingBoundaries,
                            saved.ExpiresWithEncounter
                        )
                        : null
                );
            }
            foreach (DungeonRulesEffectProjection projection in seed.Projections)
            {
                ActiveEffectInstance projectedEffect = projection.Effect;
                ActiveRuleBinding projectedBinding = projection.Binding;
                (string kind, string payload) = codecs.Capture(
                    projectedEffect.DefinitionId,
                    projectedEffect.State,
                    creature => DungeonRulesActorReference.Party(creature.Value)
                );
                ActiveEffectInstance effect = new(
                    projectedEffect.Id,
                    projectedEffect.DefinitionId,
                    ResolveProjected(projectedEffect.SourceCreature),
                    projectedEffect.Source,
                    projectedEffect.Duration,
                    codecs.Restore(
                        projectedEffect.DefinitionId,
                        kind,
                        payload,
                        actor => ResolveProjected(new CreatureId(actor.ActorId))
                    ),
                    projectedEffect.EffectStateVersion
                );
                ActiveRuleBinding binding = new(
                    projectedBinding.Id,
                    projectedBinding.DefinitionId,
                    ResolveProjected(projectedBinding.Owner),
                    projectedBinding.EffectId,
                    projectedBinding.Source,
                    projectedBinding.CreationOrder,
                    projectedBinding.IsEnabled
                );
                ActiveEffectTimingRestore? timing =
                    projection.Timing == null
                        ? null
                        : new ActiveEffectTimingRestore(
                            projectedEffect.Id,
                            projection.Timing.RemainingBoundaries,
                            projection.Timing.ExpiresWithEncounter
                        );
                Add(effect, binding, timing);
            }
            builder.AddExternalEffectReferences(references);
            builder.AddInstallation(new ConsumeSeedInstallation(seed, actorReferences));
        }

        /// <inheritdoc/>
        public void RegisterRuntime(RuleDispatcher dispatcher, CompositeLifetime lifetime)
        {
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));
            if (lifetime == null)
                throw new ArgumentNullException(nameof(lifetime));
            lifetime.Add(new RegistrationToken(ProjectDetachedEffects));
        }

        private CreatureId ResolveExternal(DungeonRulesActorReference actor)
        {
            if (!actor.IsValid)
                throw new ArgumentException("A saved effect actor identity is required.");
            if (externalCreatures.TryGetValue(actor, out CreatureId existing))
                return existing;
            CreatureId created = new($"dungeon-external:{actor.StableKey}");
            if (
                externalActors.TryGetValue(created, out DungeonRulesActorReference collision)
                && !collision.Equals(actor)
            )
                throw new InvalidOperationException(
                    $"Saved actor identities '{collision}' and '{actor}' collide."
                );
            externalCreatures.Add(actor, created);
            externalActors[created] = actor;
            RememberActorReference(created, actor);
            return created;
        }

        private void RememberActorReference(CreatureId creature, DungeonRulesActorReference actor)
        {
            if (
                actorReferences.TryGetValue(creature, out DungeonRulesActorReference existing)
                && !existing.Equals(actor)
            )
                throw new InvalidOperationException(
                    $"Rules creature '{creature.Value}' cannot represent both '{existing}' and '{actor}'."
                );
            actorReferences[creature] = actor;
        }

        private void ProjectDetachedEffects()
        {
            RulesSnapshot snapshot = owner.Snapshot;
            Dictionary<CreatureId, GameObject> actors = new();
            foreach (KeyValuePair<CreatureId, CreatureState> entry in snapshot.Creatures)
            {
                ActionController controller = owner.GetController(entry.Key);
                if (controller != null)
                    actors.Add(entry.Key, controller.gameObject);
            }
            foreach (KeyValuePair<CreatureId, GameObject> actor in actors)
            {
                ActionController controller = actor.Value.GetComponent<ActionController>();
                DungeonRulesEffectSeed seed =
                    controller.GetComponent<DungeonRulesEffectSeed>()
                    ?? controller.gameObject.AddComponent<DungeonRulesEffectSeed>();
                seed.Project(
                    DungeonRulesEffectPersistence.Project(snapshot, actor.Key),
                    actors,
                    actorReferences
                );
            }
        }

        private sealed class ConsumeSeedInstallation : IUnityCombatantInstallationContribution
        {
            private readonly DungeonRulesEffectSeed seed;
            private readonly IReadOnlyDictionary<
                CreatureId,
                DungeonRulesActorReference
            > actorReferences;

            internal ConsumeSeedInstallation(
                DungeonRulesEffectSeed seed,
                IReadOnlyDictionary<CreatureId, DungeonRulesActorReference> actorReferences
            )
            {
                this.seed = seed ?? throw new ArgumentNullException(nameof(seed));
                this.actorReferences =
                    actorReferences ?? throw new ArgumentNullException(nameof(actorReferences));
            }

            public void Apply() => seed.ConsumeRestoredEffects(actorReferences);
        }
    }

    /// <summary>Owns generic capture and validation for rules-backed dungeon effects.</summary>
    internal static class DungeonRulesEffectPersistence
    {
        internal static DungeonEffectStateCodecCatalog Codecs { get; } =
            DungeonEffectStateCodecCatalog.CreateProduction();

        internal static IReadOnlyList<DungeonRulesEffectSaveState> Capture(
            ActionController controller,
            Func<GameObject, DungeonRulesActorReference> identifyActor
        )
        {
            if (
                !controller.TryGetCombatRules(
                    out UnityCombatRulesBridge bridge,
                    out CreatureId target
                )
            )
            {
                DungeonRulesEffectSeed seed = controller.GetComponent<DungeonRulesEffectSeed>();
                IReadOnlyList<DungeonRulesEffectSaveState> projected =
                    seed == null
                        ? Array.Empty<DungeonRulesEffectSaveState>()
                        : seed.Capture(identifyActor);
                return CaptureDetachedConditions(controller, identifyActor, projected);
            }

            DungeonRulesEffectSeed attachedSeed = controller.GetComponent<DungeonRulesEffectSeed>();
            DungeonRulesActorReference IdentifyCreature(CreatureId creature)
            {
                if (
                    attachedSeed != null
                    && attachedSeed.TryGetActorReference(
                        creature,
                        out DungeonRulesActorReference remembered
                    )
                )
                    return remembered;

                DungeonRulesActorReference identified = identifyActor(
                    bridge.GetController(creature).gameObject
                );
                // Capture is the stable-identity boundary for effects created in this encounter.
                // Retain it now so later detachment survives source omission or destruction.
                attachedSeed ??= controller.gameObject.AddComponent<DungeonRulesEffectSeed>();
                attachedSeed.RememberActorReference(creature, identified);
                return identified;
            }
            return Project(bridge.Snapshot, target)
                .Select(projection => Capture(projection, IdentifyCreature))
                .ToArray();
        }

        private static IReadOnlyList<DungeonRulesEffectSaveState> CaptureDetachedConditions(
            ActionController controller,
            Func<GameObject, DungeonRulesActorReference> identifyActor,
            IReadOnlyList<DungeonRulesEffectSaveState> projected
        )
        {
            ConditionSeed conditionSeed = controller.GetComponent<ConditionSeed>();
            if (conditionSeed == null || conditionSeed.Applications.Count == 0)
                return projected;

            DungeonRulesActorReference actor = identifyActor(controller.gameObject);
            if (!actor.IsValid)
                throw new InvalidOperationException(
                    $"Detached condition owner '{controller.name}' has no stable actor identity."
                );

            List<DungeonRulesEffectSaveState> captured = new(projected);
            HashSet<string> effectIds = new(
                projected.Select(effect => effect.EffectId),
                StringComparer.Ordinal
            );
            HashSet<string> bindingIds = new(
                projected.Select(effect => effect.BindingId),
                StringComparer.Ordinal
            );
            long creationOrder = projected
                .Select(effect => effect.CreationOrder)
                .DefaultIfEmpty(-1)
                .Max();
            int identityOrdinal = 0;
            foreach (var application in conditionSeed.Applications)
            {
                if (creationOrder == long.MaxValue)
                    throw new InvalidOperationException(
                        "Detached condition creation order exhausts operation identity."
                    );
                creationOrder++;

                string effectIdentity;
                string bindingIdentity;
                do
                {
                    string applicationIdentity = $"{actor.StableKey}:{identityOrdinal++}";
                    // Detached applications do not have a live dispatcher. Their persisted actor
                    // identity provides the run-global namespace instead.
                    effectIdentity = $"active-effect:detached-condition:{applicationIdentity}";
                    bindingIdentity = $"effect-binding:detached-condition:{applicationIdentity}";
                } while (
                    effectIds.Contains(effectIdentity) || bindingIds.Contains(bindingIdentity)
                );
                effectIds.Add(effectIdentity);
                bindingIds.Add(bindingIdentity);

                (string kind, string payload) = Codecs.Capture(
                    ConditionRules.DefinitionId,
                    application.State,
                    _ =>
                        throw new InvalidOperationException(
                            "A condition payload cannot contain an actor reference."
                        )
                );
                captured.Add(
                    new DungeonRulesEffectSaveState
                    {
                        EffectId = effectIdentity,
                        BindingId = bindingIdentity,
                        DefinitionId = ConditionRules.DefinitionId.Value,
                        SourceActor = actor,
                        BindingOwnerActor = actor,
                        RuleSource = application.Source.Slug,
                        DurationKind = EffectDurationKind.Indefinite,
                        DurationAmount = 0,
                        EffectStateVersion = 0,
                        CreationOrder = creationOrder,
                        BindingEnabled = true,
                        HasTiming = false,
                        RemainingBoundaries = 0,
                        ExpiresWithEncounter = false,
                        StateKind = kind,
                        StatePayload = payload,
                    }
                );
            }
            return captured;
        }

        internal static IReadOnlyList<DungeonRulesEffectProjection> Project(
            RulesSnapshot snapshot,
            CreatureId target
        )
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            List<DungeonRulesEffectProjection> projected = new();
            foreach (
                ActiveRuleBinding binding in snapshot
                    .RuleBindings.Select(pair => pair.Value)
                    .Where(binding => binding.Owner == target && binding.EffectId.HasValue)
                    .OrderBy(binding => binding.CreationOrder)
                    .ThenBy(binding => binding.Id.Value, StringComparer.Ordinal)
            )
            {
                ActiveEffectId effectId = binding.EffectId.Value;
                if (!snapshot.ActiveEffects.TryGet(effectId, out ActiveEffectInstance effect))
                    throw new InvalidOperationException(
                        $"Effect binding '{binding.Id.Value}' has no active effect."
                    );
                snapshot.ActiveEffectTimings.TryGet(effect.Id, out ActiveEffectTimingState timing);
                projected.Add(new DungeonRulesEffectProjection(effect, binding, timing));
            }
            return projected;
        }

        internal static DungeonRulesEffectSaveState Capture(
            DungeonRulesEffectProjection projection,
            Func<CreatureId, DungeonRulesActorReference> identifyCreature
        )
        {
            if (projection == null)
                throw new ArgumentNullException(nameof(projection));
            if (identifyCreature == null)
                throw new ArgumentNullException(nameof(identifyCreature));
            ActiveEffectInstance effect = projection.Effect;
            ActiveRuleBinding binding = projection.Binding;
            ActiveEffectTimingState timing = projection.Timing;
            (string kind, string payload) = Codecs.Capture(
                effect.DefinitionId,
                effect.State,
                identifyCreature
            );
            return new DungeonRulesEffectSaveState
            {
                EffectId = effect.Id.Value,
                BindingId = binding.Id.Value,
                DefinitionId = effect.DefinitionId.Value,
                SourceActor = identifyCreature(effect.SourceCreature),
                BindingOwnerActor = identifyCreature(binding.Owner),
                RuleSource = effect.Source.Slug,
                DurationKind = effect.Duration.Kind,
                DurationAmount = effect.Duration.Amount,
                EffectStateVersion = effect.EffectStateVersion.Value,
                CreationOrder = binding.CreationOrder,
                BindingEnabled = binding.IsEnabled,
                HasTiming = timing != null,
                RemainingBoundaries = timing?.RemainingBoundaries ?? 0,
                ExpiresWithEncounter = timing?.ExpiresWithEncounter ?? false,
                StateKind = kind,
                StatePayload = payload,
            };
        }

        internal static IEnumerable<DungeonRulesActorReference> ActorReferences(
            DungeonRulesEffectSaveState effect
        )
        {
            yield return effect.SourceActor;
            yield return effect.BindingOwnerActor;
            foreach (
                DungeonRulesActorReference actor in Codecs.GetReferencedActors(
                    new RuleDefinitionId(effect.DefinitionId),
                    effect.StateKind,
                    effect.StatePayload
                )
            )
                yield return actor;
        }

        internal static EffectDuration RestoreDuration(EffectDurationKind kind, int amount) =>
            kind switch
            {
                EffectDurationKind.Indefinite when amount == 0 => EffectDuration.Indefinite,
                EffectDurationKind.Encounter when amount == 0 => EffectDuration.Encounter,
                EffectDurationKind.Rounds => EffectDuration.Rounds(amount),
                EffectDurationKind.Minutes => EffectDuration.Minutes(amount),
                _ => throw new ArgumentException("Saved effect duration is invalid."),
            };
    }

    internal sealed class ConditionEffectStateCodec : IDungeonEffectStateCodec
    {
        [Serializable]
        private sealed class Payload
        {
            public string Condition;
            public int Value;
        }

        public string Kind => "condition";
        public Type StateType => typeof(ConditionState);
        public IReadOnlyList<RuleDefinitionId> Definitions { get; } =
            new[] { ConditionRules.DefinitionId };

        public string Capture(
            IEffectState state,
            Func<CreatureId, DungeonRulesActorReference> identifyCreature
        )
        {
            ConditionState condition = (ConditionState)state;
            return JsonUtility.ToJson(
                new Payload { Condition = condition.Condition.Value, Value = condition.Value }
            );
        }

        public IEffectState Restore(
            string payload,
            Func<DungeonRulesActorReference, CreatureId> resolveCreature
        )
        {
            Payload value = Parse(payload);
            return new ConditionState(new ConditionId(value.Condition), value.Value);
        }

        public IReadOnlyList<DungeonRulesActorReference> GetReferencedActors(string payload)
        {
            Parse(payload);
            return Array.Empty<DungeonRulesActorReference>();
        }

        private static Payload Parse(string json)
        {
            Payload value = JsonUtility.FromJson<Payload>(json);
            if (value == null || string.IsNullOrWhiteSpace(value.Condition) || value.Value < 1)
                throw new ArgumentException("Saved condition effect state is invalid.");
            return value;
        }
    }

    internal sealed class SpellEffectStateCodec : IDungeonEffectStateCodec
    {
        [Serializable]
        private sealed class Payload
        {
            public string Spell;
            public int Rank;
            public DungeonRulesActorReference TargetActor;
        }

        public string Kind => "spell";
        public Type StateType => typeof(SpellEffectState);
        public IReadOnlyList<RuleDefinitionId> Definitions { get; }

        internal SpellEffectStateCodec(IEnumerable<RuleDefinitionId> definitions)
        {
            RuleDefinitionId[] copied =
                definitions?.Distinct().ToArray()
                ?? throw new ArgumentNullException(nameof(definitions));
            if (copied.Length == 0 || copied.Any(definition => definition.IsEmpty))
                throw new ArgumentException(
                    "At least one spell effect definition is required.",
                    nameof(definitions)
                );
            Definitions = Array.AsReadOnly(copied);
        }

        public string Capture(
            IEffectState state,
            Func<CreatureId, DungeonRulesActorReference> identifyCreature
        )
        {
            SpellEffectState spell = (SpellEffectState)state;
            return JsonUtility.ToJson(
                new Payload
                {
                    Spell = spell.Spell.Spell.Value,
                    Rank = spell.Spell.Rank,
                    TargetActor = identifyCreature(spell.Target),
                }
            );
        }

        public IEffectState Restore(
            string payload,
            Func<DungeonRulesActorReference, CreatureId> resolveCreature
        )
        {
            Payload value = Parse(payload);
            return new SpellEffectState(
                new SpellReference(new SpellId(value.Spell), value.Rank),
                resolveCreature(value.TargetActor)
            );
        }

        public IReadOnlyList<DungeonRulesActorReference> GetReferencedActors(string payload) =>
            new[] { Parse(payload).TargetActor };

        private static Payload Parse(string json)
        {
            Payload value = JsonUtility.FromJson<Payload>(json);
            if (
                value == null
                || string.IsNullOrWhiteSpace(value.Spell)
                || value.Rank < 1
                || !value.TargetActor.IsValid
            )
                throw new ArgumentException("Saved spell effect state is invalid.");
            return value;
        }
    }

    internal sealed class RageEffectStateCodec : IDungeonEffectStateCodec
    {
        [Serializable]
        private sealed class Payload
        {
            public bool StartedByQuickTempered;
        }

        public string Kind => "rage";
        public Type StateType => typeof(RageEffectState);
        public IReadOnlyList<RuleDefinitionId> Definitions { get; } =
            new[] { RageActionDefinition.EffectDefinitionId };

        public string Capture(
            IEffectState state,
            Func<CreatureId, DungeonRulesActorReference> identifyCreature
        ) =>
            JsonUtility.ToJson(
                new Payload
                {
                    StartedByQuickTempered = ((RageEffectState)state).StartedByQuickTempered,
                }
            );

        public IEffectState Restore(
            string payload,
            Func<DungeonRulesActorReference, CreatureId> resolveCreature
        ) => new RageEffectState(Parse(payload).StartedByQuickTempered);

        public IReadOnlyList<DungeonRulesActorReference> GetReferencedActors(string payload)
        {
            Parse(payload);
            return Array.Empty<DungeonRulesActorReference>();
        }

        private static Payload Parse(string json) =>
            JsonUtility.FromJson<Payload>(json)
            ?? throw new ArgumentException("Saved Rage effect state is invalid.");
    }
}
