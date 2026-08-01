using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class PreparedRulesRuntimeTests
    {
        private static readonly CreatureId Owner = new("prepared-owner");
        private static readonly RuleDefinitionId Definition = new("prepared:test:owned");
        private static readonly RuleSource Source = RuleSource.FromSlug("test-source");

        [Test]
        public void RegistryDeduplicatesIdenticalSpecsAndRejectsConflicts()
        {
            RuleRegistryBuilder builder = new();
            PreparedRuleDefinitionSpec first = new(Definition, Source, "owned", "fixture");
            Assert.That(builder.Define(first), Is.SameAs(builder.Define(first)));
            Assert.Throws<InvalidOperationException>(() =>
                builder.Define(new PreparedRuleDefinitionSpec(Definition, Source, "owned", "other"))
            );
            Assert.That(builder.Build().Definitions, Has.Count.EqualTo(1));
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
            Assert.That(empty.RuleBindings, Is.Empty);

            ReductionResult<StatelessRuleBindingEnabledOutcome> disabled = store.Reduce(
                Context(new DisableStatelessRuleBindingOp(binding.Id, 12, Source)),
                new DisableStatelessRuleBindingReducer()
            );
            Assert.That(disabled.IsAccepted, Is.True);
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
            Assert.That(
                store
                    .Reduce(
                        Context(new RemoveStatelessRuleBindingOp(binding.Id, 12, Source)),
                        new RemoveStatelessRuleBindingReducer()
                    )
                    .IsAccepted,
                Is.True
            );
            Assert.That(store.Snapshot.RuleBindings, Is.Empty);

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
                new RulesStateSeed().SeedRuleBinding(owned).SeedRuleBinding(effect)
            ).Snapshot;
            PreparedPredicateContext context = new(
                package,
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
        public void CollectorObservesNextSnapshotWithoutRegistryRebuild()
        {
            PreparedRulePackage package = CreatePackage();
            ActiveRuleBinding binding = package.Bindings.Single().Create(Owner);
            RuleRegistryBuilder registryBuilder = new();
            registryBuilder.Define(package.Definitions.Single());
            RuleRegistry registry = registryBuilder.Build();
            InMemoryRulesStore store = new(new RulesStateSeed().SeedRuleBinding(binding));
            PreparedPredicateContext enabled = new(
                package,
                store.Snapshot,
                Owner,
                Array.Empty<string>()
            );
            Assert.That(
                PreparedRuleCollectors.CollectModifiers(package, enabled, "ac"),
                Has.Count.EqualTo(1)
            );

            store.Reduce(
                Context(
                    new DisableStatelessRuleBindingOp(binding.Id, binding.CreationOrder, Source)
                ),
                new DisableStatelessRuleBindingReducer()
            );
            PreparedPredicateContext disabled = new(
                package,
                store.Snapshot,
                Owner,
                Array.Empty<string>()
            );
            Assert.That(PreparedRuleCollectors.CollectModifiers(package, disabled, "ac"), Is.Empty);
            Assert.That(registry.Definitions, Has.Count.EqualTo(1));
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
            PreparedCreatureInputs inputs = new(
                7,
                new PreparedAbilityModifiers(1, 4, 2, 0, 1, 2),
                new[] { new KeyValuePair<string, int>("stealth", 2) },
                new[] { "leather-armor" },
                "light",
                new[] { "undead" },
                new[] { new PreparedDefenseDescriptor("slashing", 5) },
                new[] { new PreparedDefenseDescriptor("fire", 3) },
                new[] { new PreparedImmunityDescriptor("death-effects") },
                new[]
                {
                    "self:trait:undead",
                    "self:weakness:slashing",
                    "self:resistance:fire",
                    "self:immunity:death-effects",
                }
            );
            PreparedRuleDefinitionSpec definition = new(Definition, Source, "owned", "fixture");
            return new PreparedRulePackage(
                inputs,
                new[] { definition },
                new[] { new PreparedBindingSeed("test:owned", Definition, Source, 1) },
                new[]
                {
                    new PreparedOptionSpec(
                        Definition,
                        "feat:test-source",
                        PreparedPredicate.Always
                    ),
                },
                new[]
                {
                    new PreparedModifierSpec(
                        Definition,
                        "ac",
                        "fixture",
                        1,
                        "item",
                        string.Empty,
                        PreparedPredicate.Always
                    ),
                },
                Array.Empty<PreparedAdjustmentSpec>(),
                Array.Empty<PreparedDamageDiceSpec>(),
                Array.Empty<PreparedItemAlterationSpec>(),
                Array.Empty<PreparedUnsupportedDiagnostic>()
            );
        }

        private static ReductionContext<TOp> Context<TOp>(TOp operation) =>
            new(operation, new OpId(2), new OpId(1), Source);
    }
}
