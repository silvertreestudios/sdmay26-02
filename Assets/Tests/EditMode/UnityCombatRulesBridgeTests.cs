using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class UnityCombatRulesBridgeTests
{
    [Test]
    public void EncounterModulesPreserveOrderAcrossSharedCombatantPreparation()
    {
        GameObject firstObject = new GameObject("module-order-first");
        GameObject secondObject = new GameObject("module-order-second");
        CompositeLifetime firstLifetime = new CompositeLifetime();
        CompositeLifetime secondLifetime = new CompositeLifetime();
        try
        {
            CreatureComponent firstCreature = firstObject.AddComponent<CreatureComponent>();
            CreatureComponent secondCreature = secondObject.AddComponent<CreatureComponent>();
            BridgeTestActionController first =
                firstObject.AddComponent<BridgeTestActionController>();
            BridgeTestActionController second =
                secondObject.AddComponent<BridgeTestActionController>();
            List<string> order = new List<string>();
            RecordingEncounterModule alpha = new RecordingEncounterModule("alpha", order);
            RecordingEncounterModule beta = new RecordingEncounterModule("beta", order);
            UnityEncounterComposition composition = new UnityEncounterComposition(
                new IUnityEncounterModule[] { alpha, beta }
            );

            composition.PrepareCombatant(
                new UnityCombatantEnrollmentBuilder(
                    first,
                    firstCreature,
                    new CreatureState(
                        new CreatureId("initial"),
                        new PlayerId("module-order-player")
                    ),
                    new HealthState(1, 1),
                    new GridPosition(0, 0, 0),
                    new GridDistance(0),
                    firstLifetime
                )
            );
            composition.PrepareCombatant(
                new UnityCombatantEnrollmentBuilder(
                    second,
                    secondCreature,
                    new CreatureState(
                        new CreatureId("reinforcement"),
                        new PlayerId("module-order-player")
                    ),
                    new HealthState(1, 1),
                    new GridPosition(1, 0, 0),
                    new GridDistance(0),
                    secondLifetime
                )
            );
            IReadOnlyList<IEncounterTurnStartAdapter> adapters =
                composition.CreateTurnStartAdapters();
            composition.RefreshTopology(CreateTiles(1));

            Assert.That(
                order,
                Is.EqualTo(
                    new[]
                    {
                        "alpha:initial",
                        "beta:initial",
                        "alpha:reinforcement",
                        "beta:reinforcement",
                        "alpha:topology",
                        "beta:topology",
                    }
                )
            );
            Assert.That(adapters, Is.EqualTo(new[] { alpha.Adapter, beta.Adapter }));
        }
        finally
        {
            secondLifetime.Dispose();
            firstLifetime.Dispose();
            Object.DestroyImmediate(firstObject);
            Object.DestroyImmediate(secondObject);
        }
    }

    [Test]
    public void FailedEnrollmentAfterPreparedCompilationIsPureAndRetryMatchesCleanState()
    {
        GameObject failedObject = new("failed-prepared-enrollment");
        GameObject cleanObject = new("clean-prepared-enrollment");
        CompositeLifetime failedLifetime = new();
        CompositeLifetime retryLifetime = new();
        CompositeLifetime cleanLifetime = new();
        try
        {
            CreatureComponent failedCreature = ConfigureUnpreparedCleric(failedObject);
            BridgeTestActionController failedController =
                failedObject.AddComponent<BridgeTestActionController>();
            CreatureComponent cleanCreature = ConfigureUnpreparedCleric(cleanObject);
            BridgeTestActionController cleanController =
                cleanObject.AddComponent<BridgeTestActionController>();
            CharacterBuild originalBuild = failedCreature.Build;
            PreparedCharacter originalPrepared = failedCreature.Prepared;
            List<WeaponBonus> originalWeaponBonuses = failedCreature.weaponBonuses;
            List<WeaponActionBonus> originalWeaponActionBonuses =
                failedCreature.weaponActionBonuses;
            List<ArmorBonus> originalArmorBonuses = failedCreature.armorBonuses;
            InvalidOperationException expected = new("later enrollment failure");
            CapturingFailureEnrollmentModule failure = new(expected);
            UnityEncounterComposition failingComposition = new(
                new IUnityEncounterModule[] { new UnityPreparedRulesEncounterModule(), failure }
            );
            CreatureId creatureId = new("prepared-pure-retry");

            InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
                failingComposition.PrepareCombatant(
                    CreateEnrollmentBuilder(
                        failedController,
                        failedCreature,
                        creatureId,
                        failedLifetime
                    )
                )
            );
            failedLifetime.Dispose();

            Assert.That(actual, Is.SameAs(expected));
            Assert.That(failedCreature.Build, Is.SameAs(originalBuild));
            Assert.That(originalBuild.SubclassName, Is.Null);
            Assert.That(originalBuild.ClassFeatName, Is.Null);
            Assert.That(
                originalBuild.RuleSelections,
                Is.EqualTo(new Dictionary<string, string> { ["existing"] = "kept" })
            );
            Assert.That(originalBuild.TrainedSkills, Is.EqualTo(new[] { "arcana" }));
            Assert.That(failedCreature.weaponBonuses, Is.SameAs(originalWeaponBonuses));
            Assert.That(
                failedCreature.weaponBonuses,
                Is.EqualTo(
                    new[]
                    {
                        new WeaponBonus { category = "simple", bonus = 91 },
                    }
                )
            );
            Assert.That(failedCreature.weaponActionBonuses, Is.SameAs(originalWeaponActionBonuses));
            Assert.That(
                failedCreature.weaponActionBonuses,
                Is.EqualTo(
                    new[]
                    {
                        new WeaponActionBonus { weaponName = "mace", bonus = 92 },
                    }
                )
            );
            Assert.That(failedCreature.armorBonuses, Is.SameAs(originalArmorBonuses));
            Assert.That(
                failedCreature.armorBonuses,
                Is.EqualTo(
                    new[]
                    {
                        new ArmorBonus { category = "unarmored", bonus = 93 },
                    }
                )
            );
            Assert.That(failedCreature.attackBonus, Is.EqualTo(7));
            Assert.That(failedCreature.damageBonus, Is.EqualTo(4));
            Assert.That(failedCreature.ac, Is.EqualTo(15));
            Assert.That(failedCreature.Prepared, Is.SameAs(originalPrepared));

            UnityEncounterComposition preparedOnly = new(
                new IUnityEncounterModule[] { new UnityPreparedRulesEncounterModule() }
            );
            UnityCombatantEnrollmentBuilder retryBuilder = CreateEnrollmentBuilder(
                failedController,
                failedCreature,
                creatureId,
                retryLifetime
            );
            preparedOnly.PrepareCombatant(retryBuilder);
            CombatantRulesState retry = retryBuilder.BuildState();
            UnityCombatantEnrollmentBuilder cleanBuilder = CreateEnrollmentBuilder(
                cleanController,
                cleanCreature,
                creatureId,
                cleanLifetime
            );
            preparedOnly.PrepareCombatant(cleanBuilder);
            CombatantRulesState clean = cleanBuilder.BuildState();

            AssertPreparedStateEqual(failure.Captured, retry);
            AssertPreparedStateEqual(clean, retry);
            Assert.That(
                retry.PreparedInputs.RuleValues["class.proficiency.weapon.simple"],
                Is.EqualTo(2)
            );
            Assert.That(
                retry.PreparedInputs.RuleValues["class.proficiency.armor.unarmored"],
                Is.EqualTo(2)
            );
            Assert.That(retry.PreparedInputs.SkillRanks["religion"], Is.EqualTo(1));
            Assert.That(
                retry.PreparedInputs.BoundOptions.Select(value => value.Option),
                Does.Contain("item:owned:domain-initiate")
            );
        }
        finally
        {
            cleanLifetime.Dispose();
            retryLifetime.Dispose();
            failedLifetime.Dispose();
            Object.DestroyImmediate(cleanObject);
            Object.DestroyImmediate(failedObject);
        }
    }

    [Test]
    public void InitialAndReinforcementEnrollmentUseTheSameFeatureOwnedBaseState()
    {
        GameObject initialObject = new GameObject("feature-state-initial");
        GameObject anchorObject = new GameObject("feature-state-anchor");
        GameObject reinforcementObject = new GameObject("feature-state-reinforcement");
        try
        {
            BridgeTestActionController initial = ConfigureCombatant(
                initialObject,
                "Players",
                Vector3Int.zero
            );
            BridgeTestActionController anchor = ConfigureCombatant(
                anchorObject,
                "Enemies",
                Vector3Int.right
            );
            BridgeTestActionController reinforcement = ConfigureCombatant(
                reinforcementObject,
                "Players",
                new Vector3Int(2, 0, 0)
            );
            ConfigureFeatureState(initialObject.GetComponent<CreatureComponent>());
            ConfigureFeatureState(reinforcementObject.GetComponent<CreatureComponent>());
            UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
                new ActionController[] { initial, anchor },
                CreateTiles(3)
            );
            bridge.StartEncounter("Players");

            bridge.RegisterCombatants(new ActionController[] { reinforcement });

            CreatureId initialId = bridge.GetCreatureId(initial);
            CreatureId reinforcementId = bridge.GetCreatureId(reinforcement);
            AssertFeatureState(bridge.Snapshot, initialId);
            AssertFeatureState(bridge.Snapshot, reinforcementId);
            bridge.ReleaseOwnership();
        }
        finally
        {
            Object.DestroyImmediate(initialObject);
            Object.DestroyImmediate(anchorObject);
            Object.DestroyImmediate(reinforcementObject);
        }
    }

    [Test]
    public void RestoredFiniteSpellEffectUsesEncounterTimingAndProjectsRemainingDuration()
    {
        GameObject sourceObject = new GameObject("restored-effect-source");
        GameObject targetObject = new GameObject("restored-effect-target");
        try
        {
            BridgeTestActionController source = ConfigureCombatant(
                sourceObject,
                "Players",
                Vector3Int.zero
            );
            BridgeTestActionController target = ConfigureCombatant(
                targetObject,
                "Enemies",
                Vector3Int.right
            );
            BlessSpellEffect bless = new BlessSpellEffect(sourceObject)
            {
                RemainingTargetTurnStarts = 2,
            };
            SpellEffectController targetEffects = SpellEffectController.GetOrAdd(targetObject);
            targetEffects.RestoreEffects(new[] { bless });

            UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
                new ActionController[] { source, target },
                CreateTiles(2),
                new ScriptedRollService(20, 1)
            );
            bridge.StartEncounter("Players");

            ActiveEffectInstance adopted = bridge
                .Snapshot.ActiveEffects.Select(pair => pair.Value)
                .Single(effect =>
                    effect.DefinitionId
                    == UnitySpellcastingEncounterModule.RestoredTimedEffectDefinitionId
                );
            CreatureId sourceId = bridge.GetCreatureId(source);
            Assert.That(adopted.SourceCreature, Is.EqualTo(sourceId));
            Assert.That(
                bridge.Snapshot.ActiveEffectTimings[adopted.Id].RemainingBoundaries,
                Is.EqualTo(1)
            );
            Assert.That(bless.RemainingTargetTurnStarts, Is.EqualTo(1));
            Assert.That(targetEffects.HasEffect<BlessSpellEffect>(), Is.True);

            for (int turn = 0; turn < 3 && targetEffects.HasEffect<BlessSpellEffect>(); turn++)
            {
                TurnIdentity current = bridge.GetEncounter().CurrentTurn.Value;
                bridge.EndTurn(current.Actor);
            }

            Assert.That(targetEffects.HasEffect<BlessSpellEffect>(), Is.False);
            Assert.That(
                bridge.Snapshot.ActiveEffects[adopted.Id].Status,
                Is.EqualTo(ActiveEffectStatus.Expired)
            );
            Assert.That(bridge.Snapshot.ActiveEffectTimings.Contains(adopted.Id), Is.False);
            bridge.ReleaseOwnership();
        }
        finally
        {
            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(targetObject);
        }
    }

    [Test]
    public void ReleaseAttemptsCleanupAndEveryCallbackThenReportsFailuresInStableOrder()
    {
        GameObject combatantObject = new GameObject("release-failures");
        try
        {
            BridgeTestActionController controller = ConfigureCombatant(
                combatantObject,
                "Players",
                Vector3Int.zero
            );
            UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
                new ActionController[] { controller },
                CreateTiles(1)
            );
            List<string> order = new List<string>();
            InvalidOperationException firstCleanupFailure = new InvalidOperationException(
                "first cleanup"
            );
            NotSupportedException secondCleanupFailure = new NotSupportedException(
                "second cleanup"
            );
            ApplicationException firstCallbackFailure = new ApplicationException("first callback");
            ArgumentException thirdCallbackFailure = new ArgumentException("third callback");
            TrackingDisposable firstCleanup = new TrackingDisposable(
                () => order.Add("cleanup-1"),
                firstCleanupFailure
            );
            TrackingDisposable secondCleanup = new TrackingDisposable(
                () => order.Add("cleanup-2"),
                secondCleanupFailure
            );
            bridge.OwnEncounterResource(firstCleanup);
            bridge.OwnEncounterResource(secondCleanup);
            Action callbacks = () =>
            {
                order.Add("callback-1");
                throw firstCallbackFailure;
            };
            callbacks += () => order.Add("callback-2");
            callbacks += () =>
            {
                order.Add("callback-3");
                throw thirdCallbackFailure;
            };

            AggregateException error = Assert.Throws<AggregateException>(() =>
                bridge.ReleaseOwnership(callbacks)
            );

            Assert.That(
                order,
                Is.EqualTo(
                    new[] { "cleanup-2", "cleanup-1", "callback-1", "callback-2", "callback-3" }
                )
            );
            Assert.That(
                error.InnerExceptions,
                Is.EqualTo(
                    new Exception[]
                    {
                        secondCleanupFailure,
                        firstCleanupFailure,
                        firstCallbackFailure,
                        thirdCallbackFailure,
                    }
                )
            );
            Assert.That(firstCleanup.DisposeCount, Is.EqualTo(1));
            Assert.That(secondCleanup.DisposeCount, Is.EqualTo(1));
            List<string> immediateOrder = new List<string>();
            InvalidOperationException firstImmediateFailure = new InvalidOperationException(
                "first immediate callback"
            );
            NotImplementedException thirdImmediateFailure = new NotImplementedException(
                "third immediate callback"
            );
            Action immediateCallbacks = () =>
            {
                immediateOrder.Add("immediate-1");
                throw firstImmediateFailure;
            };
            immediateCallbacks += () => immediateOrder.Add("immediate-2");
            immediateCallbacks += () =>
            {
                immediateOrder.Add("immediate-3");
                throw thirdImmediateFailure;
            };

            AggregateException immediateError = Assert.Throws<AggregateException>(() =>
                bridge.ReleaseOwnership(immediateCallbacks)
            );

            Assert.That(
                immediateOrder,
                Is.EqualTo(new[] { "immediate-1", "immediate-2", "immediate-3" })
            );
            Assert.That(
                immediateError.InnerExceptions,
                Is.EqualTo(new Exception[] { firstImmediateFailure, thirdImmediateFailure })
            );
            Assert.That(firstCleanup.DisposeCount, Is.EqualTo(1));
            Assert.That(secondCleanup.DisposeCount, Is.EqualTo(1));
            Assert.That(order, Has.Count.EqualTo(5));
            Assert.DoesNotThrow(() => bridge.ReleaseOwnership());
        }
        finally
        {
            Object.DestroyImmediate(combatantObject);
        }
    }

    [Test]
    public void ReleasePreservesSingleFailureIdentityAndRemainsIdempotent()
    {
        GameObject combatantObject = new GameObject("release-single-failure");
        try
        {
            BridgeTestActionController controller = ConfigureCombatant(
                combatantObject,
                "Players",
                Vector3Int.zero
            );
            UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
                new ActionController[] { controller },
                CreateTiles(1)
            );
            InvalidOperationException failure = new InvalidOperationException("single callback");
            int invocationCount = 0;

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                bridge.ReleaseOwnership(() =>
                {
                    invocationCount++;
                    throw failure;
                })
            );

            Assert.That(error, Is.SameAs(failure));
            ApplicationException immediateFailure = new ApplicationException(
                "single immediate callback"
            );
            ApplicationException immediateError = Assert.Throws<ApplicationException>(() =>
                bridge.ReleaseOwnership(() => throw immediateFailure)
            );
            Assert.That(immediateError, Is.SameAs(immediateFailure));
            Assert.DoesNotThrow(() => bridge.ReleaseOwnership());
            Assert.That(invocationCount, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(combatantObject);
        }
    }

    [Test]
    public void BridgeRejectsEmptyEncounterWithSpecificError()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            UnityCombatRulesBridge.Create(Array.Empty<ActionController>(), CreateTiles(1))
        );

        StringAssert.Contains("requires at least one controller", error.Message);
    }

    [Test]
    public void BridgeRejectsNullEncounterCreatureWithSpecificError()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            UnityCombatRulesBridge.Create(new ActionController[] { null }, CreateTiles(1))
        );

        StringAssert.Contains("cannot contain a null controller", error.Message);
    }

    [Test]
    public void CreatureHealthCommandsRejectMissingEncounterBridge()
    {
        GameObject creatureObject = new GameObject("creature");
        try
        {
            CreatureComponent creature = creatureObject.AddComponent<CreatureComponent>();
            creature.InitializeHealthBeforeEncounter(10, 10);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                creature.ApplyFinalDamage(1, RuleSource.FromSlug("test-damage"))
            );

            StringAssert.Contains("require an encounter health bridge", error.Message);
            Assert.That(creature.Health, Is.EqualTo(new HealthState(10, 10)));
        }
        finally
        {
            Object.DestroyImmediate(creatureObject);
        }
    }

    [Test]
    public void DetachedControllerProjectsNeutralCombatStateAndRejectsPositiveAuthority()
    {
        GameObject creatureObject = new GameObject("detached-controller");
        try
        {
            creatureObject
                .AddComponent<CreatureComponent>()
                .InitializeHealthBeforeEncounter(10, 10);
            BridgeTestActionController controller =
                creatureObject.AddComponent<BridgeTestActionController>();

            Assert.That(controller.HasTurnAuthority, Is.False);
            Assert.That(controller.ActionPoints, Is.Zero);
            Assert.That(controller.Reacted, Is.False);
            Assert.That(controller.StrikePenalty, Is.Zero);
            Assert.DoesNotThrow(() => controller.SpendActions(0));
            Assert.Throws<InvalidOperationException>(() => controller.SpendActions(1));
            Assert.Throws<InvalidOperationException>(() => controller.StartTurn());
        }
        finally
        {
            Object.DestroyImmediate(creatureObject);
        }
    }

    [Test]
    public void TurnAuthorityIsUnavailableBeforeStartAndExactAfterStart()
    {
        GameObject firstObject = new GameObject("first");
        GameObject secondObject = new GameObject("second");
        try
        {
            BridgeTestActionController first = ConfigureCombatant(
                firstObject,
                "Players",
                Vector3Int.zero
            );
            BridgeTestActionController second = ConfigureCombatant(
                secondObject,
                "Enemies",
                Vector3Int.right
            );
            UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
                new ActionController[] { first, second },
                CreateTiles(2)
            );
            CreatureId firstId = bridge.GetCreatureId(first);
            CreatureId secondId = bridge.GetCreatureId(second);

            Assert.That(bridge.HasTurnAuthority(firstId), Is.False);
            Assert.That(bridge.HasTurnAuthority(secondId), Is.False);

            EncounterState encounter = bridge.StartEncounter("Players");

            Assert.That(encounter.CurrentTurn.HasValue, Is.True);
            Assert.That(
                bridge.HasTurnAuthority(firstId),
                Is.EqualTo(encounter.CurrentTurn.Value.Actor == firstId)
            );
            Assert.That(
                bridge.HasTurnAuthority(secondId),
                Is.EqualTo(encounter.CurrentTurn.Value.Actor == secondId)
            );
        }
        finally
        {
            Object.DestroyImmediate(firstObject);
            Object.DestroyImmediate(secondObject);
        }
    }

    [Test]
    public void FailedCompetingOwnerCompositionLeavesCandidateDetachedAndAllowsRetry()
    {
        GameObject firstObject = new GameObject("owned-controller");
        GameObject candidateObject = new GameObject("candidate-controller");
        try
        {
            CreatureComponent firstCreature = firstObject.AddComponent<CreatureComponent>();
            CreatureComponent candidateCreature = candidateObject.AddComponent<CreatureComponent>();
            firstCreature.InitializeHealthBeforeEncounter(10, 10);
            candidateCreature.InitializeHealthBeforeEncounter(10, 10);
            BridgeTestActionController first =
                firstObject.AddComponent<BridgeTestActionController>();
            BridgeTestActionController candidate =
                candidateObject.AddComponent<BridgeTestActionController>();
            Team firstTeam = firstObject.AddComponent<Team>();
            Team candidateTeam = candidateObject.AddComponent<Team>();
            firstTeam.Name = "Players";
            candidateTeam.Name = "Enemies";
            UnityCombatRulesBridge owner = UnityCombatRulesBridge.Create(
                new ActionController[] { first },
                CreateTiles(2)
            );

            Assert.Throws<InvalidOperationException>(() =>
                UnityCombatRulesBridge.Create(
                    new ActionController[] { candidate, first },
                    CreateTiles(2)
                )
            );
            Assert.That(candidate.ActionPoints, Is.Zero);
            Assert.That(candidate.HasTurnAuthority, Is.False);
            Assert.That(candidateCreature.Health, Is.EqualTo(new HealthState(10, 10)));

            owner.ReleaseOwnership();
            UnityCombatRulesBridge retry = UnityCombatRulesBridge.Create(
                new ActionController[] { candidate, first },
                CreateTiles(2)
            );

            Assert.That(retry.GetCreatureId(candidate), Is.Not.EqualTo(default(CreatureId)));
            retry.ReleaseOwnership();
        }
        finally
        {
            Object.DestroyImmediate(firstObject);
            Object.DestroyImmediate(candidateObject);
        }
    }

    [Test]
    public void FailedInstallationPreflightLeavesEveryCandidateDetachedAndAllowsRetry()
    {
        GameObject firstObject = new GameObject("installation-first");
        GameObject throwerObject = new GameObject("installation-thrower");
        try
        {
            CreatureComponent firstCreature = firstObject.AddComponent<CreatureComponent>();
            CreatureComponent throwerCreature = throwerObject.AddComponent<CreatureComponent>();
            firstCreature.InitializeHealthBeforeEncounter(10, 10);
            throwerCreature.InitializeHealthBeforeEncounter(8, 8);
            BridgeTestActionController first =
                firstObject.AddComponent<BridgeTestActionController>();
            BridgeTestActionController thrower =
                throwerObject.AddComponent<BridgeTestActionController>();
            Team firstTeam = firstObject.AddComponent<Team>();
            Team throwerTeam = throwerObject.AddComponent<Team>();
            firstTeam.Name = "Players";
            throwerTeam.Name = "Enemies";
            thrower.GetActionsEvent.AddListener(_ =>
                throw new InvalidOperationException("Injected action installation failure.")
            );

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                UnityCombatRulesBridge.Create(
                    new ActionController[] { first, thrower },
                    CreateTiles(2)
                )
            );

            Assert.That(error.Message, Does.Contain("Injected action installation failure"));
            Assert.That(first.ActionPoints, Is.Zero);
            Assert.That(first.HasTurnAuthority, Is.False);
            Assert.That(thrower.ActionPoints, Is.Zero);
            Assert.That(thrower.HasTurnAuthority, Is.False);
            Assert.That(firstCreature.Health, Is.EqualTo(new HealthState(10, 10)));
            Assert.That(throwerCreature.Health, Is.EqualTo(new HealthState(8, 8)));

            thrower.GetActionsEvent.RemoveAllListeners();
            UnityCombatRulesBridge retry = UnityCombatRulesBridge.Create(
                new ActionController[] { first, thrower },
                CreateTiles(2)
            );

            Assert.That(retry.GetCreatureId(first).Value, Is.EqualTo("combat-creature-1"));
            Assert.That(retry.GetCreatureId(thrower).Value, Is.EqualTo("combat-creature-2"));
            retry.ReleaseOwnership();
        }
        finally
        {
            Object.DestroyImmediate(firstObject);
            Object.DestroyImmediate(throwerObject);
        }
    }

    [Test]
    public void MixedControllerAttachmentThrowsInsteadOfProjectingDetachedState()
    {
        GameObject ownerObject = new GameObject("attachment-owner");
        GameObject mixedObject = new GameObject("mixed-controller");
        try
        {
            CreatureComponent ownerCreature = ownerObject.AddComponent<CreatureComponent>();
            ownerCreature.InitializeHealthBeforeEncounter(10, 10);
            BridgeTestActionController ownerController =
                ownerObject.AddComponent<BridgeTestActionController>();
            Team team = ownerObject.AddComponent<Team>();
            team.Name = "Players";
            UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
                new ActionController[] { ownerController },
                CreateTiles(1)
            );
            mixedObject.AddComponent<CreatureComponent>().InitializeHealthBeforeEncounter(10, 10);
            BridgeTestActionController mixed =
                mixedObject.AddComponent<BridgeTestActionController>();
            FieldInfo bridgeField = typeof(ActionController).GetField(
                "combatRules",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.That(bridgeField, Is.Not.Null);
            bridgeField.SetValue(mixed, bridge);

            Assert.Throws<InvalidOperationException>(() => mixed.TryGetCombatRules(out _, out _));
            Assert.Throws<InvalidOperationException>(() => _ = mixed.ActionPoints);
        }
        finally
        {
            Object.DestroyImmediate(ownerObject);
            Object.DestroyImmediate(mixedObject);
        }
    }

    [Test]
    public void ReinforcementCompetingOwnerLeavesStoreUnchangedAndRetrySucceeds()
    {
        GameObject hostObject = new GameObject("reinforcement-host");
        GameObject anchorObject = new GameObject("reinforcement-anchor");
        GameObject reinforcementObject = new GameObject("reinforcement");
        try
        {
            CreatureComponent hostCreature = hostObject.AddComponent<CreatureComponent>();
            CreatureComponent anchorCreature = anchorObject.AddComponent<CreatureComponent>();
            CreatureComponent reinforcementCreature =
                reinforcementObject.AddComponent<CreatureComponent>();
            hostCreature.InitializeHealthBeforeEncounter(10, 10);
            anchorCreature.InitializeHealthBeforeEncounter(10, 10);
            reinforcementCreature.InitializeHealthBeforeEncounter(10, 10);
            BridgeTestActionController host = hostObject.AddComponent<BridgeTestActionController>();
            BridgeTestActionController anchor =
                anchorObject.AddComponent<BridgeTestActionController>();
            BridgeTestActionController reinforcement =
                reinforcementObject.AddComponent<BridgeTestActionController>();
            Team hostTeam = hostObject.AddComponent<Team>();
            Team anchorTeam = anchorObject.AddComponent<Team>();
            Team reinforcementTeam = reinforcementObject.AddComponent<Team>();
            hostTeam.Name = "Players";
            anchorTeam.Name = "Enemies";
            reinforcementTeam.Name = "Enemies";
            UnityCombatRulesBridge encounter = UnityCombatRulesBridge.Create(
                new ActionController[] { host, anchor },
                CreateTiles(3)
            );
            encounter.StartEncounter("Players");
            UnityCombatRulesBridge competing = UnityCombatRulesBridge.Create(
                new ActionController[] { reinforcement },
                CreateTiles(2)
            );
            RulesSnapshot before = encounter.Snapshot;

            Assert.Throws<InvalidOperationException>(() =>
                encounter.RegisterCombatants(new ActionController[] { reinforcement })
            );

            Assert.That(encounter.Snapshot.Version, Is.EqualTo(before.Version));
            Assert.That(
                encounter.GetEncounter().Roster.Select(entry => entry.Creature),
                Is.EqualTo(before.Encounters.Single().Value.Roster.Select(entry => entry.Creature))
            );
            competing.ReleaseOwnership();

            Assert.DoesNotThrow(() =>
                encounter.RegisterCombatants(new ActionController[] { reinforcement })
            );
            Assert.That(encounter.GetEncounter().Roster, Has.Count.EqualTo(3));
            Assert.That(
                encounter.GetCreatureId(reinforcement).Value,
                Is.EqualTo("combat-creature-3")
            );
            encounter.ReleaseOwnership();
        }
        finally
        {
            Object.DestroyImmediate(hostObject);
            Object.DestroyImmediate(anchorObject);
            Object.DestroyImmediate(reinforcementObject);
        }
    }

    [Test]
    public void ReinforcementInstallationFailureLeavesStoreAndOwnershipUnchangedForRetry()
    {
        GameObject hostObject = new GameObject("failure-host");
        GameObject anchorObject = new GameObject("failure-anchor");
        GameObject reinforcementObject = new GameObject("failure-reinforcement");
        try
        {
            CreatureComponent hostCreature = hostObject.AddComponent<CreatureComponent>();
            CreatureComponent anchorCreature = anchorObject.AddComponent<CreatureComponent>();
            CreatureComponent reinforcementCreature =
                reinforcementObject.AddComponent<CreatureComponent>();
            hostCreature.InitializeHealthBeforeEncounter(10, 10);
            anchorCreature.InitializeHealthBeforeEncounter(10, 10);
            reinforcementCreature.InitializeHealthBeforeEncounter(7, 7);
            BridgeTestActionController host = hostObject.AddComponent<BridgeTestActionController>();
            BridgeTestActionController anchor =
                anchorObject.AddComponent<BridgeTestActionController>();
            BridgeTestActionController reinforcement =
                reinforcementObject.AddComponent<BridgeTestActionController>();
            Team hostTeam = hostObject.AddComponent<Team>();
            Team anchorTeam = anchorObject.AddComponent<Team>();
            Team reinforcementTeam = reinforcementObject.AddComponent<Team>();
            hostTeam.Name = "Players";
            anchorTeam.Name = "Enemies";
            reinforcementTeam.Name = "Enemies";
            UnityCombatRulesBridge encounter = UnityCombatRulesBridge.Create(
                new ActionController[] { host, anchor },
                CreateTiles(3)
            );
            encounter.StartEncounter("Players");
            RulesSnapshot before = encounter.Snapshot;
            reinforcement.GetActionsEvent.AddListener(_ =>
                throw new InvalidOperationException("Injected reinforcement installation failure.")
            );

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                encounter.RegisterCombatants(new ActionController[] { reinforcement })
            );

            Assert.That(error.Message, Does.Contain("Injected reinforcement installation failure"));
            Assert.That(encounter.Snapshot.Version, Is.EqualTo(before.Version));
            Assert.That(
                encounter.GetEncounter().Roster.Select(entry => entry.Creature),
                Is.EqualTo(before.Encounters.Single().Value.Roster.Select(entry => entry.Creature))
            );
            Assert.That(reinforcement.ActionPoints, Is.Zero);
            Assert.That(reinforcement.HasTurnAuthority, Is.False);
            Assert.That(reinforcementCreature.Health, Is.EqualTo(new HealthState(7, 7)));

            reinforcement.GetActionsEvent.RemoveAllListeners();
            Assert.DoesNotThrow(() =>
                encounter.RegisterCombatants(new ActionController[] { reinforcement })
            );
            Assert.That(encounter.GetEncounter().Roster, Has.Count.EqualTo(3));
            Assert.That(
                encounter.GetCreatureId(reinforcement).Value,
                Is.EqualTo("combat-creature-3")
            );
            encounter.ReleaseOwnership();
        }
        finally
        {
            Object.DestroyImmediate(hostObject);
            Object.DestroyImmediate(anchorObject);
            Object.DestroyImmediate(reinforcementObject);
        }
    }

    [Test]
    public void BridgeOwnsHealthAndProjectsCommittedFactsBackToComponents()
    {
        GameObject firstObject = new GameObject("first");
        GameObject secondObject = new GameObject("second");
        try
        {
            CreatureComponent first = firstObject.AddComponent<CreatureComponent>();
            CreatureComponent second = secondObject.AddComponent<CreatureComponent>();
            first.InitializeHealthBeforeEncounter(10, 12);
            second.InitializeHealthBeforeEncounter(7, 7);
            UnityCombatRulesBridge bridge = CreateBridge(first, second);

            CreatureId firstId = bridge.GetCreatureId(first);
            CreatureId secondId = bridge.GetCreatureId(second);
            DamageOutcome damage = bridge.ApplyFinalDamage(
                firstId,
                4,
                RuleSource.FromSlug("test-strike")
            );
            HealingOutcome healing = bridge.ApplyHealing(
                firstId,
                2,
                RuleSource.FromSlug("test-heal")
            );

            Assert.That(firstId.Value, Is.EqualTo("combat-creature-1"));
            Assert.That(secondId.Value, Is.EqualTo("combat-creature-2"));
            Assert.That(damage.Applied, Is.EqualTo(4));
            Assert.That(healing.Applied, Is.EqualTo(2));
            Assert.That(first.hp, Is.EqualTo(8));
            Assert.That(first.maxHp, Is.EqualTo(12));
            Assert.That(bridge.Snapshot.Health[firstId].Current, Is.EqualTo(first.hp));
            Assert.That(
                bridge.TryGetOriginSource(
                    new HealthChangeOriginId("health-origin-1"),
                    out RuleSource originSource
                ),
                Is.True
            );
            Assert.That(originSource, Is.EqualTo(RuleSource.FromSlug("test-strike")));
        }
        finally
        {
            Object.DestroyImmediate(firstObject);
            Object.DestroyImmediate(secondObject);
        }
    }

    [Test]
    public void BridgeProjectsSourceTemporaryHitPointStateAndImmunity()
    {
        GameObject creatureObject = new GameObject("creature");
        try
        {
            CreatureComponent creature = creatureObject.AddComponent<CreatureComponent>();
            creature.InitializeHealthBeforeEncounter(10, 10);
            UnityCombatRulesBridge bridge = CreateBridge(creature);
            CreatureId id = bridge.GetCreatureId(creature);
            RuleSource rage = RuleSource.FromSlug("rage");

            creature.GrantSourceTemporaryHitPoints(rage, 4);
            creature.ApplyFinalDamage(1, RuleSource.FromSlug("test-damage"));
            creature.RemoveSourceTemporaryHitPoints(rage);
            creature.AddTemporaryHitPointImmunity(rage);
            TemporaryHitPointsGrantOutcome blocked = creature.GrantSourceTemporaryHitPoints(
                rage,
                5
            );

            Assert.That(blocked.Immune, Is.True);
            Assert.That(creature.tempHp, Is.Zero);
            Assert.That(bridge.Snapshot.Health[id].Temporary, Is.Zero);
            Assert.That(creature.HasTempHpImmunity("rage"), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(creatureObject);
        }
    }

    [Test]
    public void BridgePropagatesCompletedDispatcherFailure()
    {
        GameObject creatureObject = new GameObject("creature");
        try
        {
            CreatureComponent creature = creatureObject.AddComponent<CreatureComponent>();
            creature.InitializeHealthBeforeEncounter(10, 10);
            UnityCombatRulesBridge bridge = CreateBridge(creature);
            InvalidOperationException expected = new InvalidOperationException(
                "completed observer failure"
            );
            GetDispatcher(bridge)
                .RegisterFactObserver<HealthFact>(new CompletedFailureObserver(expected));

            InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
                bridge.ApplyFinalDamage(
                    bridge.GetCreatureId(creature),
                    1,
                    RuleSource.FromSlug("test-damage")
                )
            );

            Assert.That(actual, Is.SameAs(expected));
        }
        finally
        {
            Object.DestroyImmediate(creatureObject);
        }
    }

    [Test]
    public void BridgeRejectsIncompleteDispatcherWork()
    {
        GameObject creatureObject = new GameObject("creature");
        IncompleteObserver observer = new IncompleteObserver();
        try
        {
            CreatureComponent creature = creatureObject.AddComponent<CreatureComponent>();
            creature.InitializeHealthBeforeEncounter(10, 10);
            UnityCombatRulesBridge bridge = CreateBridge(creature);
            GetDispatcher(bridge).RegisterFactObserver<HealthFact>(observer);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                bridge.ApplyFinalDamage(
                    bridge.GetCreatureId(creature),
                    1,
                    RuleSource.FromSlug("test-damage")
                )
            );

            StringAssert.Contains("cannot contain asynchronous callbacks", error.Message);
        }
        finally
        {
            observer.Complete();
            Object.DestroyImmediate(creatureObject);
        }
    }

    [Test]
    public async Task CombatCompositionDispatchesDormantStrideThroughSharedState()
    {
        GameObject creatureObject = new GameObject("stride-creature");
        GameObject opponentObject = new GameObject("stride-opponent");
        try
        {
            CreatureComponent creature = creatureObject.AddComponent<CreatureComponent>();
            creature.InitializeHealthBeforeEncounter(10, 10);
            creature.speed = 25;
            BridgeTestActionController controller =
                creatureObject.AddComponent<BridgeTestActionController>();
            Team opponentTeam = opponentObject.AddComponent<Team>();
            opponentTeam.Name = "enemies";
            CreatureComponent opponent = opponentObject.AddComponent<CreatureComponent>();
            opponent.InitializeHealthBeforeEncounter(10, 10);
            BridgeTestActionController opponentController =
                opponentObject.AddComponent<BridgeTestActionController>();
            opponentObject.transform.position = new Vector3(3, 0, 0);
            GridPrivate.Tile[,] tiles = new GridPrivate.Tile[4, 1];
            for (int x = 0; x < tiles.GetLength(0); x++)
                tiles[x, 0] = new GridPrivate.Tile();
            UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
                new ActionController[] { controller, opponentController },
                tiles,
                new ScriptedRollService(20, 10)
            );
            CreatureId id = bridge.GetCreatureId(creature);
            bridge.BeginTurn(id, 3);

            OpResult<MovePathOutcome> result = await bridge.DispatchStride(
                id,
                new MovementPath(
                    new GridPosition(0, 0, 0),
                    new[] { new GridPosition(1, 0, 0), new GridPosition(2, 0, 0) }
                )
            );

            Assert.That(result, Is.TypeOf<ResolvedOpResult<MovePathOutcome>>());
            Assert.That(bridge.Snapshot.Positions[id], Is.EqualTo(new GridPosition(2, 0, 0)));
            Assert.That(bridge.Snapshot.ActionEconomy[id].ActionsRemaining, Is.EqualTo(2));
            Assert.That(controller.ActionPoints, Is.EqualTo(2));
        }
        finally
        {
            Object.DestroyImmediate(creatureObject);
            Object.DestroyImmediate(opponentObject);
        }
    }

    [Test]
    public async Task ExplorationStrideProjectsRulesFactsWithoutClaimingCombatActionPoints()
    {
        GameObject creatureObject = new GameObject("exploration-stride-creature");
        try
        {
            CreatureComponent creature = creatureObject.AddComponent<CreatureComponent>();
            creature.InitializeHealthBeforeEncounter(10, 10);
            creature.speed = 25;
            BridgeTestActionController controller =
                creatureObject.AddComponent<BridgeTestActionController>();
            UnityCombatRulesBridge bridge = UnityCombatRulesBridge.CreateExplorationStride(
                controller,
                CreateTiles(3)
            );
            CreatureId id = bridge.GetCreatureId(controller);
            RecordingMovementObserver observer = new RecordingMovementObserver();

            bool resolved = await bridge.DispatchProjectedStride(
                id,
                new MovementPath(
                    new GridPosition(0, 0, 0),
                    new[] { new GridPosition(1, 0, 0), new GridPosition(2, 0, 0) }
                ),
                observer
            );

            Assert.That(resolved, Is.True);
            Assert.That(observer.Facts, Has.Count.EqualTo(2));
            Assert.That(observer.Facts[0].From, Is.EqualTo(new GridPosition(0, 0, 0)));
            Assert.That(observer.Facts[0].To, Is.EqualTo(new GridPosition(1, 0, 0)));
            Assert.That(observer.Facts[1].From, Is.EqualTo(new GridPosition(1, 0, 0)));
            Assert.That(observer.Facts[1].To, Is.EqualTo(new GridPosition(2, 0, 0)));
            Assert.That(bridge.Snapshot.Positions[id], Is.EqualTo(new GridPosition(2, 0, 0)));
            Assert.That(bridge.Snapshot.ActionEconomy[id].ActionsRemaining, Is.Zero);
            Assert.That(
                controller.ActionPoints,
                Is.Zero,
                "A detached exploration controller must project neutral combat AP."
            );
        }
        finally
        {
            Object.DestroyImmediate(creatureObject);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CombatStridePreservesDirectionalFriendshipAcrossRegistrationOrder(
        bool moverRegisteredFirst
    )
    {
        GameObject teamRulesObject = new GameObject("team-rules");
        GameObject moverObject = new GameObject("directional-mover");
        GameObject occupantObject = new GameObject("directional-occupant");
        try
        {
            TeamRules teamRules = InitializeTeamRules(teamRulesObject);
            teamRules.AddHostileTeam("mover-team");
            teamRules.AddHostileTeam("occupant-team");
            teamRules.OneWayFriendly("mover-team", "occupant-team");

            BridgeTestActionController mover = ConfigureCombatant(
                moverObject,
                "mover-team",
                new Vector3(0, 0, 0)
            );
            BridgeTestActionController occupant = ConfigureCombatant(
                occupantObject,
                "occupant-team",
                new Vector3(1, 0, 0)
            );
            ActionController[] registrations = moverRegisteredFirst
                ? new ActionController[] { mover, occupant }
                : new ActionController[] { occupant, mover };
            UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
                registrations,
                CreateTiles(4)
            );
            CreatureId moverId = bridge.GetCreatureId(
                moverObject.GetComponent<CreatureComponent>()
            );
            CreatureId occupantId = bridge.GetCreatureId(
                occupantObject.GetComponent<CreatureComponent>()
            );
            PlayerId moverPlayer = bridge.Snapshot.Creatures[moverId].Player;
            PlayerId occupantPlayer = bridge.Snapshot.Creatures[occupantId].Player;

            Assert.That(TeamRules.TryGetInstance(out TeamRules activeRules), Is.True);
            Assert.That(activeRules, Is.SameAs(teamRules));
            Assert.That(teamRules.IsFriendly("mover-team", "occupant-team"), Is.True);
            Assert.That(moverPlayer, Is.Not.EqualTo(occupantPlayer));

            GridPrivate.Tile directionalTile = new GridPrivate.Tile();
            directionalTile.Occupants.Add(occupantObject);
            Assert.That(directionalTile.CanStrideOn(moverObject), Is.True);
            directionalTile.Occupants.Clear();
            directionalTile.Occupants.Add(moverObject);
            Assert.That(
                directionalTile.CanStrideOn(occupantObject),
                Is.False,
                "Path discovery must use the mover-to-occupant friendship direction."
            );

            bridge.BeginTurn(moverId, 3);
            OpResult<MovePathOutcome> forward = await bridge.DispatchStride(
                moverId,
                new MovementPath(
                    new GridPosition(0, 0, 0),
                    new[] { new GridPosition(1, 0, 0), new GridPosition(2, 0, 0) }
                )
            );

            string forwardFailure = forward is InvalidOpResult<MovePathOutcome> invalidForward
                ? invalidForward.Reason
                : string.Empty;
            Assert.That(forward, Is.TypeOf<ResolvedOpResult<MovePathOutcome>>(), forwardFailure);
            Assert.That(bridge.Snapshot.Positions[moverId], Is.EqualTo(new GridPosition(2, 0, 0)));
            Assert.That(bridge.Snapshot.ActionEconomy[moverId].ActionsRemaining, Is.EqualTo(2));

            bridge.BeginTurn(occupantId, 3);
            OpResult<MovePathOutcome> reverse = await bridge.DispatchStride(
                occupantId,
                new MovementPath(
                    new GridPosition(1, 0, 0),
                    new[] { new GridPosition(2, 0, 0), new GridPosition(3, 0, 0) }
                )
            );

            Assert.That(reverse, Is.TypeOf<InvalidOpResult<MovePathOutcome>>());
            Assert.That(
                bridge.Snapshot.Positions[occupantId],
                Is.EqualTo(new GridPosition(1, 0, 0))
            );
            Assert.That(bridge.Snapshot.ActionEconomy[occupantId].ActionsRemaining, Is.EqualTo(3));
            Assert.That(occupant.ActionPoints, Is.EqualTo(3));
        }
        finally
        {
            Object.DestroyImmediate(moverObject);
            Object.DestroyImmediate(occupantObject);
            Object.DestroyImmediate(teamRulesObject);
        }
    }

    [Test]
    public async Task CombatStrideDoesNotTreatDirectionalFriendshipAsTransitive()
    {
        GameObject teamRulesObject = new GameObject("team-rules");
        GameObject moverObject = new GameObject("non-transitive-mover");
        GameObject middleObject = new GameObject("non-transitive-middle");
        GameObject lastObject = new GameObject("non-transitive-last");
        try
        {
            TeamRules teamRules = InitializeTeamRules(teamRulesObject);
            teamRules.AddHostileTeam("mover-team");
            teamRules.AddHostileTeam("middle-team");
            teamRules.AddHostileTeam("last-team");
            teamRules.OneWayFriendly("mover-team", "middle-team");
            teamRules.OneWayFriendly("middle-team", "last-team");

            BridgeTestActionController mover = ConfigureCombatant(
                moverObject,
                "mover-team",
                new Vector3(0, 0, 0)
            );
            BridgeTestActionController middle = ConfigureCombatant(
                middleObject,
                "middle-team",
                new Vector3(1, 0, 0)
            );
            BridgeTestActionController last = ConfigureCombatant(
                lastObject,
                "last-team",
                new Vector3(2, 0, 0)
            );
            UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
                new ActionController[] { last, middle, mover },
                CreateTiles(4)
            );
            CreatureId moverId = bridge.GetCreatureId(
                moverObject.GetComponent<CreatureComponent>()
            );
            bridge.BeginTurn(moverId, 3);

            OpResult<MovePathOutcome> result = await bridge.DispatchStride(
                moverId,
                new MovementPath(
                    new GridPosition(0, 0, 0),
                    new[]
                    {
                        new GridPosition(1, 0, 0),
                        new GridPosition(2, 0, 0),
                        new GridPosition(3, 0, 0),
                    }
                )
            );

            Assert.That(result, Is.TypeOf<InvalidOpResult<MovePathOutcome>>());
            Assert.That(bridge.Snapshot.Positions[moverId], Is.EqualTo(new GridPosition(0, 0, 0)));
            Assert.That(bridge.Snapshot.ActionEconomy[moverId].ActionsRemaining, Is.EqualTo(3));
            Assert.That(mover.ActionPoints, Is.EqualTo(3));
            Assert.That(result.Facts, Is.Empty);
        }
        finally
        {
            Object.DestroyImmediate(moverObject);
            Object.DestroyImmediate(middleObject);
            Object.DestroyImmediate(lastObject);
            Object.DestroyImmediate(teamRulesObject);
        }
    }

    [TestCase(ReinforcementFailureCheckpoint.Join)]
    [TestCase(ReinforcementFailureCheckpoint.Strike)]
    [TestCase(ReinforcementFailureCheckpoint.RestoredSpellEffect)]
    public void ReinforcementPostCommitFailureRetriesExactCheckpointWithoutReplay(
        ReinforcementFailureCheckpoint checkpoint
    )
    {
        GameObject hostObject = new GameObject($"checkpoint-{checkpoint}-host");
        GameObject anchorObject = new GameObject($"checkpoint-{checkpoint}-anchor");
        GameObject reinforcementObject = new GameObject($"checkpoint-{checkpoint}-reinforcement");
        try
        {
            BridgeTestActionController host = ConfigureCombatant(
                hostObject,
                "Players",
                Vector3Int.zero
            );
            BridgeTestActionController anchor = ConfigureCombatant(
                anchorObject,
                "Enemies",
                Vector3Int.right
            );
            BridgeTestActionController reinforcement = ConfigureCombatant(
                reinforcementObject,
                "Enemies",
                new Vector3Int(2, 0, 0)
            );
            if (checkpoint == ReinforcementFailureCheckpoint.RestoredSpellEffect)
            {
                BlessSpellEffect restored = new BlessSpellEffect(hostObject)
                {
                    RemainingTargetTurnStarts = 2,
                };
                SpellEffectController
                    .GetOrAdd(reinforcementObject)
                    .RestoreEffects(new[] { restored });
            }
            ScriptedRollService rolls = new ScriptedRollService(20, 10, 1);
            UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
                new ActionController[] { host, anchor },
                CreateTiles(3),
                rolls
            );
            bridge.StartEncounter("Players");
            RuleDispatcher dispatcher = GetDispatcher(bridge);
            InvalidOperationException expected = new InvalidOperationException(
                $"Injected {checkpoint} post-commit failure."
            );
            IThrowOnceObserver observer;
            IDisposable registration;
            switch (checkpoint)
            {
                case ReinforcementFailureCheckpoint.Join:
                    ThrowOnceFactObserver<EncounterJoinedFact> joinObserver = new(expected);
                    observer = joinObserver;
                    registration = dispatcher.RegisterFactObserver<EncounterJoinedFact>(
                        joinObserver
                    );
                    break;
                case ReinforcementFailureCheckpoint.Strike:
                    ThrowOnceFactObserver<StrikeCombatantRegisteredFact> strikeObserver = new(
                        expected
                    );
                    observer = strikeObserver;
                    registration = dispatcher.RegisterFactObserver<StrikeCombatantRegisteredFact>(
                        strikeObserver
                    );
                    break;
                case ReinforcementFailureCheckpoint.RestoredSpellEffect:
                    ThrowOnceFactObserver<ActiveEffectAdoptedFact> effectObserver = new(expected);
                    observer = effectObserver;
                    registration = dispatcher.RegisterFactObserver<ActiveEffectAdoptedFact>(
                        effectObserver
                    );
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(checkpoint));
            }
            using (registration)
            {
                InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
                    bridge.RegisterCombatants(new ActionController[] { reinforcement })
                );

                Assert.That(actual, Is.SameAs(expected));
                Assert.That(observer.Count, Is.EqualTo(1));
                Assert.That(rolls.Remaining, Is.Zero);
                long failedVersion = bridge.Snapshot.Version;
                Assert.That(reinforcement.HasTurnAuthority, Is.False);
                Assert.Throws<InvalidOperationException>(() =>
                    bridge.ApplyHealing(
                        bridge.GetCreatureId(host),
                        1,
                        RuleSource.FromSlug("pending-enrollment-test")
                    )
                );

                Assert.DoesNotThrow(() =>
                    bridge.RegisterCombatants(new ActionController[] { reinforcement })
                );

                Assert.That(observer.Count, Is.EqualTo(1));
                Assert.That(rolls.Remaining, Is.Zero, "An exact Join replay must not reroll.");
                Assert.That(bridge.GetEncounter().Roster, Has.Count.EqualTo(3));
                Assert.That(
                    bridge
                        .Snapshot.Equipment.Select(pair => pair.Value)
                        .Count(item => item.Holder == bridge.GetCreatureId(reinforcement)),
                    Is.EqualTo(1)
                );
                if (
                    checkpoint == ReinforcementFailureCheckpoint.Strike
                    || checkpoint == ReinforcementFailureCheckpoint.RestoredSpellEffect
                )
                    Assert.That(bridge.Snapshot.Version, Is.EqualTo(failedVersion));
                else
                    Assert.That(bridge.Snapshot.Version, Is.EqualTo(failedVersion + 1));
                if (checkpoint == ReinforcementFailureCheckpoint.RestoredSpellEffect)
                    Assert.That(
                        bridge.Snapshot.ActiveEffects.Count(pair =>
                            pair.Value.DefinitionId
                            == UnitySpellcastingEncounterModule.RestoredTimedEffectDefinitionId
                        ),
                        Is.EqualTo(1)
                    );
            }
            bridge.ReleaseOwnership();
        }
        finally
        {
            Object.DestroyImmediate(hostObject);
            Object.DestroyImmediate(anchorObject);
            Object.DestroyImmediate(reinforcementObject);
        }
    }

    [Test]
    public void PartialInstallationRetryReconcilesOwnedActionsAndPreservesRage()
    {
        GameObject hostObject = new GameObject("partial-install-host");
        GameObject anchorObject = new GameObject("partial-install-anchor");
        GameObject reinforcementObject = new GameObject("partial-install-reinforcement");
        try
        {
            BridgeTestActionController host = ConfigureCombatant(
                hostObject,
                "Players",
                Vector3Int.zero
            );
            BridgeTestActionController anchor = ConfigureCombatant(
                anchorObject,
                "Enemies",
                Vector3Int.right
            );
            RetryActionInstallationModule installation = new();
            UnityCombatRulesBridge bridge = UnityCombatRulesBridge.CreateForTests(
                new ActionController[] { host, anchor },
                CreateTiles(3),
                new ScriptedRollService(20, 10, 1),
                new IUnityEncounterModule[] { installation }
            );
            bridge.StartEncounter("Players");
            BridgeTestActionController reinforcement = ConfigureCombatant(
                reinforcementObject,
                "Enemies",
                new Vector3Int(2, 0, 0)
            );
            ConfigureRetryActionFeatures(reinforcementObject.GetComponent<CreatureComponent>());
            installation.TargetName = reinforcementObject.name;
            installation.FailuresRemaining = 1;

            Assert.Throws<InvalidOperationException>(() =>
                bridge.RegisterCombatants(new ActionController[] { reinforcement })
            );
            Assert.That(
                reinforcement.GetActions().OfType<RetryMarkerAction>().Count(),
                Is.EqualTo(2)
            );
            Assert.That(
                reinforcement.GetActions().OfType<Game.Strikes.RulesStrikeAction>().Count(),
                Is.EqualTo(1)
            );
            Assert.That(
                reinforcement.GetActions().OfType<RulesRageAction>().Count(),
                Is.EqualTo(1)
            );
            Assert.That(
                reinforcement.GetActions().OfType<RulesCastSpellAction>().Count(),
                Is.EqualTo(1)
            );

            Assert.DoesNotThrow(() =>
                bridge.RegisterCombatants(new ActionController[] { reinforcement })
            );

            Assert.That(
                reinforcement.GetActions().OfType<RetryMarkerAction>().Count(),
                Is.EqualTo(1)
            );
            Assert.That(
                reinforcement.GetActions().OfType<Game.Strikes.RulesStrikeAction>().Count(),
                Is.EqualTo(1)
            );
            Assert.That(
                reinforcement.GetActions().OfType<RulesRageAction>().Count(),
                Is.EqualTo(1)
            );
            Assert.That(
                reinforcement.GetActions().OfType<RulesCastSpellAction>().Count(),
                Is.EqualTo(1)
            );
            bridge.ReleaseOwnership();
        }
        finally
        {
            Object.DestroyImmediate(hostObject);
            Object.DestroyImmediate(anchorObject);
            Object.DestroyImmediate(reinforcementObject);
        }
    }

    [Test]
    public void InitialPartialInstallationIsReconciledByFreshEnrollmentRetry()
    {
        GameObject actorObject = new GameObject("initial-partial-install-actor");
        GameObject opponentObject = new GameObject("initial-partial-install-opponent");
        try
        {
            BridgeTestActionController actor = ConfigureCombatant(
                actorObject,
                "Players",
                Vector3Int.zero
            );
            BridgeTestActionController opponent = ConfigureCombatant(
                opponentObject,
                "Enemies",
                Vector3Int.right
            );
            ConfigureRetryActionFeatures(actorObject.GetComponent<CreatureComponent>());
            RetryActionInstallationModule installation = new()
            {
                TargetName = actorObject.name,
                FailuresRemaining = 1,
            };

            Assert.Throws<InvalidOperationException>(() =>
                UnityCombatRulesBridge.CreateForTests(
                    new ActionController[] { actor, opponent },
                    CreateTiles(2),
                    new RandomRollService(),
                    new IUnityEncounterModule[] { installation }
                )
            );
            Assert.That(actor.ActionPoints, Is.Zero);
            Assert.That(actor.GetActions().OfType<RetryMarkerAction>().Count(), Is.EqualTo(2));

            UnityCombatRulesBridge retry = UnityCombatRulesBridge.CreateForTests(
                new ActionController[] { actor, opponent },
                CreateTiles(2),
                new RandomRollService(),
                new IUnityEncounterModule[] { installation }
            );

            Assert.That(actor.GetActions().OfType<RetryMarkerAction>().Count(), Is.EqualTo(1));
            Assert.That(
                actor.GetActions().OfType<Game.Strikes.RulesStrikeAction>().Count(),
                Is.EqualTo(1)
            );
            Assert.That(actor.GetActions().OfType<RulesRageAction>().Count(), Is.EqualTo(1));
            Assert.That(actor.GetActions().OfType<RulesCastSpellAction>().Count(), Is.EqualTo(1));
            retry.ReleaseOwnership();
        }
        finally
        {
            Object.DestroyImmediate(actorObject);
            Object.DestroyImmediate(opponentObject);
        }
    }

    [Test]
    public void RegistrationRejectsBeforePreparationOnceReleaseStartsOrCompletes()
    {
        GameObject hostObject = new GameObject("release-registration-host");
        GameObject candidateObject = new GameObject("release-registration-candidate");
        try
        {
            BridgeTestActionController host = ConfigureCombatant(
                hostObject,
                "Players",
                Vector3Int.zero
            );
            BridgeTestActionController candidate = ConfigureCombatant(
                candidateObject,
                "Enemies",
                Vector3Int.right
            );
            PreparationCountingModule preparation = new();
            UnityCombatRulesBridge bridge = UnityCombatRulesBridge.CreateForTests(
                new ActionController[] { host },
                CreateTiles(2),
                new RandomRollService(),
                new IUnityEncounterModule[] { preparation }
            );
            int preparedBefore = preparation.Count;
            int enumerations = 0;
            IEnumerable<ActionController> CandidateBatch()
            {
                enumerations++;
                yield return candidate;
            }

            bridge.ReleaseOwnership();

            Assert.Throws<InvalidOperationException>(() =>
                bridge.RegisterCombatants(CandidateBatch())
            );
            Assert.That(enumerations, Is.Zero);
            Assert.That(preparation.Count, Is.EqualTo(preparedBefore));

            UnityCombatRulesBridge requested = UnityCombatRulesBridge.CreateForTests(
                new ActionController[] { host },
                CreateTiles(2),
                new RandomRollService(),
                new IUnityEncounterModule[] { preparation }
            );
            Exception registrationFailure = null;
            int requestedEnumerations = 0;
            IEnumerable<ActionController> RequestedBatch()
            {
                requestedEnumerations++;
                yield return candidate;
            }
            GetDispatcher(requested)
                .RegisterFactObserver<HealthFact>(
                    new CallbackFactObserver<HealthFact>(() =>
                    {
                        requested.ReleaseOwnership();
                        registrationFailure = Assert.Throws<InvalidOperationException>(() =>
                            requested.RegisterCombatants(RequestedBatch())
                        );
                    })
                );
            int requestedPreparedBefore = preparation.Count;

            requested.ApplyFinalDamage(
                requested.GetCreatureId(host),
                1,
                RuleSource.FromSlug("release-request-test")
            );

            Assert.That(registrationFailure, Is.Not.Null);
            Assert.That(requestedEnumerations, Is.Zero);
            Assert.That(preparation.Count, Is.EqualTo(requestedPreparedBefore));
            Assert.That(host.ActionPoints, Is.Zero);
        }
        finally
        {
            Object.DestroyImmediate(hostObject);
            Object.DestroyImmediate(candidateObject);
        }
    }

    [Test]
    public void ReleaseDisposesPendingEnrollmentExactlyOnceUnderReentrancyAndFailure()
    {
        GameObject hostObject = new GameObject("pending-release-host");
        GameObject anchorObject = new GameObject("pending-release-anchor");
        GameObject reinforcementObject = new GameObject("pending-release-reinforcement");
        try
        {
            BridgeTestActionController host = ConfigureCombatant(
                hostObject,
                "Players",
                Vector3Int.zero
            );
            BridgeTestActionController anchor = ConfigureCombatant(
                anchorObject,
                "Enemies",
                Vector3Int.right
            );
            PendingLifetimeModule module = new();
            UnityCombatRulesBridge bridge = UnityCombatRulesBridge.CreateForTests(
                new ActionController[] { host, anchor },
                CreateTiles(3),
                new ScriptedRollService(20, 10, 1),
                new IUnityEncounterModule[] { module }
            );
            bridge.StartEncounter("Players");
            BridgeTestActionController reinforcement = ConfigureCombatant(
                reinforcementObject,
                "Enemies",
                new Vector3Int(2, 0, 0)
            );
            InvalidOperationException cleanupFailure = new("pending cleanup failure");
            TrackingDisposable resource = new(() => bridge.ReleaseOwnership(), cleanupFailure);
            module.TargetName = reinforcementObject.name;
            module.Resource = resource;
            module.FailuresRemaining = 1;

            Assert.Throws<InvalidOperationException>(() =>
                bridge.RegisterCombatants(new ActionController[] { reinforcement })
            );

            InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
                bridge.ReleaseOwnership()
            );
            Assert.That(actual, Is.SameAs(cleanupFailure));
            Assert.That(resource.DisposeCount, Is.EqualTo(1));
            Assert.DoesNotThrow(() => bridge.ReleaseOwnership());
            Assert.That(resource.DisposeCount, Is.EqualTo(1));
            Assert.That(reinforcement.ActionPoints, Is.Zero);
        }
        finally
        {
            Object.DestroyImmediate(hostObject);
            Object.DestroyImmediate(anchorObject);
            Object.DestroyImmediate(reinforcementObject);
        }
    }

    private static BridgeTestActionController ConfigureCombatant(
        GameObject combatant,
        string teamName,
        Vector3 position
    )
    {
        combatant.transform.position = position;
        CreatureComponent creature = combatant.AddComponent<CreatureComponent>();
        creature.InitializeHealthBeforeEncounter(10, 10);
        creature.speed = 25;
        Team team = combatant.AddComponent<Team>();
        team.Name = teamName;
        return combatant.AddComponent<BridgeTestActionController>();
    }

    private static CreatureComponent ConfigureUnpreparedCleric(GameObject gameObject)
    {
        CreatureComponent creature = gameObject.AddComponent<CreatureComponent>();
        creature.level = 1;
        creature.attackBonus = 7;
        creature.damageBonus = 4;
        creature.ac = 15;
        creature.weaponBonuses = new List<WeaponBonus>
        {
            new WeaponBonus { category = "simple", bonus = 91 },
        };
        creature.weaponActionBonuses = new List<WeaponActionBonus>
        {
            new WeaponActionBonus { weaponName = "mace", bonus = 92 },
        };
        creature.armorBonuses = new List<ArmorBonus>
        {
            new ArmorBonus { category = "unarmored", bonus = 93 },
        };
        CharacterBuild build = new() { ClassName = "Cleric" };
        build.RuleSelections.Add("existing", "kept");
        build.TrainedSkills.Add("arcana");
        creature.Build = build;
        creature.Prepared = new PreparedCharacter();
        return creature;
    }

    private static UnityCombatantEnrollmentBuilder CreateEnrollmentBuilder(
        ActionController controller,
        CreatureComponent creature,
        CreatureId creatureId,
        CompositeLifetime lifetime
    ) =>
        new(
            controller,
            creature,
            new CreatureState(creatureId, new PlayerId("prepared-purity-player")),
            new HealthState(10, 10),
            new GridPosition(0, 0, 0),
            new GridDistance(25),
            lifetime
        );

    private static void AssertPreparedStateEqual(
        CombatantRulesState expected,
        CombatantRulesState actual
    )
    {
        Assert.That(actual.PreparedInputs.Level, Is.EqualTo(expected.PreparedInputs.Level));
        Assert.That(actual.PreparedInputs.Abilities, Is.EqualTo(expected.PreparedInputs.Abilities));
        Assert.That(
            actual.PreparedInputs.SkillRanks,
            Is.EqualTo(expected.PreparedInputs.SkillRanks)
        );
        Assert.That(actual.PreparedInputs.Equipment, Is.EqualTo(expected.PreparedInputs.Equipment));
        Assert.That(
            actual.PreparedInputs.ArmorCategory,
            Is.EqualTo(expected.PreparedInputs.ArmorCategory)
        );
        Assert.That(actual.PreparedInputs.Traits, Is.EqualTo(expected.PreparedInputs.Traits));
        Assert.That(
            actual.PreparedInputs.Weaknesses,
            Is.EqualTo(expected.PreparedInputs.Weaknesses)
        );
        Assert.That(
            actual.PreparedInputs.Resistances,
            Is.EqualTo(expected.PreparedInputs.Resistances)
        );
        Assert.That(
            actual.PreparedInputs.Immunities,
            Is.EqualTo(expected.PreparedInputs.Immunities)
        );
        Assert.That(
            actual.PreparedInputs.StaticOptions,
            Is.EqualTo(expected.PreparedInputs.StaticOptions)
        );
        Assert.That(
            actual.PreparedInputs.BoundOptions,
            Is.EqualTo(expected.PreparedInputs.BoundOptions)
        );
        Assert.That(
            actual.PreparedInputs.RuleValues,
            Is.EqualTo(expected.PreparedInputs.RuleValues)
        );
        Assert.That(actual.RuleBindings, Is.EqualTo(expected.RuleBindings));
    }

    private static GridPrivate.Tile[,] CreateTiles(int width)
    {
        GridPrivate.Tile[,] tiles = new GridPrivate.Tile[width, 1];
        for (int x = 0; x < width; x++)
            tiles[x, 0] = new GridPrivate.Tile();
        return tiles;
    }

    private static UnityCombatRulesBridge CreateBridge(params CreatureComponent[] creatures)
    {
        ActionController[] controllers = creatures
            .Select(creature =>
            {
                BridgeTestActionController controller =
                    creature.GetComponent<BridgeTestActionController>()
                    ?? creature.gameObject.AddComponent<BridgeTestActionController>();
                Team team =
                    creature.GetComponent<Team>() ?? creature.gameObject.AddComponent<Team>();
                team.Name = "Test Team";
                return (ActionController)controller;
            })
            .ToArray();
        return UnityCombatRulesBridge.Create(
            controllers,
            CreateTiles(Math.Max(1, controllers.Length))
        );
    }

    private static void ConfigureFeatureState(CreatureComponent creature)
    {
        creature.level = 1;
        CharacterBuild build = new CharacterBuild { ClassName = "Barbarian" };
        creature.Build = build;
        PreparedCharacter prepared = Pf2eCharacterPreparer.Prepare(creature, build);
        prepared.SpellBook = new PreparedSpellBook(
            Array.Empty<PreparedSpellEntry>(),
            new[] { new PreparedSpellSlotPool(new SpellSlotPoolId("module-owned-rank-1"), 2) },
            0
        );
        creature.Prepared = prepared;
    }

    private static void ConfigureRetryActionFeatures(CreatureComponent creature)
    {
        ConfigureFeatureState(creature);
        creature.Prepared.SpellBook = new PreparedSpellBook(
            new[]
            {
                PreparedSpellEntry.Cantrip(new SpellReference(new SpellId("divine-lance"), 1)),
            },
            Array.Empty<PreparedSpellSlotPool>(),
            0
        );
        creature.InitializeRuntimeActions();
    }

    private static void AssertFeatureState(RulesSnapshot snapshot, CreatureId creature)
    {
        Assert.That(snapshot.PreparedInputs.Contains(creature), Is.True);
        Assert.That(snapshot.PreparedInputs[creature].Level, Is.EqualTo(1));
        Assert.That(
            snapshot.PreparedInputs[creature].BoundOptions.Select(value => value.Option),
            Does.Contain("item:owned:rage")
        );
        Assert.That(
            snapshot.SpellSlots.Select(pair => pair.Value).Where(slot => slot.Owner == creature),
            Is.EqualTo(
                new[]
                {
                    new SpellSlotState(
                        new SpellSlotPoolId($"{creature.Value}:module-owned-rank-1"),
                        creature,
                        2,
                        2
                    ),
                }
            )
        );
        Assert.That(
            snapshot
                .RuleBindings.Select(pair => pair.Value)
                .Where(binding => binding.Owner == creature)
                .Select(binding => binding.DefinitionId.Value),
            Does.Contain("rage-lifecycle")
        );
    }

    private static TeamRules InitializeTeamRules(GameObject owner)
    {
        TeamRules rules = owner.AddComponent<TeamRules>();
        MethodInfo awake = typeof(TeamRules).BaseType.GetMethod(
            "Awake",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(awake, Is.Not.Null);
        awake.Invoke(rules, null);
        return rules;
    }

    private static RuleDispatcher GetDispatcher(UnityCombatRulesBridge bridge)
    {
        FieldInfo field = typeof(UnityCombatRulesBridge).GetField(
            "dispatcher",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null);
        return (RuleDispatcher)field.GetValue(bridge);
    }

    private sealed class CompletedFailureObserver : IFactObserver<HealthFact>
    {
        private readonly Exception failure;

        public CompletedFailureObserver(Exception failure) => this.failure = failure;

        public ValueTask OnFactCommitted(HealthFact fact, RulesSnapshot currentSnapshot) =>
            new ValueTask(Task.FromException(failure));
    }

    private sealed class IncompleteObserver : IFactObserver<HealthFact>
    {
        private readonly TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public ValueTask OnFactCommitted(HealthFact fact, RulesSnapshot currentSnapshot) =>
            new ValueTask(completion.Task);

        public void Complete() => completion.TrySetResult(true);
    }

    private sealed class RecordingMovementObserver : IFactObserver<TokenMovedFact>
    {
        public List<TokenMovedFact> Facts { get; } = new List<TokenMovedFact>();

        public ValueTask OnFactCommitted(TokenMovedFact fact, RulesSnapshot currentSnapshot)
        {
            Facts.Add(fact);
            return default;
        }
    }

    private sealed class CapturingFailureEnrollmentModule : IUnityCombatantEnrollmentModule
    {
        private readonly Exception failure;

        public CapturingFailureEnrollmentModule(Exception failure) => this.failure = failure;

        public CombatantRulesState Captured { get; private set; }

        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder)
        {
            Captured = builder.BuildState();
            throw failure;
        }
    }


    /// <summary>Identifies a committed reinforcement checkpoint used by retry test cases.</summary>
    public enum ReinforcementFailureCheckpoint
    {
        Join,
        Strike,
        RestoredSpellEffect,
    }

    private interface IThrowOnceObserver
    {
        int Count { get; }
    }

    private sealed class ThrowOnceFactObserver<TFact> : IFactObserver<TFact>, IThrowOnceObserver
        where TFact : RuleFact
    {
        private readonly Exception failure;

        internal ThrowOnceFactObserver(Exception failure) => this.failure = failure;

        public int Count { get; private set; }

        public ValueTask OnFactCommitted(TFact fact, RulesSnapshot currentSnapshot)
        {
            Count++;
            if (Count == 1)
                throw failure;
            return default;
        }
    }

    private sealed class CallbackFactObserver<TFact> : IFactObserver<TFact>
        where TFact : RuleFact
    {
        private readonly Action callback;

        internal CallbackFactObserver(Action callback) => this.callback = callback;

        public ValueTask OnFactCommitted(TFact fact, RulesSnapshot currentSnapshot)
        {
            callback();
            return default;
        }
    }

    private sealed class PreparationCountingModule : IUnityCombatantEnrollmentModule
    {
        internal int Count { get; private set; }

        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder) => Count++;
    }

    private sealed class RetryActionInstallationModule : IUnityCombatantEnrollmentModule
    {
        internal string TargetName { get; set; } = string.Empty;
        internal int FailuresRemaining { get; set; }

        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder)
        {
            if (builder.Controller.gameObject.name != TargetName)
                return;
            builder.AddInstallation(new RetryActionInstallation(this, builder.Controller));
        }

        private sealed class RetryActionInstallation : IUnityCombatantInstallationContribution
        {
            private readonly RetryActionInstallationModule owner;
            private readonly ActionController controller;
            private readonly RetryMarkerAction desired = new();

            internal RetryActionInstallation(
                RetryActionInstallationModule owner,
                ActionController controller
            )
            {
                this.owner = owner;
                this.controller = controller;
            }

            public void Reconcile()
            {
                controller.ReconcileActions(
                    action => action is RetryMarkerAction,
                    new[] { desired }
                );
                if (owner.FailuresRemaining == 0)
                    return;
                owner.FailuresRemaining--;
                controller.AddAction(new RetryMarkerAction());
                throw new InvalidOperationException("Injected partial installation failure.");
            }
        }
    }

    private sealed class PendingLifetimeModule : IUnityCombatantEnrollmentModule
    {
        internal string TargetName { get; set; } = string.Empty;
        internal IDisposable Resource { get; set; }
        internal int FailuresRemaining { get; set; }

        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder)
        {
            if (builder.Controller.gameObject.name != TargetName)
                return;
            builder.Own(Resource);
            builder.AddInstallation(new PendingFailureInstallation(this));
        }

        private sealed class PendingFailureInstallation : IUnityCombatantInstallationContribution
        {
            private readonly PendingLifetimeModule owner;

            internal PendingFailureInstallation(PendingLifetimeModule owner) => this.owner = owner;

            public void Reconcile()
            {
                if (owner.FailuresRemaining == 0)
                    return;
                owner.FailuresRemaining--;
                throw new InvalidOperationException("Injected pending installation failure.");
            }
        }
    }

    private sealed class RetryMarkerAction : EntityAction
    {
        internal RetryMarkerAction()
            : base(0) { }

        public override string ActionName => "Retry Marker";
    }

    private sealed class RecordingEncounterModule
        : IUnityEncounterTurnStartModule,
            IUnityEncounterTopologyModule,
            IUnityCombatantEnrollmentModule
    {
        private readonly string name;
        private readonly IList<string> order;

        public RecordingEncounterModule(string name, IList<string> order)
        {
            this.name = name;
            this.order = order;
            Adapter = new RecordingTurnStartAdapter();
        }

        public IEncounterTurnStartAdapter Adapter { get; }

        public IEncounterTurnStartAdapter CreateTurnStartAdapter() => Adapter;

        public void RefreshTopology(GridPrivate.Tile[,] tiles) => order.Add($"{name}:topology");

        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder) =>
            order.Add($"{name}:{builder.CreatureId.Value}");
    }

    private sealed class RecordingTurnStartAdapter : IEncounterTurnStartAdapter
    {
        public ValueTask<TurnStartContribution> Apply(
            EncounterTurnStartContext context,
            TurnStartContribution current
        ) => new ValueTask<TurnStartContribution>(current);
    }

    private sealed class BridgeTestActionController : ActionController
    {
        public override void EndTurn() { }
    }

    private sealed class TrackingDisposable : IDisposable
    {
        private readonly Action onDispose;
        private readonly Exception failure;

        public TrackingDisposable(Action onDispose, Exception failure)
        {
            this.onDispose = onDispose;
            this.failure = failure;
        }

        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            onDispose();
            throw failure;
        }
    }
}
