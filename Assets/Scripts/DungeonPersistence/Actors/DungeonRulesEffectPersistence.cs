using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Creature.Rules;
using Game.DungeonPersistence.Repository;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;
using UnityEngine;

namespace Game.DungeonPersistence.Actors
{
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
        string Capture(IEffectState state, Func<CreatureId, string> identifyCreature);
        IEffectState Restore(string payload, Func<string, CreatureId> resolveCreature);
        IReadOnlyList<string> GetReferencedActors(string payload);
    }

    /// <summary>Stores the explicit codecs used by the current encounter composition.</summary>
    internal sealed class DungeonEffectStateCodecCatalog
    {
        private readonly IReadOnlyDictionary<string, IDungeonEffectStateCodec> byKind;
        private readonly IReadOnlyDictionary<Type, IDungeonEffectStateCodec> byType;

        internal DungeonEffectStateCodecCatalog(IEnumerable<IDungeonEffectStateCodec> codecs)
        {
            IDungeonEffectStateCodec[] copied =
                codecs?.ToArray() ?? throw new ArgumentNullException(nameof(codecs));
            if (
                copied.Any(codec =>
                    codec == null
                    || string.IsNullOrWhiteSpace(codec.Kind)
                    || codec.StateType == null
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
        }

        internal static DungeonEffectStateCodecCatalog CreateProduction() =>
            new(
                new IDungeonEffectStateCodec[]
                {
                    new ConditionEffectStateCodec(),
                    new SpellEffectStateCodec(),
                    new RageEffectStateCodec(),
                }
            );

        internal (string Kind, string Payload) Capture(
            IEffectState state,
            Func<CreatureId, string> identifyCreature
        )
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (!byType.TryGetValue(state.GetType(), out IDungeonEffectStateCodec codec))
                throw new InvalidOperationException(
                    $"Effect state type '{state.GetType().Name}' has no dungeon persistence codec."
                );
            return (codec.Kind, codec.Capture(state, identifyCreature));
        }

        internal IEffectState Restore(
            string kind,
            string payload,
            Func<string, CreatureId> resolveCreature
        ) => Require(kind).Restore(payload, resolveCreature);

        internal IReadOnlyList<string> GetReferencedActors(string kind, string payload) =>
            Require(kind).GetReferencedActors(payload);

        private IDungeonEffectStateCodec Require(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind) || !byKind.TryGetValue(kind, out var codec))
                throw new ArgumentException($"Unknown effect-state persistence kind '{kind}'.");
            return codec;
        }
    }

    /// <summary>
    /// Temporarily carries validated rules-effect save envelopes until combatant enrollment commits
    /// them to the new encounter's authoritative store.
    /// </summary>
    internal sealed class DungeonRulesEffectSeed : MonoBehaviour
    {
        private DungeonRulesEffectSaveState[] effects = Array.Empty<DungeonRulesEffectSaveState>();
        private Func<string, GameObject> resolveActor = _ =>
            throw new InvalidOperationException("The rules-effect seed is not initialized.");

        internal IReadOnlyList<DungeonRulesEffectSaveState> Effects => effects;

        internal void Initialize(
            IEnumerable<DungeonRulesEffectSaveState> restoredEffects,
            Func<string, GameObject> actorResolver
        )
        {
            DungeonRulesEffectSaveState[] copied =
                restoredEffects?.ToArray()
                ?? throw new ArgumentNullException(nameof(restoredEffects));
            Func<string, GameObject> copiedResolver =
                actorResolver ?? throw new ArgumentNullException(nameof(actorResolver));
            foreach (string actorId in copied.SelectMany(DungeonRulesEffectPersistence.ActorIds))
            {
                if (copiedResolver(actorId) == null)
                    throw new InvalidOperationException(
                        $"Saved rules effect references unavailable actor '{actorId}'."
                    );
            }
            effects = copied;
            resolveActor = copiedResolver;
            GetComponent<ConditionSeed>()?.Clear();
        }

        internal CreatureId ResolveCreature(string actorId, UnityCombatRulesBridge owner)
        {
            GameObject actor = resolveActor(actorId);
            if (
                actor == null
                || !actor.TryGetComponent(out CreatureComponent creature)
                || !owner.TryGetCreatureId(creature, out CreatureId id)
            )
                throw new InvalidOperationException(
                    $"Saved rules-effect actor '{actorId}' is not enrolled in this encounter."
                );
            return id;
        }
    }

    /// <summary>Enrolls saved generic effect envelopes through the common combatant addition.</summary>
    internal sealed class DungeonRulesEffectPersistenceModule : IUnityCombatantEnrollmentModule
    {
        private readonly UnityCombatRulesBridge owner;
        private readonly DungeonEffectStateCodecCatalog codecs;

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

            foreach (DungeonRulesEffectSaveState saved in seed.Effects)
            {
                CreatureId bindingOwner = seed.ResolveCreature(saved.BindingOwnerActorId, owner);
                if (bindingOwner != builder.CreatureId)
                    throw new InvalidOperationException(
                        $"Saved effect '{saved.EffectId}' is enrolled through the wrong binding owner."
                    );
                CreatureId source = seed.ResolveCreature(saved.SourceActorId, owner);
                Func<string, CreatureId> resolve = actorId => seed.ResolveCreature(actorId, owner);
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
                    codecs.Restore(saved.StateKind, saved.StatePayload, resolve),
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
                builder.AddActiveEffects(new[] { effect });
                builder.AddRuleBindings(new[] { binding });
                if (saved.HasTiming)
                {
                    builder.AddActiveEffectTimings(
                        new[]
                        {
                            new ActiveEffectTimingRestore(
                                effectId,
                                saved.RemainingBoundaries,
                                saved.ExpiresWithEncounter
                            ),
                        }
                    );
                }
            }
        }
    }

    /// <summary>Owns generic capture and validation for rules-backed dungeon effects.</summary>
    internal static class DungeonRulesEffectPersistence
    {
        internal static DungeonEffectStateCodecCatalog Codecs { get; } =
            DungeonEffectStateCodecCatalog.CreateProduction();

        internal static IReadOnlyList<DungeonRulesEffectSaveState> Capture(
            ActionController controller,
            Func<GameObject, string> identifyActor
        )
        {
            if (
                !controller.TryGetCombatRules(
                    out UnityCombatRulesBridge bridge,
                    out CreatureId target
                )
            )
                return Array.Empty<DungeonRulesEffectSaveState>();

            string IdentifyCreature(CreatureId creature) =>
                identifyActor(bridge.GetController(creature).gameObject);
            List<DungeonRulesEffectSaveState> captured = new();
            foreach (
                ActiveRuleBinding binding in bridge
                    .Snapshot.RuleBindings.Select(pair => pair.Value)
                    .Where(binding => binding.Owner == target && binding.EffectId.HasValue)
                    .OrderBy(binding => binding.CreationOrder)
                    .ThenBy(binding => binding.Id.Value, StringComparer.Ordinal)
            )
            {
                ActiveEffectId effectId = binding.EffectId.Value;
                if (
                    !bridge.Snapshot.ActiveEffects.TryGet(effectId, out ActiveEffectInstance effect)
                )
                    throw new InvalidOperationException(
                        $"Effect binding '{binding.Id.Value}' has no active effect."
                    );
                (string kind, string payload) = Codecs.Capture(effect.State, IdentifyCreature);
                bool hasTiming = bridge.Snapshot.ActiveEffectTimings.TryGet(
                    effect.Id,
                    out ActiveEffectTimingState timing
                );
                captured.Add(
                    new DungeonRulesEffectSaveState
                    {
                        EffectId = effect.Id.Value,
                        BindingId = binding.Id.Value,
                        DefinitionId = effect.DefinitionId.Value,
                        SourceActorId = IdentifyCreature(effect.SourceCreature),
                        BindingOwnerActorId = IdentifyCreature(binding.Owner),
                        RuleSource = effect.Source.Slug,
                        DurationKind = effect.Duration.Kind,
                        DurationAmount = effect.Duration.Amount,
                        EffectStateVersion = effect.EffectStateVersion.Value,
                        CreationOrder = binding.CreationOrder,
                        BindingEnabled = binding.IsEnabled,
                        HasTiming = hasTiming,
                        RemainingBoundaries = hasTiming ? timing.RemainingBoundaries : 0,
                        ExpiresWithEncounter = hasTiming && timing.ExpiresWithEncounter,
                        StateKind = kind,
                        StatePayload = payload,
                    }
                );
            }
            return captured;
        }

        internal static IEnumerable<string> ActorIds(DungeonRulesEffectSaveState effect)
        {
            yield return effect.SourceActorId;
            yield return effect.BindingOwnerActorId;
            foreach (
                string actorId in Codecs.GetReferencedActors(effect.StateKind, effect.StatePayload)
            )
                yield return actorId;
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

        public string Capture(IEffectState state, Func<CreatureId, string> identifyCreature)
        {
            ConditionState condition = (ConditionState)state;
            return JsonUtility.ToJson(
                new Payload { Condition = condition.Condition.Value, Value = condition.Value }
            );
        }

        public IEffectState Restore(string payload, Func<string, CreatureId> resolveCreature)
        {
            Payload value = Parse(payload);
            return new ConditionState(new ConditionId(value.Condition), value.Value);
        }

        public IReadOnlyList<string> GetReferencedActors(string payload)
        {
            Parse(payload);
            return Array.Empty<string>();
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
            public string TargetActorId;
        }

        public string Kind => "spell";
        public Type StateType => typeof(SpellEffectState);

        public string Capture(IEffectState state, Func<CreatureId, string> identifyCreature)
        {
            SpellEffectState spell = (SpellEffectState)state;
            return JsonUtility.ToJson(
                new Payload
                {
                    Spell = spell.Spell.Spell.Value,
                    Rank = spell.Spell.Rank,
                    TargetActorId = identifyCreature(spell.Target),
                }
            );
        }

        public IEffectState Restore(string payload, Func<string, CreatureId> resolveCreature)
        {
            Payload value = Parse(payload);
            return new SpellEffectState(
                new SpellReference(new SpellId(value.Spell), value.Rank),
                resolveCreature(value.TargetActorId)
            );
        }

        public IReadOnlyList<string> GetReferencedActors(string payload) =>
            new[] { Parse(payload).TargetActorId };

        private static Payload Parse(string json)
        {
            Payload value = JsonUtility.FromJson<Payload>(json);
            if (
                value == null
                || string.IsNullOrWhiteSpace(value.Spell)
                || value.Rank < 1
                || string.IsNullOrWhiteSpace(value.TargetActorId)
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

        public string Capture(IEffectState state, Func<CreatureId, string> identifyCreature) =>
            JsonUtility.ToJson(
                new Payload
                {
                    StartedByQuickTempered = ((RageEffectState)state).StartedByQuickTempered,
                }
            );

        public IEffectState Restore(string payload, Func<string, CreatureId> resolveCreature) =>
            new RageEffectState(Parse(payload).StartedByQuickTempered);

        public IReadOnlyList<string> GetReferencedActors(string payload)
        {
            Parse(payload);
            return Array.Empty<string>();
        }

        private static Payload Parse(string json) =>
            JsonUtility.FromJson<Payload>(json)
            ?? throw new ArgumentException("Saved Rage effect state is invalid.");
    }
}
