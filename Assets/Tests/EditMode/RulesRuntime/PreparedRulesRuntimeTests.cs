using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class PreparedRulesRuntimeTests
    {
        private static readonly CreatureId Owner = new("prepared-owner");
        private static readonly RuleDefinitionId Definition = new("prepared:test:owned");
        private static readonly RuleSource Source = RuleSource.FromSlug("test-source");

        [Test]
        public void PreparedContributionCompositionRejectsNullBuilder()
        {
            RuleDispatcherBuilder builder = null;

            ArgumentNullException error = Assert.Throws<ArgumentNullException>(() =>
                builder.UsePreparedContributions()
            );

            Assert.That(error.ParamName, Is.EqualTo(nameof(builder)));
        }

        [Test]
        public void RegistryDeduplicatesIdenticalSpecsAndRejectsConflicts()
        {
            RuleRegistryBuilder builder = new();
            PreparedRuleDefinitionSpec first = new(Definition, Source, "owned", "fixture");
            Assert.That(builder.Define(first), Is.SameAs(builder.Define(first)));
            Assert.Throws<InvalidOperationException>(() =>
                builder.Define(new PreparedRuleDefinitionSpec(Definition, Source, "owned", "other"))
            );
            Assert.Throws<InvalidOperationException>(() =>
                builder.Define(
                    new PreparedRuleDefinitionSpec(
                        Definition,
                        Source,
                        "owned",
                        "fixture",
                        "fixture",
                        new[]
                        {
                            new PreparedModifierSpec(
                                Definition,
                                "ac",
                                "conflict",
                                1,
                                "item",
                                string.Empty,
                                PreparedPredicate.Always
                            ),
                        },
                        Array.Empty<PreparedAdjustmentSpec>(),
                        Array.Empty<PreparedDamageDiceSpec>(),
                        Array.Empty<PreparedItemAlterationSpec>()
                    )
                )
            );
            Assert.That(builder.Build().Definitions, Has.Count.EqualTo(1));
        }

        [Test]
        public void PreparedInputsDefensivelyCopyMutableSources()
        {
            List<KeyValuePair<string, int>> skills = new()
            {
                new KeyValuePair<string, int>("stealth", 1),
            };
            List<string> equipment = new() { "Leather Armor" };
            List<PreparedBoundOption> options = new()
            {
                new PreparedBoundOption(Definition, "feat:test-source", PreparedPredicate.Always),
            };
            PreparedCreatureInputs inputs = new(
                1,
                default,
                skills,
                equipment,
                "light",
                Array.Empty<string>(),
                Array.Empty<PreparedDefenseDescriptor>(),
                Array.Empty<PreparedDefenseDescriptor>(),
                Array.Empty<PreparedImmunityDescriptor>(),
                Array.Empty<string>(),
                options,
                Array.Empty<KeyValuePair<string, int>>()
            );

            skills.Clear();
            equipment.Clear();
            options.Clear();

            Assert.That(inputs.SkillRanks["stealth"], Is.EqualTo(1));
            Assert.That(inputs.Equipment, Is.EqualTo(new[] { "leather-armor" }));
            Assert.That(inputs.BoundOptions, Has.Count.EqualTo(1));
        }

        [Test]
        public void EnableStatelessRuleBindingOpReportsBindingParameterName()
        {
            ArgumentException error = Assert.Throws<ArgumentException>(() =>
                new EnableStatelessRuleBindingOp(default, 0, Source)
            );

            Assert.That(error.ParamName, Is.EqualTo("binding"));
        }

        [Test]
        public void EnableStatelessRuleBindingOpReportsExpectedCreationOrderParameterName()
        {
            ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new EnableStatelessRuleBindingOp(new BindingId("binding"), -1, Source)
            );

            Assert.That(error.ParamName, Is.EqualTo("expectedCreationOrder"));
        }

        [Test]
        public void EnableStatelessRuleBindingOpReportsSourceParameterName()
        {
            ArgumentException error = Assert.Throws<ArgumentException>(() =>
                new EnableStatelessRuleBindingOp(new BindingId("binding"), 0, default)
            );

            Assert.That(error.ParamName, Is.EqualTo("source"));
        }

        [Test]
        public void StatelessLifecycleIsOptimisticImmutableAndRejectsEffectBindings()
        {
            RuleRegistryBuilder registryBuilder = new();
            registryBuilder.Define(Definition);
            RuleRegistry registry = registryBuilder.Build();
            InMemoryRulesStore store = new();
            ActiveRuleBinding binding = new(
                new BindingId("stateless-binding"),
                Definition,
                Owner,
                null,
                Source,
                12
            );
            RulesSnapshot empty = store.Snapshot;

            ReductionResult<StatelessRuleBindingCreatedOutcome> created = store.Reduce(
                Context(new CreateStatelessRuleBindingOp(binding)),
                new CreateStatelessRuleBindingReducer(registry)
            );
            Assert.That(created.IsAccepted, Is.True);
            Assert.That(created.Facts.Single(), Is.TypeOf<StatelessRuleBindingCreatedFact>());
            Assert.That(empty.RuleBindings, Is.Empty);
            Assert.That(
                created.Snapshot.StatelessRuleBindingGenerations[binding.Id],
                Is.EqualTo(12)
            );

            ReductionResult<StatelessRuleBindingEnabledOutcome> sourceMismatch = store.Reduce(
                Context(
                    new DisableStatelessRuleBindingOp(
                        binding.Id,
                        binding.CreationOrder,
                        RuleSource.FromSlug("other-source")
                    )
                ),
                new DisableStatelessRuleBindingReducer()
            );
            Assert.That(sourceMismatch.IsRejected, Is.True);
            Assert.That(sourceMismatch.Facts, Is.Empty);

            ReductionResult<StatelessRuleBindingEnabledOutcome> disabled = store.Reduce(
                Context(new DisableStatelessRuleBindingOp(binding.Id, 12, Source)),
                new DisableStatelessRuleBindingReducer()
            );
            Assert.That(disabled.IsAccepted, Is.True);
            Assert.That(
                disabled.Facts.Single(),
                Is.TypeOf<StatelessRuleBindingEnabledChangedFact>()
            );
            StatelessRuleBindingEnabledChangedFact disabledFact =
                (StatelessRuleBindingEnabledChangedFact)disabled.Facts.Single();
            Assert.That(disabledFact.Binding.Id, Is.EqualTo(binding.Id));
            Assert.That(disabledFact.Binding.Source, Is.EqualTo(Source));
            Assert.That(disabledFact.Binding.CreationOrder, Is.EqualTo(12));
            Assert.That(disabledFact.Binding.IsEnabled, Is.False);
            Assert.That(disabled.Snapshot.RuleBindings[binding.Id].IsEnabled, Is.False);
            Assert.That(
                store
                    .Reduce(
                        Context(new EnableStatelessRuleBindingOp(binding.Id, 11, Source)),
                        new EnableStatelessRuleBindingReducer()
                    )
                    .IsRejected,
                Is.True
            );
            Assert.That(
                store
                    .Reduce(
                        Context(new EnableStatelessRuleBindingOp(binding.Id, 12, Source)),
                        new EnableStatelessRuleBindingReducer()
                    )
                    .IsAccepted,
                Is.True
            );
            ReductionResult<StatelessRuleBindingRemovedOutcome> removed = store.Reduce(
                Context(new RemoveStatelessRuleBindingOp(binding.Id, 12, Source)),
                new RemoveStatelessRuleBindingReducer()
            );
            Assert.That(removed.IsAccepted, Is.True);
            StatelessRuleBindingRemovedFact removedFact = (StatelessRuleBindingRemovedFact)
                removed.Facts.Single();
            Assert.That(removedFact.Binding.Source, Is.EqualTo(Source));
            Assert.That(removedFact.Binding.CreationOrder, Is.EqualTo(12));
            Assert.That(store.Snapshot.RuleBindings, Is.Empty);
            Assert.That(store.Snapshot.StatelessRuleBindingGenerations[binding.Id], Is.EqualTo(12));

            foreach (long staleGeneration in new long[] { 11, 12 })
            {
                ActiveRuleBinding staleReplacement = new ActiveRuleBinding(
                    binding.Id,
                    Definition,
                    Owner,
                    null,
                    Source,
                    staleGeneration
                );
                ReductionResult<StatelessRuleBindingCreatedOutcome> staleCreate = store.Reduce(
                    Context(new CreateStatelessRuleBindingOp(staleReplacement)),
                    new CreateStatelessRuleBindingReducer(registry)
                );
                Assert.That(staleCreate.IsRejected, Is.True);
                Assert.That(staleCreate.Facts, Is.Empty);
            }

            ActiveRuleBinding replacement = new ActiveRuleBinding(
                binding.Id,
                Definition,
                Owner,
                null,
                Source,
                13
            );
            ReductionResult<StatelessRuleBindingCreatedOutcome> recreated = store.Reduce(
                Context(new CreateStatelessRuleBindingOp(replacement)),
                new CreateStatelessRuleBindingReducer(registry)
            );
            Assert.That(recreated.IsAccepted, Is.True);
            StatelessRuleBindingCreatedFact recreatedFact = (StatelessRuleBindingCreatedFact)
                recreated.Facts.Single();
            Assert.That(recreatedFact.Binding, Is.EqualTo(replacement));
            Assert.That(recreatedFact.Binding.CreationOrder, Is.EqualTo(13));
            Assert.That(recreatedFact.Binding, Is.Not.EqualTo(removedFact.Binding));
            Assert.That(
                recreated.Snapshot.StatelessRuleBindingGenerations[binding.Id],
                Is.EqualTo(13)
            );

            Assert.That(
                store
                    .Reduce(
                        Context(new DisableStatelessRuleBindingOp(binding.Id, 13, Source)),
                        new DisableStatelessRuleBindingReducer()
                    )
                    .IsAccepted,
                Is.True
            );
            Assert.That(
                store
                    .Reduce(
                        Context(new EnableStatelessRuleBindingOp(binding.Id, 12, Source)),
                        new EnableStatelessRuleBindingReducer()
                    )
                    .IsRejected,
                Is.True
            );
            Assert.That(store.Snapshot.RuleBindings[binding.Id].IsEnabled, Is.False);
            Assert.That(
                store
                    .Reduce(
                        Context(new EnableStatelessRuleBindingOp(binding.Id, 13, Source)),
                        new EnableStatelessRuleBindingReducer()
                    )
                    .IsAccepted,
                Is.True
            );
            Assert.That(
                store
                    .Reduce(
                        Context(new DisableStatelessRuleBindingOp(binding.Id, 12, Source)),
                        new DisableStatelessRuleBindingReducer()
                    )
                    .IsRejected,
                Is.True
            );
            Assert.That(
                store
                    .Reduce(
                        Context(new RemoveStatelessRuleBindingOp(binding.Id, 12, Source)),
                        new RemoveStatelessRuleBindingReducer()
                    )
                    .IsRejected,
                Is.True
            );
            Assert.That(store.Snapshot.RuleBindings[binding.Id], Is.EqualTo(replacement));

            ActiveRuleBinding effectBacked = new(
                new BindingId("effect-binding"),
                Definition,
                Owner,
                new ActiveEffectId("effect"),
                Source,
                13
            );
            Assert.That(
                store
                    .Reduce(
                        Context(new CreateStatelessRuleBindingOp(effectBacked)),
                        new CreateStatelessRuleBindingReducer(registry)
                    )
                    .IsRejected,
                Is.True
            );
            InMemoryRulesStore effectStore = new(
                new RulesStateSeed().SeedRuleBinding(effectBacked)
            );
            ReductionResult<StatelessRuleBindingEnabledOutcome> effectMutation = effectStore.Reduce(
                Context(
                    new DisableStatelessRuleBindingOp(
                        effectBacked.Id,
                        effectBacked.CreationOrder,
                        effectBacked.Source
                    )
                ),
                new DisableStatelessRuleBindingReducer()
            );
            Assert.That(effectMutation.IsRejected, Is.True);
            Assert.That(effectMutation.Facts, Is.Empty);
            Assert.That(
                effectStore.Snapshot.StatelessRuleBindingGenerations.Contains(effectBacked.Id),
                Is.False
            );
        }

        [Test]
        public void StatelessGenerationHistorySeedsWithoutParticipatingTombstone()
        {
            BindingId id = new("seeded-generation");
            ActiveRuleBinding binding = new(id, Definition, Owner, null, Source, 7);
            RulesSnapshot active = new InMemoryRulesStore(
                new RulesStateSeed().SeedRuleBinding(binding)
            ).Snapshot;
            RulesSnapshot removed = new InMemoryRulesStore(
                new RulesStateSeed().SeedStatelessRuleBindingGeneration(id, 7)
            ).Snapshot;

            Assert.That(active.RuleBindings[id], Is.EqualTo(binding));
            Assert.That(active.StatelessRuleBindingGenerations[id], Is.EqualTo(7));
            Assert.That(removed.RuleBindings, Is.Empty);
            Assert.That(removed.StatelessRuleBindingGenerations[id], Is.EqualTo(7));
            Assert.Throws<ArgumentException>(() =>
                new RulesStateSeed()
                    .SeedStatelessRuleBindingGeneration(id, 7)
                    .SeedStatelessRuleBindingGeneration(id, 6)
            );
        }

        [Test]
        public void PredicateUsesOwnedStaticDynamicAndCurrentContextFacts()
        {
            PreparedRulePackage package = CreatePackage();
            ActiveRuleBinding owned = package.Bindings.Single().Create(Owner);
            ActiveRuleBinding effect = new(
                new BindingId("rage-effect-binding"),
                new RuleDefinitionId("rage-effect"),
                Owner,
                new ActiveEffectId("rage-effect"),
                RuleSource.FromSlug("rage"),
                2
            );
            RulesSnapshot snapshot = new InMemoryRulesStore(
                new RulesStateSeed()
                    .SeedPreparedInputs(Owner, package.Inputs)
                    .SeedRuleBinding(owned)
                    .SeedRuleBinding(effect)
            ).Snapshot;
            PreparedPredicateContext context = new(
                snapshot,
                Owner,
                new[] { "target:condition:off-guard", "item:trait:finesse" }
            );
            PreparedPredicate predicate = new PreparedAllPredicate(
                new PreparedPredicate[]
                {
                    new PreparedOptionPredicate("feat:test-source"),
                    new PreparedOptionPredicate("self:effect:rage"),
                    new PreparedOptionPredicate("target:condition:off-guard"),
                    new PreparedOptionPredicate("item:trait:finesse"),
                    new PreparedOptionPredicate("self:trait:undead"),
                    new PreparedOptionPredicate("self:weakness:slashing"),
                    new PreparedOptionPredicate("self:resistance:fire"),
                    new PreparedOptionPredicate("self:immunity:death-effects"),
                    new PreparedNumericAtLeastPredicate(
                        PreparedNumericFactKind.Level,
                        string.Empty,
                        7
                    ),
                    new PreparedNumericAtLeastPredicate(
                        PreparedNumericFactKind.SkillRank,
                        "stealth",
                        2
                    ),
                }
            );
            Assert.That(predicate.Evaluate(context), Is.True);
        }

        [Test]
        public void BoundOptionPredicatesTrackBindingChangesAndSuppressCyclesDeterministically()
        {
            RuleDefinitionId prerequisiteDefinition = new("prepared:prerequisite");
            RuleDefinitionId dependentDefinition = new("prepared:dependent");
            RuleDefinitionId cycleADefinition = new("prepared:cycle-a");
            RuleDefinitionId cycleBDefinition = new("prepared:cycle-b");
            PreparedCreatureInputs inputs = new PreparedCreatureInputs(
                1,
                default,
                Array.Empty<KeyValuePair<string, int>>(),
                Array.Empty<string>(),
                string.Empty,
                Array.Empty<string>(),
                Array.Empty<PreparedDefenseDescriptor>(),
                Array.Empty<PreparedDefenseDescriptor>(),
                Array.Empty<PreparedImmunityDescriptor>(),
                Array.Empty<string>(),
                new[]
                {
                    new PreparedBoundOption(
                        dependentDefinition,
                        "feature:dependent",
                        new PreparedOptionPredicate("feature:prerequisite")
                    ),
                    new PreparedBoundOption(
                        prerequisiteDefinition,
                        "feature:prerequisite",
                        PreparedPredicate.Always
                    ),
                    new PreparedBoundOption(
                        cycleBDefinition,
                        "feature:cycle-b",
                        new PreparedOptionPredicate("feature:cycle-a")
                    ),
                    new PreparedBoundOption(
                        cycleADefinition,
                        "feature:cycle-a",
                        new PreparedOptionPredicate("feature:cycle-b")
                    ),
                },
                Array.Empty<KeyValuePair<string, int>>()
            );
            ActiveRuleBinding prerequisite = Binding("prerequisite", prerequisiteDefinition, 1);
            ActiveRuleBinding dependent = Binding("dependent", dependentDefinition, 2);
            ActiveRuleBinding cycleA = Binding("cycle-a", cycleADefinition, 3);
            ActiveRuleBinding cycleB = Binding("cycle-b", cycleBDefinition, 4);
            InMemoryRulesStore store = new InMemoryRulesStore(
                new RulesStateSeed()
                    .SeedPreparedInputs(Owner, inputs)
                    .SeedRuleBinding(prerequisite)
                    .SeedRuleBinding(dependent)
                    .SeedRuleBinding(cycleA)
                    .SeedRuleBinding(cycleB)
            );

            PreparedPredicateContext enabled = new PreparedPredicateContext(
                store.Snapshot,
                Owner,
                Array.Empty<string>()
            );
            Assert.That(enabled.HasOption("feature:prerequisite"), Is.True);
            Assert.That(enabled.HasOption("feature:dependent"), Is.True);
            Assert.That(enabled.HasOption("feature:cycle-a"), Is.False);
            Assert.That(enabled.HasOption("feature:cycle-b"), Is.False);

            Assert.That(
                store
                    .Reduce(
                        Context(
                            new DisableStatelessRuleBindingOp(
                                prerequisite.Id,
                                prerequisite.CreationOrder,
                                Source
                            )
                        ),
                        new DisableStatelessRuleBindingReducer()
                    )
                    .IsAccepted,
                Is.True
            );
            PreparedPredicateContext disabled = new PreparedPredicateContext(
                store.Snapshot,
                Owner,
                Array.Empty<string>()
            );
            Assert.That(disabled.HasOption("feature:prerequisite"), Is.False);
            Assert.That(disabled.HasOption("feature:dependent"), Is.False);

            Assert.That(
                store
                    .Reduce(
                        Context(
                            new EnableStatelessRuleBindingOp(
                                prerequisite.Id,
                                prerequisite.CreationOrder,
                                Source
                            )
                        ),
                        new EnableStatelessRuleBindingReducer()
                    )
                    .IsAccepted,
                Is.True
            );
            PreparedPredicateContext reenabled = new PreparedPredicateContext(
                store.Snapshot,
                Owner,
                Array.Empty<string>()
            );
            Assert.That(reenabled.HasOption("feature:dependent"), Is.True);

            ActiveRuleBinding Binding(string id, RuleDefinitionId definition, long order) =>
                new ActiveRuleBinding(new BindingId(id), definition, Owner, null, Source, order);
        }

        [Test]
        public async Task CollectorObservesNextSnapshotWithoutRegistryRebuild()
        {
            PreparedRulePackage package = CreatePackage();
            ActiveRuleBinding binding = package.Bindings.Single().Create(Owner);
            RuleRegistryBuilder registryBuilder = new();
            registryBuilder.Define(package.Definitions.Single());
            RuleRegistry registry = registryBuilder.Build();
            InMemoryRulesStore store = new(
                new RulesStateSeed()
                    .SeedPreparedInputs(Owner, package.Inputs)
                    .SeedRuleBinding(binding)
            );
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(store)
                .UseRuleRegistry(registry)
                .UseStatelessRuleBindingRules(registry)
                .UsePreparedContributions()
                .Build();
            PreparedContributionContext contributionContext = new(
                string.Empty,
                string.Empty,
                false,
                0,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>()
            );
            ResolvedOpResult<IReadOnlyList<PreparedModifierValue>> enabled =
                (ResolvedOpResult<IReadOnlyList<PreparedModifierValue>>)
                    await dispatcher.Dispatch(
                        new CollectPreparedModifiersOp(Owner, "ac", contributionContext)
                    );
            Assert.That(enabled.Value, Has.Count.EqualTo(1));

            ReductionResult<StatelessRuleBindingEnabledOutcome> disabled = store.Reduce(
                Context(
                    new DisableStatelessRuleBindingOp(binding.Id, binding.CreationOrder, Source)
                ),
                new DisableStatelessRuleBindingReducer()
            );
            Assert.That(disabled.IsAccepted, Is.True);
            ResolvedOpResult<IReadOnlyList<PreparedModifierValue>> nextSnapshot =
                (ResolvedOpResult<IReadOnlyList<PreparedModifierValue>>)
                    await dispatcher.Dispatch(
                        new CollectPreparedModifiersOp(Owner, "ac", contributionContext)
                    );
            Assert.That(nextSnapshot.Value, Is.Empty);
            Assert.That(registry.Definitions, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ModifierAdjustmentsUseCompiledPriorityAcrossBindingRecreation()
        {
            RuleDefinitionId baseId = new("prepared:test:rage-base");
            RuleDefinitionId furyId = new("prepared:test:fury-upgrade");
            RuleDefinitionId agileId = new("prepared:test:rage-agile");
            ActiveRuleBinding baseBinding = PreparedBinding("rage-base", baseId, 30);
            ActiveRuleBinding furyBinding = PreparedBinding("fury-upgrade", furyId, 10);
            ActiveRuleBinding agileBinding = PreparedBinding("rage-agile", agileId, 20);
            (InMemoryRulesStore store, RuleDispatcher dispatcher, RuleRegistry registry) =
                CreateAdjustmentRuntime(
                    new[]
                    {
                        AdjustmentDefinition(agileId, "multiply", 0.5f, 95),
                        ModifierDefinition(baseId, 2),
                        AdjustmentDefinition(furyId, "upgrade", 3, 0),
                    },
                    new[] { baseBinding, furyBinding, agileBinding }
                );

            Assert.That(await RageValue(dispatcher), Is.EqualTo(1));
            Assert.That(
                store
                    .Reduce(
                        Context(
                            new RemoveStatelessRuleBindingOp(
                                agileBinding.Id,
                                agileBinding.CreationOrder,
                                agileBinding.Source
                            )
                        ),
                        new RemoveStatelessRuleBindingReducer()
                    )
                    .IsAccepted,
                Is.True
            );
            Assert.That(await RageValue(dispatcher), Is.EqualTo(3));

            ActiveRuleBinding recreated = PreparedBinding("rage-agile", agileId, 1000);
            Assert.That(
                store
                    .Reduce(
                        Context(new CreateStatelessRuleBindingOp(recreated)),
                        new CreateStatelessRuleBindingReducer(registry)
                    )
                    .IsAccepted,
                Is.True
            );
            Assert.That(await RageValue(dispatcher), Is.EqualTo(1));
        }

        [Test]
        public async Task EqualPriorityAdjustmentsUseSemanticOrderAcrossCatalogAndBindingOrder()
        {
            int[] values = new int[2];
            for (int index = 0; index < values.Length; index++)
            {
                bool reversed = index == 1;
                RuleDefinitionId baseId = new("prepared:test:tie-base");
                RuleDefinitionId multiplyId = new("prepared:test:tie-multiply");
                RuleDefinitionId upgradeId = new("prepared:test:tie-upgrade");
                PreparedRuleDefinitionSpec baseDefinition = ModifierDefinition(baseId, 2);
                PreparedRuleDefinitionSpec multiplyDefinition = AdjustmentDefinition(
                    multiplyId,
                    "multiply",
                    0.5f,
                    10
                );
                PreparedRuleDefinitionSpec upgradeDefinition = AdjustmentDefinition(
                    upgradeId,
                    "upgrade",
                    3,
                    10
                );
                PreparedRuleDefinitionSpec[] definitions = reversed
                    ? new[] { upgradeDefinition, baseDefinition, multiplyDefinition }
                    : new[] { multiplyDefinition, baseDefinition, upgradeDefinition };
                ActiveRuleBinding[] bindings =
                {
                    PreparedBinding($"tie-base-{index}", baseId, 30),
                    PreparedBinding($"tie-multiply-{index}", multiplyId, reversed ? 20 : 10),
                    PreparedBinding($"tie-upgrade-{index}", upgradeId, reversed ? 10 : 20),
                };
                RuleDispatcher dispatcher = CreateAdjustmentRuntime(
                    definitions,
                    bindings
                ).Dispatcher;
                values[index] = await RageValue(dispatcher);
            }

            Assert.That(values, Is.EqualTo(new[] { 3, 3 }));
        }

        [Test]
        public void RulesRuntimeAssemblyHasNoUnityJsonOrMutablePreparedDependency()
        {
            string[] references = typeof(PreparedRulePackage)
                .Assembly.GetReferencedAssemblies()
                .Select(value => value.Name)
                .ToArray();
            Assert.That(references, Does.Not.Contain("UnityEngine"));
            Assert.That(references, Does.Not.Contain("Newtonsoft.Json"));
            Assert.That(
                typeof(PreparedRulePackage)
                    .Assembly.GetTypes()
                    .Any(type => type.Name == "PreparedCharacter"),
                Is.False
            );
        }

        private static PreparedRulePackage CreatePackage()
        {
            PreparedModifierSpec modifier = new(
                Definition,
                "ac",
                "fixture",
                1,
                "item",
                string.Empty,
                PreparedPredicate.Always
            );
            PreparedCreatureInputs inputs = new(
                7,
                new PreparedAbilityModifiers(1, 4, 2, 0, 1, 2),
                new[] { new KeyValuePair<string, int>("stealth", 2) },
                new[] { "leather-armor" },
                "light",
                new[] { "undead" },
                new[] { new PreparedDefenseDescriptor("slashing", 5) },
                new[] { new PreparedDefenseDescriptor("fire", 3) },
                new[]
                {
                    new PreparedImmunityDescriptor(
                        "death-effects",
                        PreparedImmunityKind.EffectTrait
                    ),
                },
                new[]
                {
                    "self:trait:undead",
                    "self:weakness:slashing",
                    "self:resistance:fire",
                    "self:immunity:death-effects",
                },
                new[]
                {
                    new PreparedBoundOption(
                        Definition,
                        "feat:test-source",
                        PreparedPredicate.Always
                    ),
                },
                Array.Empty<KeyValuePair<string, int>>()
            );
            PreparedRuleDefinitionSpec definition = new(
                Definition,
                Source,
                "owned",
                "fixture",
                "fixture",
                new[] { modifier },
                Array.Empty<PreparedAdjustmentSpec>(),
                Array.Empty<PreparedDamageDiceSpec>(),
                Array.Empty<PreparedItemAlterationSpec>()
            );
            return new PreparedRulePackage(
                inputs,
                new[] { definition },
                new[] { new PreparedBindingSeed("test:owned", Definition, Source, 1) },
                Array.Empty<PreparedUnsupportedDiagnostic>()
            );
        }

        private static PreparedRuleDefinitionSpec DefinitionWith(
            RuleDefinitionId id,
            IEnumerable<PreparedModifierSpec> modifiers,
            IEnumerable<PreparedAdjustmentSpec> adjustments
        ) =>
            new(
                id,
                Source,
                "fixture",
                id.Value,
                id.Value,
                modifiers,
                adjustments,
                Array.Empty<PreparedDamageDiceSpec>(),
                Array.Empty<PreparedItemAlterationSpec>()
            );

        private static PreparedRuleDefinitionSpec ModifierDefinition(
            RuleDefinitionId id,
            int value
        ) =>
            DefinitionWith(
                id,
                new[]
                {
                    new PreparedModifierSpec(
                        id,
                        "strike-damage",
                        "rage",
                        value,
                        string.Empty,
                        string.Empty,
                        PreparedPredicate.Always
                    ),
                },
                Array.Empty<PreparedAdjustmentSpec>()
            );

        private static PreparedRuleDefinitionSpec AdjustmentDefinition(
            RuleDefinitionId id,
            string mode,
            float value,
            int priority
        ) =>
            DefinitionWith(
                id,
                Array.Empty<PreparedModifierSpec>(),
                new[]
                {
                    new PreparedAdjustmentSpec(
                        id,
                        "strike-damage",
                        "rage",
                        mode,
                        value,
                        priority,
                        PreparedPredicate.Always
                    ),
                }
            );

        private static ActiveRuleBinding PreparedBinding(
            string id,
            RuleDefinitionId definition,
            long order
        ) => new(new BindingId(id), definition, Owner, null, Source, order);

        private static (
            InMemoryRulesStore Store,
            RuleDispatcher Dispatcher,
            RuleRegistry Registry
        ) CreateAdjustmentRuntime(
            IEnumerable<PreparedRuleDefinitionSpec> definitions,
            IEnumerable<ActiveRuleBinding> bindings
        )
        {
            RuleRegistryBuilder registryBuilder = new();
            foreach (PreparedRuleDefinitionSpec definition in definitions)
                registryBuilder.Define(definition);
            RuleRegistry registry = registryBuilder.Build();
            RulesStateSeed seed = new RulesStateSeed().SeedPreparedInputs(
                Owner,
                CreatePackage().Inputs
            );
            foreach (ActiveRuleBinding binding in bindings)
                seed.SeedRuleBinding(binding);
            InMemoryRulesStore store = new(seed);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(store)
                .UseRuleRegistry(registry)
                .UseStatelessRuleBindingRules(registry)
                .UsePreparedContributions()
                .Build();
            return (store, dispatcher, registry);
        }

        private static async Task<int> RageValue(RuleDispatcher dispatcher)
        {
            PreparedContributionContext contributionContext = new(
                "test-weapon",
                "martial",
                false,
                4,
                new[] { "agile" },
                Array.Empty<string>(),
                Array.Empty<string>()
            );
            OpResult<IReadOnlyList<PreparedModifierValue>> result = await dispatcher.Dispatch(
                new CollectPreparedModifiersOp(Owner, "strike-damage", contributionContext)
            );
            return ((ResolvedOpResult<IReadOnlyList<PreparedModifierValue>>)result)
                .Value.Single()
                .Value;
        }

        private static ReductionContext<TOp> Context<TOp>(TOp operation) =>
            new(operation, new OpId(2), new OpId(1), Source);
    }
}
