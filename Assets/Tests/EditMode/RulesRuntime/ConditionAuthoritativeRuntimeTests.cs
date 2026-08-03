using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class ConditionAuthoritativeRuntimeTests
    {
        private static readonly CreatureId Owner = new CreatureId("condition-owner");
        private static readonly CreatureId SourceCreature = new CreatureId("condition-source");
        private static readonly RuleSource AdoptionSource = RuleSource.FromSlug(
            "condition-enrollment"
        );

        [Test]
        public async Task AliasApplyCommitsOneCanonicalGenericCreationFact()
        {
            InMemoryRulesStore store = CreateStoreWithRegisteredCreatures();
            RuleDispatcher dispatcher = CreateDispatcher(store);

            OpResult<ConditionApplicationOutcome> result = await dispatcher.Dispatch(
                Apply("Flat-Footed", "alias-source", ConditionMarkerState.Instance)
            );

            ResolvedOpResult<ConditionApplicationOutcome> resolved = RequireResolved(result);
            ActiveEffectCreatedFact created = resolved
                .Facts.OfType<ActiveEffectCreatedFact>()
                .Single(fact => fact.DefinitionId == ConditionRuleDefinitions.OffGuard);
            Assert.That(created.EffectId, Is.EqualTo(resolved.Value.EffectId));
            Assert.That(
                resolved.Facts.Select(fact => fact.RootOpId).Distinct().Count(),
                Is.EqualTo(1)
            );
            Assert.That(
                store.Snapshot.ActiveEffects[resolved.Value.EffectId].DefinitionId,
                Is.EqualTo(ConditionRuleDefinitions.OffGuard)
            );
            Assert.That(
                ConditionSelectors.HasMarker(
                    store.Snapshot,
                    Owner,
                    ConditionRuleDefinitions.OffGuard
                ),
                Is.True
            );
        }

        [Test]
        public async Task PreparedConditionImmunityBlocksCanonicalApplicationWithoutMutation()
        {
            PreparedCreatureInputs prepared = PreparedWithConditionImmunity("Flat-Footed");
            InMemoryRulesStore store = new InMemoryRulesStore(
                new RulesStateSeed()
                    .SeedCreature(new CreatureState(Owner, new PlayerId("condition-owner-player")))
                    .SeedCreature(
                        new CreatureState(SourceCreature, new PlayerId("condition-source-player"))
                    )
                    .SeedPreparedInputs(Owner, prepared)
            );
            RuleDispatcher dispatcher = CreateDispatcher(store);
            long version = store.Snapshot.Version;

            ResolvedOpResult<ConditionApplicationOutcome> blocked = RequireResolved(
                await dispatcher.Dispatch(
                    Apply("off-guard", "immune-source", ConditionMarkerState.Instance)
                )
            );

            Assert.That(blocked.Value.Status, Is.EqualTo(ConditionApplicationStatus.Blocked));
            Assert.That(blocked.Value.BlockedReason, Does.Contain("immune to off-guard"));
            Assert.That(blocked.Facts, Is.Empty);
            Assert.That(store.Snapshot.Version, Is.EqualTo(version));
            Assert.That(store.Snapshot.ActiveEffects, Is.Empty);
            Assert.That(store.Snapshot.RuleBindings, Is.Empty);
        }

        [Test]
        public async Task FreshApplicationFromUnregisteredSourceRejectsAtomically()
        {
            InMemoryRulesStore store = CreateStoreWithRegisteredCreatures();
            RuleDispatcher dispatcher = CreateDispatcher(store);

            OpResult<ConditionApplicationOutcome> result = await dispatcher.Dispatch(
                new ApplyConditionOp(
                    "off-guard",
                    Owner,
                    new CreatureId("reserved-historical-source"),
                    RuleSource.FromSlug("unregistered-fresh-source"),
                    EffectDuration.Indefinite,
                    ConditionMarkerState.Instance
                )
            );

            Assert.That(result, Is.TypeOf<InvalidOpResult<ConditionApplicationOutcome>>());
            Assert.That(
                ((InvalidOpResult<ConditionApplicationOutcome>)result).Reason,
                Does.Contain("registered source creature")
            );
            Assert.That(result.Facts, Is.Empty);
            Assert.That(store.Snapshot.Version, Is.Zero);
            Assert.That(store.Snapshot.ActiveEffects, Is.Empty);
            Assert.That(store.Snapshot.RuleBindings, Is.Empty);
        }

        [Test]
        public async Task ImmunityCannotMaskFreshUnregisteredSource()
        {
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Owner, new PlayerId("condition-owner-player")))
                .SeedCreature(
                    new CreatureState(SourceCreature, new PlayerId("condition-source-player"))
                )
                .SeedPreparedInputs(Owner, PreparedWithConditionImmunity("off-guard"))
                .SeedPreparedInputs(SourceCreature, PreparedCreatureInputs.Empty);
            InMemoryRulesStore store = new(seed);

            OpResult<ConditionApplicationOutcome> result = await CreateDispatcher(store)
                .Dispatch(
                    new ApplyConditionOp(
                        "off-guard",
                        Owner,
                        new CreatureId("reserved-immune-source"),
                        RuleSource.FromSlug("unregistered-immune-source"),
                        EffectDuration.Indefinite,
                        ConditionMarkerState.Instance
                    )
                );

            Assert.That(result, Is.TypeOf<InvalidOpResult<ConditionApplicationOutcome>>());
            Assert.That(
                ((InvalidOpResult<ConditionApplicationOutcome>)result).Reason,
                Does.Contain("registered source creature")
            );
            Assert.That(result.Facts, Is.Empty);
            Assert.That(store.Snapshot.Version, Is.Zero);
        }

        [Test]
        public void MalformedActiveConditionPairFailsBeforeImmunityClassification()
        {
            RuleSource effectSource = RuleSource.FromSlug("malformed-source");
            ActiveEffectInstance effect = new(
                new ActiveEffectId("malformed-effect"),
                ConditionRuleDefinitions.OffGuard,
                SourceCreature,
                effectSource,
                EffectDuration.Indefinite,
                ConditionMarkerState.Instance
            );
            ActiveRuleBinding binding = new(
                new BindingId("malformed-binding"),
                effect.DefinitionId,
                Owner,
                effect.Id,
                RuleSource.FromSlug("conflicting-binding-source"),
                1
            );
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Owner, new PlayerId("condition-owner-player")))
                .SeedCreature(
                    new CreatureState(SourceCreature, new PlayerId("condition-source-player"))
                )
                .SeedPreparedInputs(Owner, PreparedWithConditionImmunity("off-guard"))
                .SeedPreparedInputs(SourceCreature, PreparedCreatureInputs.Empty)
                .SeedActiveEffect(effect)
                .SeedRuleBinding(binding);

            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
                _ = new InMemoryRulesStore(seed)
            );

            Assert.That(
                failure.Message,
                Is.EqualTo("Active condition malformed-effect has a malformed registration.")
            );
        }

        [Test]
        public void RegisteredConditionTargetWithoutPreparedInputsFailsClosedWithoutMutation()
        {
            InMemoryRulesStore store = new InMemoryRulesStore(
                new RulesStateSeed()
                    .SeedCreature(new CreatureState(Owner, new PlayerId("condition-owner-player")))
                    .SeedCreature(
                        new CreatureState(SourceCreature, new PlayerId("condition-source-player"))
                    )
            );
            RuleDispatcher dispatcher = CreateDispatcher(store);

            InvalidOperationException failure = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(
                        Apply("off-guard", "missing-prepared", ConditionMarkerState.Instance)
                    )
            );

            Assert.That(failure.Message, Does.Contain("no authoritative prepared inputs"));
            Assert.That(store.Snapshot.Version, Is.Zero);
            Assert.That(store.Snapshot.ActiveEffects, Is.Empty);
            Assert.That(store.Snapshot.RuleBindings, Is.Empty);
        }

        [Test]
        public void ActiveConditionForUnregisteredOwnerIsRejectedFromInitialState()
        {
            ActiveEffectRegistration registration = Registration(
                "unregistered-initial-owner",
                Owner,
                ConditionRuleDefinitions.OffGuard,
                RuleSource.FromSlug("unregistered-initial-owner-source"),
                ConditionMarkerState.Instance,
                1
            );
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(
                    new CreatureState(SourceCreature, new PlayerId("condition-source-player"))
                )
                .SeedPreparedInputs(SourceCreature, PreparedCreatureInputs.Empty)
                .SeedActiveEffect(registration.Effect)
                .SeedRuleBinding(registration.Binding);

            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
                _ = new InMemoryRulesStore(seed)
            );

            Assert.That(
                failure.Message,
                Is.EqualTo($"Active condition owner {Owner.Value} is not a registered creature.")
            );
            Assert.That(seed.ActiveEffects[registration.Effect.Id], Is.SameAs(registration.Effect));
            Assert.That(
                seed.RuleBindings[registration.Binding.Id],
                Is.SameAs(registration.Binding)
            );
        }

        [Test]
        public void ExpiredConditionTombstoneMayRemainWithoutOwnerOrPreparedInputs()
        {
            RuleSource source = RuleSource.FromSlug("expired-immune-source");
            ActiveEffectInstance effect = new ActiveEffectInstance(
                new ActiveEffectId("expired-immune-effect"),
                ConditionRuleDefinitions.OffGuard,
                SourceCreature,
                source,
                EffectDuration.Indefinite,
                ConditionMarkerState.Instance,
                new EffectStateVersion(2),
                ActiveEffectStatus.Expired
            );
            ActiveRuleBinding binding = new ActiveRuleBinding(
                new BindingId("expired-immune-binding"),
                effect.DefinitionId,
                Owner,
                effect.Id,
                source,
                1,
                false
            );

            RulesSnapshot snapshot = new InMemoryRulesStore(
                new RulesStateSeed().SeedActiveEffect(effect).SeedRuleBinding(binding)
            ).Snapshot;

            Assert.That(snapshot.ActiveEffects[effect.Id], Is.EqualTo(effect));
            Assert.That(ConditionSelectors.GetActiveSlugs(snapshot, Owner), Is.Empty);
        }

        [TestCase("missing")]
        [TestCase("expired")]
        [TestCase("disabled")]
        [TestCase("mismatched-id")]
        [TestCase("mismatched-definition")]
        [TestCase("mismatched-source")]
        public void ActiveSlugSelectionRequiresOneExactEnabledActivePair(string invalidPair)
        {
            ActiveEffectId effectId = new ActiveEffectId("selector-effect");
            RuleSource source = RuleSource.FromSlug("selector-source");
            ActiveEffectInstance effect = new ActiveEffectInstance(
                effectId,
                invalidPair == "mismatched-definition"
                    ? ConditionRuleDefinitions.Deafened
                    : ConditionRuleDefinitions.OffGuard,
                Owner,
                invalidPair == "mismatched-source"
                    ? RuleSource.FromSlug("other-selector-source")
                    : source,
                EffectDuration.Indefinite,
                ConditionMarkerState.Instance,
                default,
                invalidPair == "expired" ? ActiveEffectStatus.Expired : ActiveEffectStatus.Active
            );
            ActiveRuleBinding binding = new ActiveRuleBinding(
                new BindingId("selector-binding"),
                ConditionRuleDefinitions.OffGuard,
                Owner,
                invalidPair == "mismatched-id"
                    ? new ActiveEffectId("other-selector-effect")
                    : effectId,
                source,
                1,
                invalidPair != "disabled"
            );
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Owner, new PlayerId("selector-owner")))
                .SeedPreparedInputs(Owner, PreparedCreatureInputs.Empty)
                .SeedRuleBinding(binding);
            if (invalidPair != "missing")
                seed.SeedActiveEffect(effect);

            if (invalidPair == "missing" || invalidPair == "expired")
            {
                IReadOnlyList<string> slugs = ConditionSelectors.GetActiveSlugs(
                    new InMemoryRulesStore(seed).Snapshot,
                    Owner
                );
                Assert.That(slugs, Is.Empty);
            }
            else
            {
                InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
                    _ = new InMemoryRulesStore(seed)
                );
                Assert.That(failure.Message, Does.Contain("malformed registration"));
            }
        }

        [Test]
        public async Task AdoptionIsIdempotentWithoutDuplicateStateOrFacts()
        {
            RuleSource source = RuleSource.FromSlug("persisted-source");
            ActiveEffectInstance effect = new ActiveEffectInstance(
                new ActiveEffectId("persisted-effect"),
                ConditionRuleDefinitions.Deafened,
                SourceCreature,
                source,
                EffectDuration.Indefinite,
                ConditionMarkerState.Instance
            );
            ActiveRuleBinding binding = new ActiveRuleBinding(
                new BindingId("persisted-binding"),
                effect.DefinitionId,
                Owner,
                effect.Id,
                source,
                4
            );
            InMemoryRulesStore store = CreateStoreWithRegisteredCreatures();
            RuleDispatcher dispatcher = CreateDispatcher(store);
            AdoptActiveEffectRegistrationsOp adopt = new AdoptActiveEffectRegistrationsOp(
                new[] { new ActiveEffectRegistration(effect, binding) },
                AdoptionSource
            );

            ResolvedOpResult<ActiveEffectAdoptionOutcome> first = RequireResolved(
                await dispatcher.Dispatch(adopt)
            );
            long committedVersion = store.Snapshot.Version;
            ResolvedOpResult<ActiveEffectAdoptionOutcome> repeated = RequireResolved(
                await dispatcher.Dispatch(adopt)
            );

            Assert.That(first.Value.Adopted, Is.EqualTo(1));
            Assert.That(first.Facts, Has.Count.EqualTo(1));
            Assert.That(
                first
                    .Facts.OfType<ActiveEffectAdoptedFact>()
                    .Single(fact => fact.DefinitionId == ConditionRuleDefinitions.Deafened)
                    .EffectId,
                Is.EqualTo(effect.Id)
            );
            Assert.That(repeated.Value.Adopted, Is.Zero);
            Assert.That(repeated.Facts, Is.Empty);
            Assert.That(store.Snapshot.Version, Is.EqualTo(committedVersion));
            Assert.That(store.Snapshot.ActiveEffects[effect.Id], Is.EqualTo(effect));
        }

        [Test]
        public async Task ExternalAdoptionRejectsWholeBatchWhenOneConditionIsImmune()
        {
            PreparedCreatureInputs prepared = PreparedWithConditionImmunity("Flat-Footed");
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Owner, new PlayerId("condition-owner-player")))
                .SeedCreature(
                    new CreatureState(SourceCreature, new PlayerId("condition-source-player"))
                )
                .SeedPreparedInputs(Owner, prepared)
                .SeedPreparedInputs(SourceCreature, PreparedCreatureInputs.Empty);
            InMemoryRulesStore store = new InMemoryRulesStore(seed);
            RuleDispatcher dispatcher = CreateDispatcher(store);
            ActiveEffectRegistration allowed = Registration(
                "allowed-adoption",
                ConditionRuleDefinitions.Deafened
            );
            ActiveEffectRegistration immune = Registration(
                "immune-adoption",
                ConditionRuleDefinitions.OffGuard
            );

            OpResult<ActiveEffectAdoptionOutcome> result = await dispatcher.Dispatch(
                new AdoptActiveEffectRegistrationsOp(new[] { allowed, immune }, AdoptionSource)
            );

            Assert.That(result, Is.TypeOf<InvalidOpResult<ActiveEffectAdoptionOutcome>>());
            Assert.That(
                ((InvalidOpResult<ActiveEffectAdoptionOutcome>)result).Reason,
                Does.Contain("immune to off-guard")
            );
            Assert.That(store.Snapshot.Version, Is.Zero);
            Assert.That(store.Snapshot.ActiveEffects, Is.Empty);
            Assert.That(store.Snapshot.RuleBindings, Is.Empty);
            Assert.That(result.Facts, Is.Empty);

            ActiveEffectRegistration Registration(string identity, RuleDefinitionId definition)
            {
                RuleSource source = RuleSource.FromSlug(identity);
                ActiveEffectInstance effect = new ActiveEffectInstance(
                    new ActiveEffectId($"{identity}-effect"),
                    definition,
                    SourceCreature,
                    source,
                    EffectDuration.Indefinite,
                    ConditionMarkerState.Instance
                );
                return new ActiveEffectRegistration(
                    effect,
                    new ActiveRuleBinding(
                        new BindingId($"{identity}-binding"),
                        definition,
                        Owner,
                        effect.Id,
                        source,
                        identity == "allowed-adoption" ? 1 : 2
                    )
                );
            }
        }

        [Test]
        public async Task AdoptionPublishesExactActiveAndExpiredProvenanceWithoutCreationFacts()
        {
            RuleSource source = RuleSource.FromSlug("restored-stunned-source");
            ActiveEffectInstance active = new ActiveEffectInstance(
                new ActiveEffectId("active-stunned-effect"),
                ConditionRuleDefinitions.Stunned,
                SourceCreature,
                source,
                EffectDuration.Indefinite,
                new ValuedStunnedConditionState(2),
                new EffectStateVersion(4),
                ActiveEffectStatus.Active
            );
            ActiveRuleBinding activeBinding = new ActiveRuleBinding(
                new BindingId("active-stunned-binding"),
                active.DefinitionId,
                Owner,
                active.Id,
                source,
                3
            );
            ActiveEffectInstance expired = new ActiveEffectInstance(
                new ActiveEffectId("expired-stunned-effect"),
                ConditionRuleDefinitions.Stunned,
                SourceCreature,
                source,
                EffectDuration.Encounter,
                DurationOnlyStunnedConditionState.Instance,
                new EffectStateVersion(7),
                ActiveEffectStatus.Expired
            );
            ActiveRuleBinding expiredBinding = new ActiveRuleBinding(
                new BindingId("expired-stunned-binding"),
                expired.DefinitionId,
                Owner,
                expired.Id,
                source,
                8,
                isEnabled: false
            );
            InMemoryRulesStore store = CreateStoreWithRegisteredCreatures();
            RuleDispatcher dispatcher = CreateDispatcher(store);
            CountingConditionCreatedObserver created = new CountingConditionCreatedObserver();
            using IDisposable registration =
                dispatcher.RegisterFactObserver<ActiveEffectCreatedFact>(created);

            ResolvedOpResult<ActiveEffectAdoptionOutcome> result = RequireResolved(
                await dispatcher.Dispatch(
                    new AdoptActiveEffectRegistrationsOp(
                        new[]
                        {
                            new ActiveEffectRegistration(active, activeBinding),
                            new ActiveEffectRegistration(expired, expiredBinding),
                        },
                        AdoptionSource
                    )
                )
            );

            Assert.That(result.Value.Adopted, Is.EqualTo(2));
            Assert.That(created.Count, Is.Zero, "Restore must not trigger creation listeners.");
            Assert.That(
                result
                    .Facts.OfType<ActiveEffectCreatedFact>()
                    .Where(fact =>
                        ConditionRuleDefinitions.IsConditionDefinition(fact.DefinitionId)
                    ),
                Is.Empty
            );
            ActiveEffectAdoptedFact[] adopted = result
                .Facts.OfType<ActiveEffectAdoptedFact>()
                .Where(fact => ConditionRuleDefinitions.IsConditionDefinition(fact.DefinitionId))
                .ToArray();
            Assert.That(adopted.Select(fact => fact.Effect), Is.EqualTo(new[] { active, expired }));
            Assert.That(
                adopted.Select(fact => fact.Binding),
                Is.EqualTo(new[] { activeBinding, expiredBinding })
            );
            Assert.That(adopted[0].Effect.Status, Is.EqualTo(ActiveEffectStatus.Active));
            Assert.That(adopted[1].Effect.Status, Is.EqualTo(ActiveEffectStatus.Expired));
        }

        [Test]
        public async Task AdoptionRejectsLaterLifecycleConflictWithoutStateOrFacts()
        {
            RuleSource source = RuleSource.FromSlug("atomic-adoption");
            ActiveEffectRegistration pending = Registration(
                "valid",
                Owner,
                ConditionRuleDefinitions.Deafened,
                source,
                ConditionMarkerState.Instance,
                1
            );
            ActiveEffectRegistration existing = Registration(
                "conflicting",
                Owner,
                ConditionRuleDefinitions.Fatigued,
                source,
                ConditionMarkerState.Instance,
                2
            );
            InMemoryRulesStore store = CreateStoreWithRegisteredCreatures();
            RuleDispatcher dispatcher = CreateDispatcher(store);
            RequireResolved(
                await dispatcher.Dispatch(
                    new AdoptActiveEffectRegistrationsOp(new[] { existing }, AdoptionSource)
                )
            );
            RequireResolved(
                await dispatcher.Dispatch(
                    new CleanupConditionsFromSourceOp(
                        Owner,
                        existing.Effect.DefinitionId,
                        source,
                        ConditionCleanupKind.Expire
                    )
                )
            );
            long conflictedVersion = store.Snapshot.Version;

            OpResult<ActiveEffectAdoptionOutcome> result = await dispatcher.Dispatch(
                new AdoptActiveEffectRegistrationsOp(new[] { pending, existing }, AdoptionSource)
            );

            Assert.That(result, Is.TypeOf<InvalidOpResult<ActiveEffectAdoptionOutcome>>());
            Assert.That(
                ((InvalidOpResult<ActiveEffectAdoptionOutcome>)result).Reason,
                Is.EqualTo(
                    "An adopted active-effect identity is already used by different or partial state."
                )
            );
            Assert.That(result.Facts, Is.Empty);
            Assert.That(store.Snapshot.Version, Is.EqualTo(conflictedVersion));
            Assert.That(store.Snapshot.ActiveEffects.Contains(pending.Effect.Id), Is.False);
            Assert.That(
                store.Snapshot.ActiveEffects[existing.Effect.Id].Status,
                Is.EqualTo(ActiveEffectStatus.Expired)
            );
            Assert.That(store.Snapshot.RuleBindings[existing.Binding.Id].IsEnabled, Is.False);
        }

        [Test]
        public async Task CleanupRemovesActiveAndExpiredRegistrationsInStableOrder()
        {
            RuleSource source = RuleSource.FromSlug("atomic-cleanup");
            ActiveEffectRegistration active = Registration(
                "cleanup-valid",
                Owner,
                ConditionRuleDefinitions.OffGuard,
                source,
                ConditionMarkerState.Instance,
                1
            );
            ActiveEffectRegistration expiresFirst = Registration(
                "cleanup-conflict",
                Owner,
                ConditionRuleDefinitions.Deafened,
                source,
                ConditionMarkerState.Instance,
                2
            );
            InMemoryRulesStore store = CreateStoreWithRegisteredCreatures();
            RuleDispatcher dispatcher = CreateDispatcher(store);
            RequireResolved(
                await dispatcher.Dispatch(
                    new AdoptActiveEffectRegistrationsOp(
                        new[] { active, expiresFirst },
                        AdoptionSource
                    )
                )
            );
            RequireResolved(
                await dispatcher.Dispatch(
                    new CleanupConditionsFromSourceOp(
                        Owner,
                        expiresFirst.Effect.DefinitionId,
                        source,
                        ConditionCleanupKind.Expire
                    )
                )
            );

            ResolvedOpResult<ConditionCleanupOutcome> result = RequireResolved(
                await dispatcher.Dispatch(
                    new CleanupConditionsFromSourceOp(source, ConditionCleanupKind.Remove)
                )
            );

            Assert.That(
                result.Value.Affected,
                Is.EqualTo(new[] { active.Effect.Id, expiresFirst.Effect.Id })
            );
            Assert.That(store.Snapshot.ActiveEffects, Is.Empty);
            Assert.That(store.Snapshot.RuleBindings, Is.Empty);
        }

        [Test]
        public async Task CleanupRejectsLaterConflictWithoutPartialCommitOrFacts()
        {
            RuleSource source = RuleSource.FromSlug("atomic-cleanup-rejection");
            ActiveEffectInstance validEffect = new ActiveEffectInstance(
                new ActiveEffectId("effect-cleanup-valid"),
                ConditionRuleDefinitions.OffGuard,
                SourceCreature,
                source,
                EffectDuration.Indefinite,
                ConditionMarkerState.Instance
            );
            ActiveRuleBinding validBinding = new ActiveRuleBinding(
                new BindingId("binding-cleanup-valid"),
                validEffect.DefinitionId,
                Owner,
                validEffect.Id,
                source,
                1,
                isEnabled: true
            );
            ActiveEffectInstance conflictingEffect = new ActiveEffectInstance(
                new ActiveEffectId("effect-cleanup-conflict"),
                ConditionRuleDefinitions.Deafened,
                SourceCreature,
                source,
                EffectDuration.Indefinite,
                ConditionMarkerState.Instance,
                EffectStateVersion.Initial,
                ActiveEffectStatus.Expired
            );
            ActiveRuleBinding conflictingBinding = new ActiveRuleBinding(
                new BindingId("binding-cleanup-conflict"),
                conflictingEffect.DefinitionId,
                Owner,
                conflictingEffect.Id,
                source,
                2,
                isEnabled: true
            );
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Owner, new PlayerId("condition-owner-player")))
                .SeedPreparedInputs(Owner, PreparedCreatureInputs.Empty)
                .SeedCreature(
                    new CreatureState(SourceCreature, new PlayerId("condition-source-player"))
                )
                .SeedPreparedInputs(SourceCreature, PreparedCreatureInputs.Empty)
                .SeedActiveEffect(validEffect)
                .SeedRuleBinding(validBinding)
                .SeedActiveEffect(conflictingEffect)
                .SeedRuleBinding(conflictingBinding);
            InMemoryRulesStore store = new InMemoryRulesStore(seed);
            RuleDispatcher dispatcher = CreateDispatcher(store);
            RulesSnapshot original = store.Snapshot;

            OpResult<ConditionCleanupOutcome> result = await dispatcher.Dispatch(
                new CleanupConditionsFromSourceOp(source, ConditionCleanupKind.Remove)
            );

            Assert.That(result, Is.TypeOf<InvalidOpResult<ConditionCleanupOutcome>>());
            Assert.That(
                ((InvalidOpResult<ConditionCleanupOutcome>)result).Reason,
                Is.EqualTo(
                    "Source cleanup found conflicting lifecycle state for effect-cleanup-conflict."
                )
            );
            Assert.That(result.Facts, Is.Empty);
            Assert.That(store.Snapshot, Is.SameAs(original));
            Assert.That(store.Snapshot.Version, Is.EqualTo(original.Version));
            Assert.That(store.Snapshot.ActiveEffects[validEffect.Id], Is.SameAs(validEffect));
            Assert.That(store.Snapshot.RuleBindings[validBinding.Id], Is.SameAs(validBinding));
            Assert.That(
                store.Snapshot.ActiveEffects[conflictingEffect.Id],
                Is.SameAs(conflictingEffect)
            );
            Assert.That(
                store.Snapshot.RuleBindings[conflictingBinding.Id],
                Is.SameAs(conflictingBinding)
            );
        }

        [Test]
        public async Task SourceWideCleanupSpansOwnersAndDefinitionsInStableOrder()
        {
            CreatureId otherOwner = new CreatureId("other-condition-owner");
            RuleSource source = RuleSource.FromSlug("source-wide-cleanup");
            ActiveEffectRegistration later = Registration(
                "later",
                Owner,
                ConditionRuleDefinitions.Slowed,
                source,
                new SlowedConditionState(1),
                9
            );
            ActiveEffectRegistration first = Registration(
                "first",
                otherOwner,
                ConditionRuleDefinitions.Fatigued,
                source,
                ConditionMarkerState.Instance,
                2
            );
            ActiveEffectRegistration middle = Registration(
                "middle",
                Owner,
                ConditionRuleDefinitions.Deafened,
                source,
                ConditionMarkerState.Instance,
                5
            );
            InMemoryRulesStore store = CreateStoreWithRegisteredCreatures(otherOwner);
            RuleDispatcher dispatcher = CreateDispatcher(store);
            RequireResolved(
                await dispatcher.Dispatch(
                    new AdoptActiveEffectRegistrationsOp(
                        new[] { later, first, middle },
                        AdoptionSource
                    )
                )
            );

            ResolvedOpResult<ConditionCleanupOutcome> cleanup = RequireResolved(
                await dispatcher.Dispatch(
                    new CleanupConditionsFromSourceOp(source, ConditionCleanupKind.Remove)
                )
            );

            Assert.That(
                cleanup.Value.Affected,
                Is.EqualTo(new[] { first.Effect.Id, middle.Effect.Id, later.Effect.Id })
            );
            Assert.That(store.Snapshot.ActiveEffects, Is.Empty);
        }

        [Test]
        public async Task SourceCleanupUsesStableOrderAndExposesLowerSlowedSource()
        {
            InMemoryRulesStore store = CreateStoreWithRegisteredCreatures();
            RuleDispatcher dispatcher = CreateDispatcher(store);
            ResolvedOpResult<ConditionApplicationOutcome> low = RequireResolved(
                await dispatcher.Dispatch(
                    Apply("Slowed", "low-source", new SlowedConditionState(1))
                )
            );
            ResolvedOpResult<ConditionApplicationOutcome> highFirst = RequireResolved(
                await dispatcher.Dispatch(
                    Apply("Slowed", "high-source", new SlowedConditionState(2))
                )
            );
            Assert.That(
                ConditionSelectors.TryGetSlowed(store.Snapshot, Owner, out var selected),
                Is.True
            );
            Assert.That(selected.State.Value, Is.EqualTo(2), "Slowed sources must not sum.");
            ResolvedOpResult<ConditionApplicationOutcome> highSecond = RequireResolved(
                await dispatcher.Dispatch(
                    Apply("Slowed", "high-source", new SlowedConditionState(3))
                )
            );

            Assert.That(
                ConditionSelectors.TryGetSlowed(store.Snapshot, Owner, out selected),
                Is.True
            );
            Assert.That(selected.State.Value, Is.EqualTo(3));

            ResolvedOpResult<ConditionCleanupOutcome> cleanup = RequireResolved(
                await dispatcher.Dispatch(
                    new CleanupConditionsFromSourceOp(
                        Owner,
                        ConditionRuleDefinitions.Slowed,
                        RuleSource.FromSlug("high-source"),
                        ConditionCleanupKind.Remove
                    )
                )
            );

            Assert.That(
                cleanup.Value.Affected,
                Is.EqualTo(new[] { highFirst.Value.EffectId, highSecond.Value.EffectId })
            );
            Assert.That(
                ConditionSelectors.TryGetSlowed(store.Snapshot, Owner, out selected),
                Is.True
            );
            Assert.That(selected.EffectId, Is.EqualTo(low.Value.EffectId));
            Assert.That(selected.State.Value, Is.EqualTo(1));
        }

        [Test]
        public void QuickenedAllowanceSelectorUnionsSourcesAndUnrestrictedDominates()
        {
            ActiveEffectRegistration stride = Registration(
                "quickened-stride",
                Owner,
                ConditionRuleDefinitions.Quickened,
                RuleSource.FromSlug("stride-source"),
                new QuickenedConditionState(new[] { new ActionDefinitionId("stride") }),
                2
            );
            ActiveEffectRegistration strike = Registration(
                "quickened-strike",
                Owner,
                ConditionRuleDefinitions.Quickened,
                RuleSource.FromSlug("strike-source"),
                new QuickenedConditionState(new[] { new ActionDefinitionId("strike") }),
                1
            );
            ActiveEffectRegistration unrestricted = Registration(
                "quickened-unrestricted",
                Owner,
                ConditionRuleDefinitions.Quickened,
                RuleSource.FromSlug("unrestricted-source"),
                QuickenedConditionState.Unrestricted,
                3
            );
            RulesStateSeed restrictedSeed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Owner, new PlayerId("condition-owner-player")))
                .SeedPreparedInputs(Owner, PreparedCreatureInputs.Empty)
                .SeedActiveEffect(stride.Effect)
                .SeedRuleBinding(stride.Binding)
                .SeedActiveEffect(strike.Effect)
                .SeedRuleBinding(strike.Binding);

            Assert.That(
                ConditionSelectors.GetQuickenedAllowance(new InMemoryRulesStore().Snapshot, Owner),
                Is.SameAs(ActionAllowance.None)
            );
            Assert.That(
                ConditionSelectors.GetQuickenedAllowance(
                    new InMemoryRulesStore(restrictedSeed).Snapshot,
                    Owner
                ),
                Is.EqualTo(
                    ActionAllowance.Restricted(
                        new[] { new ActionDefinitionId("stride"), new ActionDefinitionId("strike") }
                    )
                )
            );

            restrictedSeed
                .SeedActiveEffect(unrestricted.Effect)
                .SeedRuleBinding(unrestricted.Binding);
            Assert.That(
                ConditionSelectors.GetQuickenedAllowance(
                    new InMemoryRulesStore(restrictedSeed).Snapshot,
                    Owner
                ),
                Is.SameAs(ActionAllowance.Unrestricted)
            );
        }

        [Test]
        public async Task StunnedQuickenedAndMarkerSelectorsComposeActiveSources()
        {
            InMemoryRulesStore store = CreateStoreWithRegisteredCreatures();
            RuleDispatcher dispatcher = CreateDispatcher(store);
            await dispatcher.Dispatch(
                Apply("Stunned", "valued-one", new ValuedStunnedConditionState(1))
            );
            await dispatcher.Dispatch(
                Apply("Stunned", "valued-three", new ValuedStunnedConditionState(3))
            );

            Assert.That(
                ConditionSelectors.TryGetStunned(store.Snapshot, Owner, out var stunned),
                Is.True
            );
            Assert.That(stunned.State, Is.TypeOf<ValuedStunnedConditionState>());
            Assert.That(((ValuedStunnedConditionState)stunned.State).Value, Is.EqualTo(3));

            await dispatcher.Dispatch(
                Apply("Stunned", "duration", DurationOnlyStunnedConditionState.Instance)
            );
            await dispatcher.Dispatch(
                Apply(
                    "Quickened",
                    "stride-source",
                    new QuickenedConditionState(new[] { new ActionDefinitionId("stride") })
                )
            );
            await dispatcher.Dispatch(
                Apply(
                    "Quickened",
                    "strike-source",
                    new QuickenedConditionState(new[] { new ActionDefinitionId("strike") })
                )
            );
            await dispatcher.Dispatch(
                Apply("Fatigued", "fatigued-source", ConditionMarkerState.Instance)
            );
            await dispatcher.Dispatch(
                Apply("Encumbered", "encumbered-source", ConditionMarkerState.Instance)
            );

            ConditionSelectors.TryGetStunned(store.Snapshot, Owner, out stunned);
            ActionAllowance restricted = ConditionSelectors.GetQuickenedAllowance(
                store.Snapshot,
                Owner
            );
            Assert.That(stunned.State, Is.TypeOf<DurationOnlyStunnedConditionState>());
            Assert.That(restricted.IsUnrestricted, Is.False);
            Assert.That(
                restricted.AllowedActions,
                Is.EqualTo(
                    new[] { new ActionDefinitionId("stride"), new ActionDefinitionId("strike") }
                )
            );
            Assert.That(
                ConditionSelectors.HasMarker(
                    store.Snapshot,
                    Owner,
                    ConditionRuleDefinitions.Fatigued
                ),
                Is.True
            );
            Assert.That(
                ConditionSelectors.HasMarker(
                    store.Snapshot,
                    Owner,
                    ConditionRuleDefinitions.Encumbered
                ),
                Is.True
            );

            await dispatcher.Dispatch(
                Apply("Quickened", "unrestricted-source", QuickenedConditionState.Unrestricted)
            );
            Assert.That(
                ConditionSelectors.GetQuickenedAllowance(store.Snapshot, Owner).IsUnrestricted,
                Is.True
            );
        }

        [Test]
        public async Task MultipleOffGuardSourcesResolveOnePenaltyUntilLastSourceExpires()
        {
            InMemoryRulesStore store = CreateStoreWithRegisteredCreatures();
            RuleDispatcher dispatcher = CreateDispatcher(store);
            await dispatcher.Dispatch(
                Apply("Off-Guard", "first-off-guard", ConditionMarkerState.Instance)
            );
            await dispatcher.Dispatch(
                Apply("Off-Guard", "second-off-guard", ConditionMarkerState.Instance)
            );

            ModifierCollection both = RequireResolved(
                await dispatcher.Dispatch(new DefenseWorkflowOp(Owner))
            ).Value;
            Assert.That(both.Total, Is.EqualTo(18));
            Assert.That(both.Applied.Count(modifier => modifier.Value == -2), Is.EqualTo(1));

            await dispatcher.Dispatch(
                new CleanupConditionsFromSourceOp(
                    Owner,
                    ConditionRuleDefinitions.OffGuard,
                    RuleSource.FromSlug("first-off-guard"),
                    ConditionCleanupKind.Expire
                )
            );
            Assert.That(
                RequireResolved(
                    await dispatcher.Dispatch(new DefenseWorkflowOp(Owner))
                ).Value.Total,
                Is.EqualTo(18)
            );

            await dispatcher.Dispatch(
                new CleanupConditionsFromSourceOp(
                    Owner,
                    ConditionRuleDefinitions.OffGuard,
                    RuleSource.FromSlug("second-off-guard"),
                    ConditionCleanupKind.Expire
                )
            );
            Assert.That(
                RequireResolved(
                    await dispatcher.Dispatch(new DefenseWorkflowOp(Owner))
                ).Value.Total,
                Is.EqualTo(20)
            );
        }

        [Test]
        public async Task ContextualFlankingModifierDoesNotPersistACondition()
        {
            InMemoryRulesStore store = new InMemoryRulesStore();
            RuleDispatcher dispatcher = CreateDispatcher(store);

            ModifierCollection result = RequireResolved(
                await dispatcher.Dispatch(new DefenseWorkflowOp(Owner, includeFlanking: true))
            ).Value;

            Assert.That(result.Total, Is.EqualTo(18));
            Assert.That(
                ConditionSelectors.HasMarker(
                    store.Snapshot,
                    Owner,
                    ConditionRuleDefinitions.OffGuard
                ),
                Is.False
            );
            Assert.That(store.Snapshot.ActiveEffects, Is.Empty);
        }

        private static ApplyConditionOp Apply(
            string condition,
            string source,
            IEffectState state
        ) =>
            new ApplyConditionOp(
                condition,
                Owner,
                SourceCreature,
                RuleSource.FromSlug(source),
                EffectDuration.Indefinite,
                state
            );

        private static ActiveEffectRegistration Registration(
            string identity,
            CreatureId owner,
            RuleDefinitionId definition,
            RuleSource source,
            IEffectState state,
            long order,
            bool isEnabled = true
        )
        {
            ActiveEffectInstance effect = new ActiveEffectInstance(
                new ActiveEffectId($"effect-{identity}"),
                definition,
                SourceCreature,
                source,
                EffectDuration.Indefinite,
                state
            );
            return new ActiveEffectRegistration(
                effect,
                new ActiveRuleBinding(
                    new BindingId($"binding-{identity}"),
                    definition,
                    owner,
                    effect.Id,
                    source,
                    order,
                    isEnabled
                )
            );
        }

        private static RuleDispatcher CreateDispatcher(InMemoryRulesStore store)
        {
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder();
            ConditionRuleDefinitions.DefineAll(registryBuilder);
            RuleRegistry registry = registryBuilder.Build();
            return new RuleDispatcherBuilder(store)
                .RegisterHandler<DefenseWorkflowOp, ModifierCollection>(
                    new DefenseWorkflowHandler()
                )
                .UseCheckResolution()
                .UseActiveEffectRules(registry)
                .UseConditionRules(registry)
                .Build();
        }

        private static InMemoryRulesStore CreateStoreWithRegisteredCreatures(
            params CreatureId[] additionalCreatures
        )
        {
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Owner, new PlayerId("condition-owner-player")))
                .SeedCreature(
                    new CreatureState(SourceCreature, new PlayerId("condition-source-player"))
                )
                .SeedPreparedInputs(Owner, PreparedCreatureInputs.Empty)
                .SeedPreparedInputs(SourceCreature, PreparedCreatureInputs.Empty);
            for (int index = 0; index < additionalCreatures.Length; index++)
            {
                CreatureId creature = additionalCreatures[index];
                seed.SeedCreature(
                        new CreatureState(
                            creature,
                            new PlayerId($"additional-condition-owner-{index}")
                        )
                    )
                    .SeedPreparedInputs(creature, PreparedCreatureInputs.Empty);
            }
            return new InMemoryRulesStore(seed);
        }

        private static PreparedCreatureInputs PreparedWithConditionImmunity(string condition) =>
            new PreparedCreatureInputs(
                0,
                default,
                Array.Empty<KeyValuePair<string, int>>(),
                Array.Empty<string>(),
                string.Empty,
                Array.Empty<string>(),
                Array.Empty<PreparedDefenseDescriptor>(),
                Array.Empty<PreparedDefenseDescriptor>(),
                new[] { new PreparedImmunityDescriptor(condition, PreparedImmunityKind.Condition) },
                Array.Empty<string>()
            );

        private static ResolvedOpResult<TResult> RequireResolved<TResult>(OpResult<TResult> result)
        {
            Assert.That(result, Is.TypeOf<ResolvedOpResult<TResult>>());
            return (ResolvedOpResult<TResult>)result;
        }

        private sealed class DefenseWorkflowOp : IRuleOp<ModifierCollection>
        {
            internal DefenseWorkflowOp(CreatureId defender, bool includeFlanking = false)
            {
                Defender = defender;
                IncludeFlanking = includeFlanking;
            }

            internal CreatureId Defender { get; }
            internal bool IncludeFlanking { get; }
        }

        private sealed class CountingConditionCreatedObserver
            : IFactObserver<ActiveEffectCreatedFact>
        {
            internal int Count { get; private set; }

            public ValueTask OnFactCommitted(
                ActiveEffectCreatedFact fact,
                RulesSnapshot currentSnapshot
            )
            {
                if (ConditionRuleDefinitions.IsConditionDefinition(fact.DefinitionId))
                    Count++;
                return default;
            }
        }

        private sealed class DefenseWorkflowHandler
            : IOpHandler<DefenseWorkflowOp, ModifierCollection>
        {
            public async ValueTask<ModifierCollection> Handle(
                OpFrame<DefenseWorkflowOp> frame,
                OpHandlerContext context
            )
            {
                List<Modifier> modifiers = new List<Modifier>
                {
                    Modifier.Untyped(
                        20,
                        RuleSource.FromSlug("base-armor-class"),
                        Statistic.ArmorClass
                    ),
                };
                if (frame.Op.IncludeFlanking)
                    modifiers.Add(
                        new Modifier(
                            -2,
                            ModifierType.Circumstance,
                            RuleSource.FromSlug("flanking"),
                            Statistic.ArmorClass
                        )
                    );
                return RequireResolved(
                    await context.Dispatch(
                        new CollectDefenseModifiersOp(
                            frame.Op.Defender,
                            modifiers,
                            CheckSource.From(frame.Id)
                        )
                    )
                ).Value;
            }
        }
    }
}
