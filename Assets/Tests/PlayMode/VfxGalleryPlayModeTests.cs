using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Combat.Spells;
using Game.Creature;
using Game.KayKit;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Light;
using Game.Rules.Unity.Strike;
using Game.Rules.Unity.Vfx;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

public sealed class VfxPersistentPresentationPlayModeTests
{
    [UnityTest]
    public IEnumerator PersistentObserverReplacesRestoresRemovesAndEndsWithoutLeaks()
    {
        GameObject ownerObject = new("Persistent observer owner");
        CreatureComponent owner = ownerObject.AddComponent<CreatureComponent>();
        CreatureId ownerId = new("persistent-observer-owner");
        ActiveEffectInstance effect = new(
            new ActiveEffectId("persistent-observer-effect"),
            new RuleDefinitionId("persistent-observer-definition"),
            ownerId,
            RuleSource.FromSlug("persistent-observer-test"),
            EffectDuration.Indefinite,
            new SpellEffectState(new SpellReference(new SpellId("shield"), 1), ownerId)
        );
        RulesSnapshot snapshot = new InMemoryRulesStore(
            new RulesStateSeed().SeedActiveEffect(effect)
        ).Snapshot;
        Dictionary<CreatureId, CreatureComponent> creatures = new() { [ownerId] = owner };
        using UnityVfxPlayback playback = new(
            new ResourcesVfxPrefabCatalog(),
            "Persistent observer test playback"
        );
        VfxCueId selectedCue = new("spell/shield/persistent");
        using UnityPersistentVfxObserver observer = new(
            playback,
            creatures,
            _ => new PersistentVfxSelection(ownerId, selectedCue)
        );
        ActiveEffectCreatedFact created = new(effect, new BindingId("persistent-binding"));

        observer.OnFactCommitted(created, new OpId(1), snapshot);
        Assert.That(playback.LiveObjectCount, Is.EqualTo(1));
        selectedCue = new VfxCueId("spell/bless/persistent");
        observer.OnFactCommitted(created, new OpId(1), snapshot);
        yield return null;
        Assert.That(playback.LiveObjectCount, Is.EqualTo(1), "Refresh must replace the same key.");

        ActiveEffectRemovedFact removed = new(
            effect,
            new ActiveRuleBinding(
                new BindingId("persistent-binding"),
                effect.DefinitionId,
                ownerId,
                effect.Id,
                effect.Source,
                1
            ),
            ActiveEffectRemovalReason.Expired
        );
        observer.OnFactCommitted(removed, new OpId(1), snapshot);
        observer.OnFactCommitted(removed, new OpId(1), snapshot);
        yield return null;
        Assert.That(
            playback.LiveObjectCount,
            Is.Zero,
            "Expiry and repeated removal are idempotent."
        );

        observer.OnFactCommitted(created, new OpId(2), snapshot);
        Assert.That(
            playback.LiveObjectCount,
            Is.EqualTo(1),
            "Restoration recreates the exact effect."
        );
        observer.OnFactCommitted(
            new EncounterOutcomeCommittedFact(
                new EncounterId("persistent-observer-encounter"),
                EncounterOutcome.PlayerVictory
            ),
            new OpId(2),
            snapshot
        );
        yield return null;
        Assert.That(playback.LiveObjectCount, Is.Zero, "Encounter end removes owned effects.");

        Object.Destroy(ownerObject);
        yield return null;
    }

    [UnityTest]
    public IEnumerator ActionCreationWaitsForResultAndSurvivesThroughTerminalPresentation()
    {
        GameObject ownerObject = new("Post-result persistent owner");
        CreatureComponent owner = ownerObject.AddComponent<CreatureComponent>();
        CreatureId ownerId = new("post-result-persistent-owner");
        ActiveEffectInstance effect = new(
            new ActiveEffectId("post-result-persistent-effect"),
            SpellFeatureRules.ShieldEffect,
            ownerId,
            RuleSource.FromSlug("post-result-persistent-test"),
            EffectDuration.Indefinite,
            new SpellEffectState(new SpellReference(new SpellId("shield"), 1), ownerId)
        );
        ActiveRuleBinding binding = new(
            new BindingId("post-result-persistent-binding"),
            effect.DefinitionId,
            ownerId,
            effect.Id,
            effect.Source,
            1
        );
        RulesSnapshot snapshot = new InMemoryRulesStore(
            new RulesStateSeed().SeedActiveEffect(effect)
        ).Snapshot;
        using UnityVfxPlayback playback = new(
            new ResourcesVfxPrefabCatalog(),
            "Post-result persistent playback"
        );
        using UnityActionPresentationCoordinator coordinator = new();
        using UnityPersistentVfxObserver observer = new(
            playback,
            new Dictionary<CreatureId, CreatureComponent> { [ownerId] = owner },
            SpellPersistentVfxSelector.Select,
            coordinator
        );
        List<string> trace = new();
        playback.CueStarted += cue => trace.Add(cue.Value);
        object action = new();
        OpId rootId = new(7);
        coordinator.Begin(action, rootId);
        coordinator.Enqueue(action, () => Record(() => trace.Add("windup")));
        observer.OnFactCommitted(new ActiveEffectCreatedFact(effect, binding.Id), rootId, snapshot);
        Assert.That(
            playback.LiveObjectCount,
            Is.Zero,
            "Committed creation must wait for the action's resolved presentation."
        );
        Assert.That(
            coordinator.TryEnqueueReaction(rootId, () => Record(() => trace.Add("reaction"))),
            Is.True
        );
        Assert.That(
            coordinator.TryEnqueueAfterAction(
                rootId,
                () => RecordDefeat(ownerObject, playback, trace)
            ),
            Is.True
        );
        coordinator.Enqueue(action, () => Record(() => trace.Add("result")));

        IEnumerator drain = coordinator.Drain(action);
        while (drain.MoveNext())
            yield return drain.Current;

        Assert.That(
            trace,
            Is.EqualTo(
                new[] { "windup", "result", "reaction", "spell/shield/persistent", "defeat" }
            )
        );
        Assert.That(ownerObject.activeSelf, Is.False);
        for (int frame = 0; frame < 30 && playback.LiveObjectCount > 0; frame++)
            yield return null;
        Assert.That(
            playback.LiveObjectCount,
            Is.Zero,
            "Terminal owner loss must end the newly created persistent visual."
        );

        observer.OnFactCommitted(
            new ActiveEffectRemovedFact(effect, binding, ActiveEffectRemovalReason.Expired),
            new OpId(8),
            snapshot
        );
        ownerObject.SetActive(true);
        observer.OnFactCommitted(
            new ActiveEffectCreatedFact(effect, binding.Id),
            new OpId(9),
            snapshot
        );
        Assert.That(
            playback.LiveObjectCount,
            Is.EqualTo(1),
            "Restoration outside an action must project immediately."
        );
        observer.OnFactCommitted(
            new EncounterOutcomeCommittedFact(
                new EncounterId("post-result-persistent-encounter"),
                EncounterOutcome.PlayerVictory
            ),
            new OpId(10),
            snapshot
        );
        Assert.That(playback.LiveObjectCount, Is.Zero);

        Object.Destroy(ownerObject);
        yield return null;
    }

    [UnityTest]
    public IEnumerator LightCreationFollowsActualResultTimelineWithoutTransientOverlap()
    {
        Time.captureDeltaTime = 0.1f;
        GameObject ownerObject = new("Post-result Light owner");
        CreatureComponent owner = ownerObject.AddComponent<CreatureComponent>();
        CreatureId ownerId = new("post-result-light-owner");
        SpellReference lightSpell = new(new SpellId("light"), 1);
        UnitySpellDefinitionCatalog catalog = UnitySpellDefinitionCatalog.Load();
        Assert.That(catalog.TryGetSpell(lightSpell, out SpellDefinition definition), Is.True);
        RuleDefinitionId lightDefinition = definition.Effects.Single().DefinitionId;
        ActiveEffectInstance effect = new(
            new ActiveEffectId("post-result-light-effect"),
            lightDefinition,
            ownerId,
            RuleSource.FromSlug("post-result-light-test"),
            EffectDuration.Indefinite,
            new SpellEffectState(lightSpell, ownerId)
        );
        ActiveRuleBinding binding = new(
            new BindingId("post-result-light-binding"),
            effect.DefinitionId,
            ownerId,
            effect.Id,
            effect.Source,
            1
        );
        RulesSnapshot snapshot = new InMemoryRulesStore(
            new RulesStateSeed().SeedActiveEffect(effect)
        ).Snapshot;
        Dictionary<CreatureId, CreatureComponent> creatures = new() { [ownerId] = owner };
        using UnityVfxPlayback playback = new(
            new ResourcesVfxPrefabCatalog(),
            "Post-result Light playback"
        );
        playback.ConfigureTestTiming(0.01f);
        using UnityActionPresentationCoordinator coordinator = new();
        using UnityLightEffectPresentationObserver observer =
            UnityLightEffectPresentationObserver.Create(catalog, creatures, playback, coordinator);
        UnitySpellActionPresenter presenter = new(creatures, catalog, playback);
        List<string> trace = new();
        List<int> lightCountsAtPersistentCue = new();
        playback.CueStarted += cue =>
        {
            trace.Add(cue.Value);
            if (cue.Value == "spell/light/persistent")
            {
                lightCountsAtPersistentCue.Add(
                    owner.GetComponentsInChildren<UnityEngine.Light>(includeInactive: true).Length
                );
            }
        };
        CastSpellActionOp action = new(
            ownerId,
            lightSpell,
            new SpellActionVariant(2),
            SpellCastSelection.Empty
        );
        CastSpellOutcome outcome = new(
            ownerId,
            lightSpell,
            new[] { effect.Id },
            System.Array.Empty<SpellAttackResolution>()
        );
        OpId rootId = new(22);
        coordinator.Begin(action, rootId);
        coordinator.Enqueue(action, () => presenter.PresentBeginning(action, snapshot));
        observer.OnFactCommitted(new ActiveEffectCreatedFact(effect, binding.Id), rootId, snapshot);
        Assert.That(playback.LiveObjectCount, Is.Zero);
        Assert.That(
            owner.GetComponentsInChildren<UnityEngine.Light>(includeInactive: true),
            Is.Empty,
            "The authoritative Light must wait for resolved presentation."
        );
        coordinator.Enqueue(action, () => presenter.PresentResolved(action, outcome, snapshot));

        int peakLiveVfx = 0;
        IEnumerator drain = coordinator.Drain(action);
        while (drain.MoveNext())
        {
            peakLiveVfx = Mathf.Max(peakLiveVfx, playback.LiveObjectCount);
            yield return drain.Current;
        }
        peakLiveVfx = Mathf.Max(peakLiveVfx, playback.LiveObjectCount);

        Assert.That(
            trace,
            Is.EqualTo(
                new[] { "spell/light/cast", "spell/light/persistent", "spell/light/persistent" }
            )
        );
        Assert.That(
            lightCountsAtPersistentCue,
            Is.EqualTo(new[] { 0, 1 }),
            "The transient result must finish before the authoritative point light is created."
        );
        Assert.That(
            peakLiveVfx,
            Is.EqualTo(1),
            "The transient and persistent Light VFX must never overlap."
        );
        Assert.That(playback.LiveObjectCount, Is.EqualTo(1));
        Assert.That(
            owner.GetComponentsInChildren<UnityEngine.Light>(includeInactive: true),
            Has.Length.EqualTo(1)
        );

        ActiveEffectRemovedFact removed = new(effect, binding, ActiveEffectRemovalReason.Expired);
        observer.OnFactCommitted(removed, new OpId(23), snapshot);
        yield return null;
        Assert.That(playback.LiveObjectCount, Is.Zero);
        Assert.That(
            owner.GetComponentsInChildren<UnityEngine.Light>(includeInactive: true),
            Is.Empty
        );

        observer.OnFactCommitted(
            new ActiveEffectCreatedFact(effect, binding.Id),
            new OpId(24),
            snapshot
        );
        Assert.That(playback.LiveObjectCount, Is.EqualTo(1));
        Assert.That(
            owner.GetComponentsInChildren<UnityEngine.Light>(includeInactive: true),
            Has.Length.EqualTo(1),
            "Restoration outside an action must recreate the exact Light immediately."
        );
        ownerObject.SetActive(false);
        yield return null;
        yield return null;
        Assert.That(playback.LiveObjectCount, Is.Zero);
        Assert.That(
            owner.GetComponentsInChildren<UnityEngine.Light>(includeInactive: true),
            Is.Empty,
            "Disabling the owner must destroy both halves of the Light presentation."
        );

        ownerObject.SetActive(true);
        yield return null;
        Assert.That(
            owner.GetComponentsInChildren<UnityEngine.Light>(includeInactive: true),
            Is.Empty,
            "Re-enabling the owner must not resurrect the stale child point light."
        );
        observer.OnFactCommitted(
            new ActiveEffectCreatedFact(effect, binding.Id),
            new OpId(25),
            snapshot
        );
        Assert.That(playback.LiveObjectCount, Is.EqualTo(1));
        Assert.That(
            owner.GetComponentsInChildren<UnityEngine.Light>(includeInactive: true),
            Has.Length.EqualTo(1),
            "An explicit restoration Fact must recreate the Light after owner reuse."
        );

        Object.Destroy(ownerObject);
        yield return null;
        yield return null;
        Assert.That(playback.LiveObjectCount, Is.Zero);
        Assert.That(
            Object
                .FindObjectsByType<UnityEngine.Light>(FindObjectsSortMode.None)
                .Where(light => light.gameObject.name == "Spell Effect Light"),
            Is.Empty,
            "Destroying the owner must not orphan the authoritative point light."
        );

        Time.captureDeltaTime = 0f;
    }

    [UnityTest]
    public IEnumerator EmptyAreaCastsKeepCastCueWithoutFabricatingAResult()
    {
        Time.captureDeltaTime = 0.1f;
        GameObject casterObject = new("Empty area caster");
        GameObject allyObject = new("Buff target");
        CreatureComponent caster = casterObject.AddComponent<CreatureComponent>();
        CreatureComponent ally = allyObject.AddComponent<CreatureComponent>();
        CreatureId casterId = new("empty-area-caster");
        CreatureId allyId = new("buff-target");
        Dictionary<CreatureId, CreatureComponent> creatures = new()
        {
            [casterId] = caster,
            [allyId] = ally,
        };
        UnitySpellDefinitionCatalog catalog = UnitySpellDefinitionCatalog.Load();
        using UnityVfxPlayback playback = new(
            new ResourcesVfxPrefabCatalog(),
            "Empty area spell playback"
        );
        playback.ConfigureTestTiming(0.01f);
        UnitySpellActionPresenter presenter = new(creatures, catalog, playback);
        List<string> trace = new();
        playback.CueStarted += cue => trace.Add(cue.Value);
        RulesSnapshot snapshot = new InMemoryRulesStore(new RulesStateSeed()).Snapshot;

        SpellReference hymn = new(new SpellId("haunting-hymn"), 1);
        CastSpellActionOp emptyHymn = new(
            casterId,
            hymn,
            new SpellActionVariant(2),
            new SpellCastSelection(System.Array.Empty<CreatureId>(), SpellAreaDirection.East)
        );
        CastSpellOutcome emptyHymnOutcome = new(
            casterId,
            hymn,
            System.Array.Empty<ActiveEffectId>(),
            System.Array.Empty<SpellAttackResolution>()
        );
        SpellReference emptyHealSpell = new(new SpellId("heal"), 1);
        CastSpellActionOp emptyHeal = new(
            casterId,
            emptyHealSpell,
            new SpellActionVariant(3),
            SpellCastSelection.Empty
        );
        CastSpellOutcome emptyHealOutcome = new(
            casterId,
            emptyHealSpell,
            System.Array.Empty<ActiveEffectId>(),
            System.Array.Empty<SpellAttackResolution>()
        );
        SpellReference shield = new(new SpellId("shield"), 1);
        CastSpellActionOp selfEffect = new(
            casterId,
            shield,
            new SpellActionVariant(1),
            SpellCastSelection.Empty
        );
        CastSpellOutcome selfEffectOutcome = new(
            casterId,
            shield,
            new[] { new ActiveEffectId("shield-effect") },
            System.Array.Empty<SpellAttackResolution>()
        );
        SpellReference guidance = new(new SpellId("guidance"), 1);
        CastSpellActionOp selectedBuff = new(
            casterId,
            guidance,
            new SpellActionVariant(1),
            new SpellCastSelection(new[] { allyId })
        );
        CastSpellOutcome selectedBuffOutcome = new(
            casterId,
            guidance,
            new[] { new ActiveEffectId("guidance-effect") },
            System.Array.Empty<SpellAttackResolution>()
        );

        IEnumerator[] timelines =
        {
            presenter.PresentBeginning(emptyHymn, snapshot),
            presenter.PresentResolved(emptyHymn, emptyHymnOutcome, snapshot),
            presenter.PresentBeginning(emptyHeal, snapshot),
            presenter.PresentResolved(emptyHeal, emptyHealOutcome, snapshot),
            presenter.PresentBeginning(selfEffect, snapshot),
            presenter.PresentResolved(selfEffect, selfEffectOutcome, snapshot),
            presenter.PresentBeginning(selectedBuff, snapshot),
            presenter.PresentResolved(selectedBuff, selectedBuffOutcome, snapshot),
        };
        foreach (IEnumerator timeline in timelines)
        {
            using (timeline as System.IDisposable)
            {
                while (timeline.MoveNext())
                    yield return timeline.Current;
            }
        }

        Assert.That(
            trace,
            Is.EqualTo(
                new[]
                {
                    "spell/haunting-hymn/cast",
                    "spell/heal/cast",
                    "spell/shield/cast",
                    "spell/shield/persistent",
                    "spell/guidance/cast",
                    "spell/guidance/persistent",
                }
            )
        );
        Assert.That(playback.LiveObjectCount, Is.Zero);

        Object.Destroy(casterObject);
        Object.Destroy(allyObject);
        yield return null;
        Time.captureDeltaTime = 0f;
    }

    [UnityTest]
    public IEnumerator HealResultCuesUseCommittedMixedAreaOutcomesAfterTraitsChange()
    {
        Time.captureDeltaTime = 0.1f;
        GameObject casterObject = new("Committed Heal caster");
        GameObject livingTargetObject = new("Committed living Heal target");
        GameObject undeadTargetObject = new("Committed undead Heal target");
        CreatureComponent caster = casterObject.AddComponent<CreatureComponent>();
        CreatureComponent livingTarget = livingTargetObject.AddComponent<CreatureComponent>();
        CreatureComponent undeadTarget = undeadTargetObject.AddComponent<CreatureComponent>();
        CreatureId casterId = new("committed-heal-caster");
        CreatureId livingTargetId = new("committed-heal-living-target");
        CreatureId undeadTargetId = new("committed-heal-undead-target");
        Dictionary<CreatureId, CreatureComponent> creatures = new()
        {
            [casterId] = caster,
            [livingTargetId] = livingTarget,
            [undeadTargetId] = undeadTarget,
        };
        using UnityVfxPlayback playback = new(
            new ResourcesVfxPrefabCatalog(),
            "Committed Heal outcome playback"
        );
        playback.ConfigureTestTiming(0.01f);
        UnitySpellActionPresenter presenter = new(
            creatures,
            UnitySpellDefinitionCatalog.Load(),
            playback
        );
        List<string> trace = new();
        playback.CueStarted += cue => trace.Add(cue.Value);
        SpellReference spell = new(new SpellId("heal"), 1);
        CastSpellActionOp action = new(
            casterId,
            spell,
            new SpellActionVariant(3),
            new SpellCastSelection(new[] { casterId, livingTargetId, undeadTargetId })
        );
        CastSpellOutcome outcome = new(
            casterId,
            spell,
            System.Array.Empty<ActiveEffectId>(),
            System.Array.Empty<SpellAttackResolution>(),
            new[]
            {
                new SpellTargetResolution(
                    casterId,
                    null,
                    System.Array.Empty<TypedDamagePart>(),
                    0,
                    false
                ),
                new SpellTargetResolution(
                    livingTargetId,
                    null,
                    System.Array.Empty<TypedDamagePart>(),
                    0,
                    false
                ),
                new SpellTargetResolution(
                    undeadTargetId,
                    Game.Rules.Runtime.DegreeOfSuccess.CriticalSuccess,
                    new[] { new TypedDamagePart("vitality", 0, new[] { "heal" }) },
                    0,
                    false
                ),
            }
        );

        livingTarget.traits = new List<string> { "undead" };
        undeadTarget.traits = new List<string>();
        IEnumerator timeline = presenter.PresentResolved(
            action,
            outcome,
            new InMemoryRulesStore(new RulesStateSeed()).Snapshot
        );
        using (timeline as System.IDisposable)
        {
            while (timeline.MoveNext())
                yield return timeline.Current;
        }

        Assert.That(
            trace,
            Is.EqualTo(
                new[]
                {
                    "spell/heal/3-action-emanation",
                    "spell/heal/3-action-living",
                    "spell/heal/3-action-living",
                    "spell/heal/3-action-undead-critical-success",
                }
            ),
            "Zero healing and fully resisted undead damage must retain their committed classifications."
        );

        Object.Destroy(casterObject);
        Object.Destroy(livingTargetObject);
        Object.Destroy(undeadTargetObject);
        Time.captureDeltaTime = 0f;
        yield return null;
    }

    [UnityTest]
    public IEnumerator LightDisposeCleansAVisualWhoseQueuedRemovalWasAborted()
    {
        GameObject ownerObject = new("Aborted Light removal owner");
        CreatureComponent owner = ownerObject.AddComponent<CreatureComponent>();
        CreatureId ownerId = new("aborted-light-removal-owner");
        SpellReference lightSpell = new(new SpellId("light"), 1);
        UnitySpellDefinitionCatalog catalog = UnitySpellDefinitionCatalog.Load();
        Assert.That(catalog.TryGetSpell(lightSpell, out SpellDefinition definition), Is.True);
        ActiveEffectInstance effect = new(
            new ActiveEffectId("aborted-light-removal-effect"),
            definition.Effects.Single().DefinitionId,
            ownerId,
            RuleSource.FromSlug("aborted-light-removal-test"),
            EffectDuration.Indefinite,
            new SpellEffectState(lightSpell, ownerId)
        );
        ActiveRuleBinding binding = new(
            new BindingId("aborted-light-removal-binding"),
            effect.DefinitionId,
            ownerId,
            effect.Id,
            effect.Source,
            1
        );
        RulesSnapshot snapshot = new InMemoryRulesStore(
            new RulesStateSeed().SeedActiveEffect(effect)
        ).Snapshot;
        using UnityVfxPlayback playback = new(
            new ResourcesVfxPrefabCatalog(),
            "Aborted Light removal playback"
        );
        using UnityActionPresentationCoordinator coordinator = new();
        using UnityLightEffectPresentationObserver observer =
            UnityLightEffectPresentationObserver.Create(
                catalog,
                new Dictionary<CreatureId, CreatureComponent> { [ownerId] = owner },
                playback,
                coordinator
            );

        observer.OnFactCommitted(
            new ActiveEffectCreatedFact(effect, binding.Id),
            new OpId(30),
            snapshot
        );
        Assert.That(playback.LiveObjectCount, Is.EqualTo(1));
        Assert.That(
            owner.GetComponentsInChildren<UnityEngine.Light>(includeInactive: true),
            Has.Length.EqualTo(1)
        );

        object action = new();
        OpId removalRoot = new(31);
        coordinator.Begin(action, removalRoot);
        observer.OnFactCommitted(
            new ActiveEffectRemovedFact(effect, binding, ActiveEffectRemovalReason.Expired),
            removalRoot,
            snapshot
        );
        Assert.That(
            owner.GetComponentsInChildren<UnityEngine.Light>(includeInactive: true),
            Has.Length.EqualTo(1),
            "The point light remains until the queued removal drains or ownership is disposed."
        );

        observer.Dispose();
        coordinator.Dispose();
        yield return null;
        yield return null;
        Assert.That(playback.LiveObjectCount, Is.Zero);
        Assert.That(
            owner.GetComponentsInChildren<UnityEngine.Light>(includeInactive: true),
            Is.Empty,
            "Observer disposal must clean instantiated visuals even after ownership was removed."
        );

        Object.Destroy(ownerObject);
        yield return null;
    }

    [UnityTest]
    public IEnumerator PersistentObserverDisposeCleansAVisualAfterItsQueuedRemovalIsAborted()
    {
        GameObject ownerObject = new("Aborted persistent removal owner");
        CreatureComponent owner = ownerObject.AddComponent<CreatureComponent>();
        CreatureId ownerId = new("aborted-persistent-removal-owner");
        ActiveEffectInstance effect = new(
            new ActiveEffectId("aborted-persistent-removal-effect"),
            SpellFeatureRules.GuidanceEffect,
            ownerId,
            RuleSource.FromSlug("aborted-persistent-removal-test"),
            EffectDuration.Indefinite,
            new SpellEffectState(new SpellReference(new SpellId("guidance"), 1), ownerId)
        );
        ActiveRuleBinding binding = new(
            new BindingId("aborted-persistent-removal-binding"),
            effect.DefinitionId,
            ownerId,
            effect.Id,
            effect.Source,
            1
        );
        RulesSnapshot snapshot = new InMemoryRulesStore(
            new RulesStateSeed().SeedActiveEffect(effect)
        ).Snapshot;
        UnityVfxPlayback playback = new(
            new ResourcesVfxPrefabCatalog(),
            "Shared aborted persistent removal playback"
        );
        UnityActionPresentationCoordinator coordinator = new();
        UnityPersistentVfxObserver observer = new(
            playback,
            new Dictionary<CreatureId, CreatureComponent> { [ownerId] = owner },
            SpellPersistentVfxSelector.Select,
            coordinator
        );

        observer.OnFactCommitted(
            new ActiveEffectCreatedFact(effect, binding.Id),
            new OpId(40),
            snapshot
        );
        Assert.That(playback.LiveObjectCount, Is.EqualTo(1));

        object action = new();
        OpId removalRoot = new(41);
        coordinator.Begin(action, removalRoot);
        coordinator.Enqueue(action, FailingPresentation);
        observer.OnFactCommitted(
            new ActiveEffectRemovedFact(effect, binding, ActiveEffectRemovalReason.Ended),
            removalRoot,
            snapshot
        );
        LogAssert.Expect(LogType.Exception, new Regex("Synthetic presentation failure\\."));

        IEnumerator drain = coordinator.Drain(action);
        while (drain.MoveNext())
            yield return drain.Current;

        Assert.That(
            playback.LiveObjectCount,
            Is.EqualTo(1),
            "The aborted queued callback has not yet removed the authoritative visual."
        );
        observer.Dispose();
        coordinator.Dispose();
        Assert.That(
            playback.LiveObjectCount,
            Is.Zero,
            "Observer disposal must immediately clean a pending removal after sequence failure."
        );
        Assert.That(
            GameObject.Find("Shared aborted persistent removal playback"),
            Is.Not.Null,
            "Cleanup must not require disposal of the shared playback session."
        );

        playback.Dispose();
        Object.Destroy(ownerObject);
        yield return null;
    }

    [UnityTest]
    public IEnumerator SelectedBuffResultsUseRecipientAnchorsWhileInfuseUsesDeliveryBeams()
    {
        Time.captureDeltaTime = 0.1f;
        GameObject casterObject = new("Selected buff caster");
        GameObject firstAllyObject = new("Selected buff ally one");
        GameObject secondAllyObject = new("Selected buff ally two");
        casterObject.transform.position = new Vector3(-2f, 0f, -1f);
        firstAllyObject.transform.position = new Vector3(2f, 0f, 1f);
        secondAllyObject.transform.position = new Vector3(0.5f, 0f, 3f);
        CreatureComponent caster = casterObject.AddComponent<CreatureComponent>();
        CreatureComponent firstAlly = firstAllyObject.AddComponent<CreatureComponent>();
        CreatureComponent secondAlly = secondAllyObject.AddComponent<CreatureComponent>();
        CreatureId casterId = new("selected-buff-caster");
        CreatureId firstAllyId = new("selected-buff-ally-one");
        CreatureId secondAllyId = new("selected-buff-ally-two");
        Dictionary<CreatureId, CreatureComponent> creatures = new()
        {
            [casterId] = caster,
            [firstAllyId] = firstAlly,
            [secondAllyId] = secondAlly,
        };
        using UnityVfxPlayback playback = new(
            new ResourcesVfxPrefabCatalog(),
            "Selected buff anchor playback"
        );
        playback.ConfigureTestTiming(0.01f);
        UnitySpellActionPresenter presenter = new(
            creatures,
            UnitySpellDefinitionCatalog.Load(),
            playback
        );
        RulesSnapshot snapshot = new InMemoryRulesStore(new RulesStateSeed()).Snapshot;
        List<(VfxCueId Cue, Vector3 Origin, Vector3 Destination)> starts = new();
        playback.TransientStarted += (cue, origin, destination) =>
            starts.Add((cue, origin, destination));
        var scenarios = new[]
        {
            (
                Name: "Guidance self",
                Spell: new SpellId("guidance"),
                Actions: 1,
                Targets: new[] { casterId },
                Travels: false
            ),
            (
                Name: "Guidance ally",
                Spell: new SpellId("guidance"),
                Actions: 1,
                Targets: new[] { firstAllyId },
                Travels: false
            ),
            (
                Name: "Bless multiple recipients",
                Spell: new SpellId("bless"),
                Actions: 2,
                Targets: new[] { casterId, firstAllyId, secondAllyId },
                Travels: false
            ),
            (
                Name: "Infuse one action",
                Spell: new SpellId("infuse-vitality"),
                Actions: 1,
                Targets: new[] { firstAllyId },
                Travels: true
            ),
            (
                Name: "Infuse two actions",
                Spell: new SpellId("infuse-vitality"),
                Actions: 2,
                Targets: new[] { firstAllyId, secondAllyId },
                Travels: true
            ),
            (
                Name: "Infuse three actions including self",
                Spell: new SpellId("infuse-vitality"),
                Actions: 3,
                Targets: new[] { casterId, firstAllyId, secondAllyId },
                Travels: true
            ),
        };

        foreach (var scenario in scenarios)
        {
            starts.Clear();
            SpellReference spell = new(scenario.Spell, 1);
            CastSpellActionOp operation = new(
                casterId,
                spell,
                new SpellActionVariant(scenario.Actions),
                new SpellCastSelection(scenario.Targets)
            );
            CastSpellOutcome outcome = new(
                casterId,
                spell,
                scenario
                    .Targets.Select(
                        (_, index) =>
                            new ActiveEffectId($"{scenario.Spell.Value}-{scenario.Actions}-{index}")
                    )
                    .ToArray(),
                System.Array.Empty<SpellAttackResolution>()
            );
            IEnumerator timeline = presenter.PresentResolved(operation, outcome, snapshot);
            using (timeline as System.IDisposable)
            {
                while (timeline.MoveNext())
                    yield return timeline.Current;
            }

            Assert.That(starts, Has.Count.EqualTo(scenario.Targets.Length), scenario.Name);
            VfxCueId expectedCue = SpellVfxCueSelector.GetResult(scenario.Spell, scenario.Actions);
            Vector3 casterPosition = caster.transform.position + Vector3.up * 0.6f;
            for (int index = 0; index < scenario.Targets.Length; index++)
            {
                var started = starts[index];
                Vector3 targetPosition =
                    creatures[scenario.Targets[index]].transform.position + Vector3.up * 0.6f;
                Assert.That(started.Cue, Is.EqualTo(expectedCue), scenario.Name);
                Assert.That(
                    Vector3.Distance(
                        started.Origin,
                        scenario.Travels ? casterPosition : targetPosition
                    ),
                    Is.LessThan(0.001f),
                    scenario.Name + " origin"
                );
                Assert.That(
                    Vector3.Distance(started.Destination, targetPosition),
                    Is.LessThan(0.001f),
                    scenario.Name + " destination"
                );
            }
        }

        Object.Destroy(casterObject);
        Object.Destroy(firstAllyObject);
        Object.Destroy(secondAllyObject);
        yield return null;
        Time.captureDeltaTime = 0f;
    }

    [UnityTest]
    public IEnumerator GuidanceConsumptionPlaysTransientPulseAndLeavesNoPersistentObject()
    {
        Time.captureDeltaTime = 0.1f;
        GameObject ownerObject = new("Guidance pulse owner");
        CreatureComponent owner = ownerObject.AddComponent<CreatureComponent>();
        CreatureId ownerId = new("guidance-pulse-owner");
        ActiveEffectInstance effect = new(
            new ActiveEffectId("guidance-pulse-effect"),
            SpellFeatureRules.GuidanceEffect,
            ownerId,
            RuleSource.FromSlug("guidance-pulse-test"),
            EffectDuration.Indefinite,
            new SpellEffectState(new SpellReference(new SpellId("guidance"), 1), ownerId)
        );
        ActiveRuleBinding binding = new(
            new BindingId("guidance-pulse-binding"),
            effect.DefinitionId,
            ownerId,
            effect.Id,
            effect.Source,
            1
        );
        RulesSnapshot snapshot = new InMemoryRulesStore(
            new RulesStateSeed().SeedActiveEffect(effect)
        ).Snapshot;
        using UnityVfxPlayback playback = new(
            new ResourcesVfxPrefabCatalog(),
            "Guidance pulse playback"
        );
        playback.ConfigureTestTiming(0.01f);
        List<string> trace = new();
        playback.CueStarted += cue => trace.Add(cue.Value);
        using UnityPersistentVfxObserver observer = new(
            playback,
            new Dictionary<CreatureId, CreatureComponent> { [ownerId] = owner },
            SpellPersistentVfxSelector.Select
        );

        observer.OnFactCommitted(
            new ActiveEffectCreatedFact(effect, binding.Id),
            new OpId(11),
            snapshot
        );
        observer.OnFactCommitted(
            new ActiveEffectRemovedFact(effect, binding, ActiveEffectRemovalReason.Ended),
            new OpId(12),
            snapshot
        );
        for (int frame = 0; frame < 30 && playback.LiveObjectCount > 0; frame++)
            yield return null;

        Assert.That(
            trace,
            Is.EqualTo(new[] { "spell/guidance/persistent", "spell/guidance/consume" })
        );
        Assert.That(playback.LiveObjectCount, Is.Zero);
        Time.captureDeltaTime = 0f;
        Object.Destroy(ownerObject);
        yield return null;
    }

    [UnityTest]
    public IEnumerator InfuseVitalityPersistentVisualFollowsTheSelectedHandAnchor()
    {
        GameObject ownerObject = new("Infuse anchor owner");
        CreatureComponent owner = ownerObject.AddComponent<CreatureComponent>();
        Transform hand = new GameObject("Right Hand Presentation Anchor").transform;
        hand.SetParent(ownerObject.transform, false);
        hand.localPosition = new Vector3(0.45f, 1.1f, 0.2f);
        CreatureId ownerId = new("infuse-anchor-owner");
        ActiveEffectInstance effect = new(
            new ActiveEffectId("infuse-anchor-effect"),
            SpellFeatureRules.InfuseVitalityEffect,
            ownerId,
            RuleSource.FromSlug("infuse-anchor-test"),
            EffectDuration.Indefinite,
            new SpellEffectState(new SpellReference(new SpellId("infuse-vitality"), 1), ownerId)
        );
        RulesSnapshot snapshot = new InMemoryRulesStore(
            new RulesStateSeed().SeedActiveEffect(effect)
        ).Snapshot;
        using UnityVfxPlayback playback = new(
            new ResourcesVfxPrefabCatalog(),
            "Infuse anchor playback"
        );
        using UnityPersistentVfxObserver observer = new(
            playback,
            new Dictionary<CreatureId, CreatureComponent> { [ownerId] = owner },
            SpellPersistentVfxSelector.Select
        );

        observer.OnFactCommitted(
            new ActiveEffectCreatedFact(effect, new BindingId("infuse-anchor-binding")),
            new OpId(21),
            snapshot
        );
        yield return null;
        UnityVfxInstance instance = Object
            .FindObjectsByType<UnityVfxInstance>(FindObjectsSortMode.None)
            .Single(value => value.name == "Persistent VFX " + effect.Id.Value);

        Assert.That(
            Vector3.Distance(instance.transform.position, hand.position),
            Is.LessThan(0.01f)
        );
        hand.position += Vector3.right;
        hand.rotation = Quaternion.Euler(0f, 65f, 0f);
        yield return null;
        Assert.That(
            Vector3.Distance(instance.transform.position, hand.position),
            Is.LessThan(0.01f)
        );
        Assert.That(
            Quaternion.Angle(instance.transform.rotation, hand.rotation),
            Is.LessThan(0.1f)
        );

        Object.Destroy(ownerObject);
        yield return null;
    }

    private static IEnumerator Record(System.Action record)
    {
        record();
        yield break;
    }

    private static IEnumerator FailingPresentation()
    {
        yield return null;
        throw new System.InvalidOperationException("Synthetic presentation failure.");
    }

    private static IEnumerator RecordDefeat(
        GameObject owner,
        UnityVfxPlayback playback,
        ICollection<string> trace
    )
    {
        Assert.That(
            owner.activeSelf,
            Is.True,
            "Post-result creation must run before terminal defeat presentation."
        );
        Assert.That(
            playback.LiveObjectCount,
            Is.EqualTo(1),
            "The post-result persistent visual must exist before terminal defeat."
        );
        trace.Add("defeat");
        owner.SetActive(false);
        yield break;
    }
}

public sealed class VfxGalleryPlayModeTests
{
    [UnityTest]
    public IEnumerator GalleryIteratesEveryProductionEntryAndResetLeavesNoObjects()
    {
        yield return SceneManager.LoadSceneAsync("VfxGallery", LoadSceneMode.Single);
        VfxGalleryController gallery = Object.FindFirstObjectByType<VfxGalleryController>();
        Assert.That(gallery, Is.Not.Null);
        Assert.That(gallery.EntryCount, Is.EqualTo(VfxCoverageManifest.Load().entries.Count));
        gallery.ConfigureTestTiming(0.01f);
        Time.captureDeltaTime = 0.1f;
        int defeatCountBefore = gallery.DefeatPresentationCount;
        int expectedDefeats = VfxCoverageManifest
            .Load()
            .entries.Count(entry => entry.category == "strike" && entry.outcome == "critical");
        int strikeDamageFactsBefore = gallery.TotalStrikeDamageFactCount;
        int expectedStrikeDamageFacts = VfxCoverageManifest
            .Load()
            .entries.Count(entry =>
                (
                    entry.id.StartsWith("strike/", System.StringComparison.Ordinal)
                    && entry.outcome is "hit" or "critical"
                )
                || entry.id
                    is "auxiliary/sneak-attack/hit"
                        or "auxiliary/infuse-vitality-strike/hit"
            );

        yield return gallery.PlayAllEntries();
        Assert.That(
            gallery.DefeatPresentationCount - defeatCountBefore,
            Is.EqualTo(expectedDefeats),
            "Play All must replay terminal presentation for every critical Strike fixture."
        );
        Assert.That(
            gallery.TotalStrikeDamageFactCount - strikeDamageFactsBefore,
            Is.EqualTo(expectedStrikeDamageFacts),
            "Play All must emit damage Facts only for Strikes with applied damage."
        );
        gallery.ResetGallery();
        yield return null;
        yield return null;

        Assert.That(gallery.LiveVfxObjectCount, Is.Zero);
        Time.captureDeltaTime = 0f;
        Scene cleanup = SceneManager.CreateScene("Vfx Gallery Test Cleanup");
        SceneManager.SetActiveScene(cleanup);
        yield return SceneManager.UnloadSceneAsync("VfxGallery");
    }

    [UnityTest]
    public IEnumerator EveryManifestEntryUsesItsProductionCueAndExactTargetCount()
    {
        yield return SceneManager.LoadSceneAsync("VfxGallery", LoadSceneMode.Single);
        VfxGalleryController gallery = Object.FindFirstObjectByType<VfxGalleryController>();
        VfxCoverageManifest manifest = VfxCoverageManifest.Load();
        gallery.ConfigureTestTiming(0.01f);
        Time.captureDeltaTime = 0.1f;
        Dictionary<string, Transform> galleryActors = Object
            .FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(actor =>
                actor.name
                    is "Gallery Source"
                        or "Gallery Target"
                        or "Gallery Target 2"
                        or "Gallery Target 3"
            )
            .ToDictionary(actor => actor.name, System.StringComparer.Ordinal);

        foreach (VfxCoverageEntry entry in manifest.entries)
        {
            gallery.Select(entry.id);
            yield return gallery.PlaySelectedForTests();
            Assert.That(
                gallery.LastTrace,
                Does.Contain(entry.cue),
                entry.id + " did not traverse its declared production cue."
            );
            int expectedTargets =
                entry.id.Contains("infuse-vitality/3-action") ? 3
                : entry.id.Contains("infuse-vitality/2-action") ? 2
                : entry.id is "spell/heal/3-action/living" or "spell/heal/3-action/area-wave" ? 3
                : entry.id.StartsWith("spell/light/")
                || entry.id.StartsWith("spell/shield/")
                || entry.id.StartsWith("spell/bless/")
                    ? 0
                : 1;
            Assert.That(gallery.CurrentTargetCount, Is.EqualTo(expectedTargets), entry.id);
            if (entry.id.StartsWith("spell/", System.StringComparison.Ordinal))
            {
                Assert.That(
                    gallery.LatestSpellSelectionValidation,
                    Is.TypeOf<ActionValidationResult.ValidActionValidationResult>(),
                    entry.id + " must pass the real production targeting-profile validator."
                );
                Assert.That(
                    gallery.LatestSelection,
                    Is.EqualTo(gallery.LatestSpellSelection.Creatures),
                    entry.id + " must present the same creatures its action selected."
                );
                if (
                    gallery.LatestSpellProfile.Kind
                    is SpellSelectionKind.SingleCreature
                        or SpellSelectionKind.ExactCreatureCount
                )
                {
                    int requiredCount =
                        gallery.LatestSpellProfile.ExactCreatureCount > 0
                            ? gallery.LatestSpellProfile.ExactCreatureCount
                            : 1;
                    Assert.That(
                        gallery.LatestSpellSelection.Creatures,
                        Has.Count.EqualTo(requiredCount),
                        entry.id + " must satisfy its production profile's exact target count."
                    );
                }
                float maximumFeet =
                    gallery.LatestSpellProfile.RangeFeet > 0
                        ? gallery.LatestSpellProfile.RangeFeet
                        : gallery.LatestSpellProfile.AreaFeet;
                foreach (
                    CreatureId creature in gallery.LatestSpellSelection.Creatures.Where(creature =>
                        creature.Value != "vfx-gallery-source"
                    )
                )
                {
                    string objectName = creature.Value switch
                    {
                        "vfx-gallery-target-1" => "Gallery Target",
                        "vfx-gallery-target-2" => "Gallery Target 2",
                        "vfx-gallery-target-3" => "Gallery Target 3",
                        _ => throw new AssertionException(
                            $"Unknown gallery selection actor '{creature.Value}'."
                        ),
                    };
                    float distanceFeet =
                        Vector3.Distance(
                            galleryActors["Gallery Source"].position,
                            galleryActors[objectName].position
                        ) * 5f;
                    Assert.That(
                        distanceFeet,
                        Is.LessThanOrEqualTo(maximumFeet + 0.001f),
                        entry.id
                            + " must stage every selected creature inside its production profile."
                    );
                    if (gallery.LatestSpellProfile.Kind == SpellSelectionKind.Cone)
                        Assert.That(
                            galleryActors[objectName].position.x,
                            Is.GreaterThan(galleryActors["Gallery Source"].position.x),
                            entry.id + " must stage its target in the selected eastward cone."
                        );
                }
            }
            if (entry.id.StartsWith("spell/infuse-vitality/"))
            {
                string beamCue = SpellVfxCueSelector
                    .GetResult(new SpellId("infuse-vitality"), expectedTargets)
                    .Value;
                string[] expectedTrace = new[] { "spell/infuse-vitality/cast" }
                    .Concat(Enumerable.Repeat(beamCue, expectedTargets))
                    .Concat(Enumerable.Repeat("spell/infuse-vitality/persistent", expectedTargets))
                    .ToArray();
                Assert.That(
                    gallery.LastTrace,
                    Is.EqualTo(expectedTrace),
                    entry.id + " must complete every delivery beam before creating coatings."
                );
                Assert.That(
                    gallery.LastTrace.Count(cue => cue == entry.cue),
                    Is.EqualTo(expectedTargets),
                    entry.id + " must emit one simultaneous production beam per selected target."
                );
                Assert.That(
                    gallery.CurrentEffectOwners.Count,
                    Is.EqualTo(expectedTargets),
                    entry.id + " must create one production coating effect per selected target."
                );
                Assert.That(
                    gallery.CurrentEffectOwners.Distinct().Count(),
                    Is.EqualTo(expectedTargets),
                    entry.id + " must coat distinct selected targets."
                );
                Assert.That(
                    gallery.LiveVfxObjectCount,
                    Is.EqualTo(expectedTargets),
                    entry.id + " must leave every selected coating visibly active."
                );
            }
            if (entry.id.StartsWith("spell/bless/"))
            {
                Assert.That(
                    gallery.LatestSelection.Select(creature => creature.Value),
                    Is.EqualTo(new[] { "vfx-gallery-source" }),
                    "Bless's emanation fixture must include its caster like production targeting."
                );
            }
            if (entry.id.StartsWith("spell/haunting-hymn/"))
            {
                Assert.That(gallery.LatestSpellProfile.Kind, Is.EqualTo(SpellSelectionKind.Cone));
                Assert.That(gallery.LatestSpellSelection.HasAreaDirection, Is.True, entry.id);
                Assert.That(
                    gallery.LatestSpellSelection.AreaDirection,
                    Is.EqualTo(SpellAreaDirection.East),
                    entry.id
                );
            }
            if (entry.id.StartsWith("spell/heal/2-action/"))
            {
                Assert.That(
                    gallery.LastTrace.Count(cue => cue == "spell/heal/2-action-delivery"),
                    Is.EqualTo(1),
                    entry.id + " must emit one ranged delivery before its target result."
                );
            }
            if (entry.id.StartsWith("spell/heal/3-action/"))
            {
                Assert.That(
                    gallery.LatestSpellProfile.Kind,
                    Is.EqualTo(SpellSelectionKind.Emanation),
                    entry.id
                );
                Assert.That(gallery.LatestSpellProfile.IncludeCaster, Is.True, entry.id);
                Assert.That(
                    gallery.LatestSelection.Select(creature => creature.Value),
                    Does.Contain("vfx-gallery-source"),
                    entry.id + " must include Heal's caster in the emanation selection."
                );
                Assert.That(
                    gallery.LastTrace.Count(cue => cue == "spell/heal/3-action-emanation"),
                    Is.EqualTo(1),
                    entry.id + " must emit exactly one area wave."
                );
            }
            if (
                entry.id.StartsWith("spell/", System.StringComparison.Ordinal)
                && entry.outcome == "create active"
            )
            {
                Assert.That(
                    gallery.LastTrace[0],
                    Is.EqualTo("spell/" + entry.id.Split('/')[1] + "/cast"),
                    entry.id + " must present windup before its persistent result."
                );
            }
            if (entry.outcome == "miss")
            {
                Assert.That(
                    gallery.LastTrace.Any(cue => cue.EndsWith("/hit") || cue.EndsWith("/critical")),
                    Is.False,
                    entry.id + " must suppress contact VFX."
                );
            }
            bool shouldRemainActive =
                entry.outcome
                is "create active"
                    or "restore active"
                    or "refresh active"
                    or "active";
            if (entry.category == "lifecycle" || entry.variant.Contains("persistent"))
            {
                int expectedPersistentInstances =
                    shouldRemainActive && entry.id.StartsWith("spell/infuse-vitality/")
                        ? expectedTargets
                    : shouldRemainActive ? 1
                    : 0;
                Assert.That(
                    gallery.LiveVfxObjectCount,
                    Is.EqualTo(expectedPersistentInstances),
                    entry.id + " has an incorrect authoritative persistent lifetime."
                );
            }
        }

        gallery.ResetGallery();
        yield return null;
        yield return null;
        Assert.That(gallery.LiveVfxObjectCount, Is.Zero);
        Assert.That(gallery.LastTrace, Is.Empty);
        Time.captureDeltaTime = 0f;
        Scene cleanup = SceneManager.CreateScene("VFX Gallery Coverage Cleanup");
        SceneManager.SetActiveScene(cleanup);
        yield return SceneManager.UnloadSceneAsync("VfxGallery");
    }

    [UnityTest]
    public IEnumerator HealDeliveryAndHymnResultsUseTheirCorrectSourceAndTargetAnchors()
    {
        yield return SceneManager.LoadSceneAsync("VfxGallery", LoadSceneMode.Single);
        VfxGalleryController gallery = Object.FindFirstObjectByType<VfxGalleryController>();
        gallery.ConfigureTestTiming(0.01f);
        Time.captureDeltaTime = 0.1f;
        Transform source = GameObject.Find("Gallery Source").transform;
        Transform firstTarget = GameObject.Find("Gallery Target").transform;
        const float feetPerGridUnit = 5f;

        string[] touchEntries =
        {
            "spell/heal/1-action/living",
            "spell/heal/1-action/undead-critical-success",
            "spell/heal/1-action/undead-success",
            "spell/heal/1-action/undead-failure",
            "spell/heal/1-action/undead-critical-failure",
        };
        foreach (string entry in touchEntries)
        {
            gallery.Select(entry);
            yield return gallery.PlaySelectedForTests();
            float distanceFeet =
                Vector3.Distance(source.position, firstTarget.position) * feetPerGridUnit;
            Assert.That(
                distanceFeet,
                Is.EqualTo(5f).Within(0.001f),
                entry + " must stage its target in an adjacent contact-range grid cell."
            );
            var touch = gallery.LastTransientStarts.Single(value =>
                value.Cue.StartsWith("spell/heal/1-action-", System.StringComparison.Ordinal)
            );
            Assert.That(
                Vector3.Distance(touch.Origin, firstTarget.position + Vector3.up * 0.6f),
                Is.LessThan(0.01f),
                entry
            );
            Assert.That(touch.Destination, Is.EqualTo(touch.Origin), entry);
        }

        gallery.Select("spell/heal/2-action/living");
        yield return gallery.PlaySelectedForTests();
        var delivery = gallery.LastTransientStarts.Single(value =>
            value.Cue == "spell/heal/2-action-delivery"
        );
        var rangedResult = gallery.LastTransientStarts.Single(value =>
            value.Cue == "spell/heal/2-action-living"
        );
        Assert.That(
            Vector3.Distance(delivery.Origin, source.position + Vector3.up * 0.6f),
            Is.LessThan(0.01f)
        );
        Assert.That(
            Vector3.Distance(delivery.Destination, firstTarget.position + Vector3.up * 0.6f),
            Is.LessThan(0.01f)
        );
        Assert.That(rangedResult.Origin, Is.EqualTo(rangedResult.Destination));
        float rangedDistanceFeet =
            Vector3.Distance(source.position, firstTarget.position) * feetPerGridUnit;
        Assert.That(rangedDistanceFeet, Is.GreaterThan(5f));
        Assert.That(rangedDistanceFeet, Is.LessThanOrEqualTo(30f));
        Assert.That(
            gallery.LastTrace.ToList().IndexOf("spell/heal/2-action-delivery"),
            Is.LessThan(gallery.LastTrace.ToList().IndexOf("spell/heal/2-action-living"))
        );

        gallery.Select("spell/heal/3-action/living");
        yield return gallery.PlaySelectedForTests();
        Assert.That(
            gallery.LastTrace.Count(cue => cue == "spell/heal/3-action-emanation"),
            Is.EqualTo(1)
        );
        Assert.That(
            gallery.LastTrace.Where(cue => cue.StartsWith("spell/heal/3-action-")).ToArray(),
            Is.EqualTo(
                new[]
                {
                    "spell/heal/3-action-emanation",
                    "spell/heal/3-action-living",
                    "spell/heal/3-action-living",
                    "spell/heal/3-action-undead-success",
                    "spell/heal/3-action-undead-critical-failure",
                }
            )
        );
        Transform[] areaTargets = Object
            .FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(target =>
                target.name is "Gallery Target" or "Gallery Target 2" or "Gallery Target 3"
            )
            .ToArray();
        Assert.That(areaTargets, Has.Length.EqualTo(3));
        Assert.That(
            areaTargets.All(target =>
                Vector3.Distance(source.position, target.position) * feetPerGridUnit <= 30f
            ),
            Is.True,
            "Every staged 3-action Heal target must remain inside the 30-foot emanation."
        );
        string[] areaResults =
        {
            "spell/heal/3-action-living",
            "spell/heal/3-action-undead-success",
            "spell/heal/3-action-undead-critical-failure",
        };
        Assert.That(
            gallery
                .LastTransientStarts.Where(value => areaResults.Contains(value.Cue))
                .All(value => value.Origin == value.Destination),
            Is.True
        );

        gallery.Select("spell/haunting-hymn/2-action/critical-failure");
        yield return gallery.PlaySelectedForTests();
        var hymn = gallery.LastTransientStarts.Single(value =>
            value.Cue == "spell/haunting-hymn/critical-failure"
        );
        Assert.That(
            Vector3.Distance(hymn.Origin, firstTarget.position + Vector3.up * 0.6f),
            Is.LessThan(0.01f)
        );
        Assert.That(hymn.Destination, Is.EqualTo(hymn.Origin));

        Time.captureDeltaTime = 0f;
        Scene cleanup = SceneManager.CreateScene("VFX Delivery Anchor Cleanup");
        SceneManager.SetActiveScene(cleanup);
        yield return SceneManager.UnloadSceneAsync("VfxGallery");
    }

    [UnityTest]
    public IEnumerator RangedMissTravelUsesOffTargetEndpointsAndHitsRetainContact()
    {
        yield return SceneManager.LoadSceneAsync("VfxGallery", LoadSceneMode.Single);
        VfxGalleryController gallery = Object.FindFirstObjectByType<VfxGalleryController>();
        gallery.ConfigureTestTiming(0.01f);
        Time.captureDeltaTime = 0.1f;
        Transform source = GameObject.Find("Gallery Source").transform;
        Transform target = GameObject.Find("Gallery Target").transform;
        var strikeMisses = new[]
        {
            (Id: "strike/shortbow/miss", Cue: "strike/bow/travel"),
            (Id: "strike/sling/miss", Cue: "strike/sling/travel"),
            (Id: "strike/spear/miss", Cue: "strike/piercing/travel"),
        };

        foreach (var value in strikeMisses)
        {
            gallery.Select(value.Id);
            yield return gallery.PlaySelectedForTests();
            var travel = gallery.LastTransientStarts.Single(start => start.Cue == value.Cue);
            Vector3 targetCenter = target.position + Vector3.up * 0.6f;
            Assert.That(
                Vector3.Distance(travel.Destination, targetCenter),
                Is.EqualTo(0.85f).Within(0.001f),
                value.Id + " must visibly clear the target center."
            );
        }

        gallery.Select("spell/divine-lance/2-action/miss");
        yield return gallery.PlaySelectedForTests();
        var spellMiss = gallery.LastTransientStarts.Single(start =>
            start.Cue == "spell/divine-lance/projectile"
        );
        Vector3 spellTargetCenter = target.position + Vector3.up * 0.6f;
        Assert.That(
            Vector3.Distance(spellMiss.Destination, spellTargetCenter),
            Is.EqualTo(0.85f).Within(0.001f)
        );

        string[] contactEntries =
        {
            "strike/shortbow/hit",
            "strike/shortbow/critical",
            "spell/divine-lance/2-action/hit",
            "spell/divine-lance/2-action/critical",
        };
        foreach (string entry in contactEntries)
        {
            gallery.Select(entry);
            yield return gallery.PlaySelectedForTests();
            var travel = gallery.LastTransientStarts.First(start =>
                start.Cue is "strike/bow/travel" or "spell/divine-lance/projectile"
            );
            Vector3 targetCenter = target.position + Vector3.up * 0.6f;
            Assert.That(
                Vector3.Distance(travel.Destination, targetCenter),
                Is.LessThan(0.001f),
                entry + " must retain its exact contact endpoint."
            );
        }

        Vector3 originalTargetPosition = target.position;
        target.position = source.position;
        Vector3 coincidentCenter = target.position + Vector3.up * 0.6f;
        CreatureId sourceId = new("coincident-source");
        CreatureId targetId = new("coincident-target");
        using UnityVfxPlayback playback = new(
            new ResourcesVfxPrefabCatalog(),
            "Coincident miss playback"
        );
        playback.ConfigureTestTiming(0.01f);
        List<(VfxCueId Cue, Vector3 Origin, Vector3 Destination)> starts = new();
        playback.TransientStarted += (cue, origin, destination) =>
            starts.Add((cue, origin, destination));
        CoincidentStrikeCatalog strikeCatalog = new();
        UnityStrikeActionPresenter presenter = new(
            new Dictionary<CreatureId, GameObject> { [sourceId] = source.gameObject },
            new Dictionary<CreatureId, CreatureComponent>
            {
                [targetId] = target.GetComponent<CreatureComponent>(),
            },
            strikeCatalog,
            playback
        );
        StrikeActionOp missAction = new(sourceId, strikeCatalog.Item.Item, targetId);
        StrikeResolution missResolution = new(
            new RollResult(DiceExpressions.D20, new[] { 3 }),
            8,
            0,
            0,
            18,
            0,
            false,
            Game.Rules.Runtime.DegreeOfSuccess.Failure,
            System.Array.Empty<TypedDamagePart>(),
            0
        );
        IEnumerator missTimeline = presenter.PresentResolved(
            missAction,
            missResolution,
            new InMemoryRulesStore(new RulesStateSeed()).Snapshot
        );
        using (missTimeline as System.IDisposable)
        {
            while (missTimeline.MoveNext())
                yield return missTimeline.Current;
        }
        var coincidentMiss = starts.Single(start => start.Cue.Value == "strike/bow/travel");
        Assert.That(
            Vector3.Distance(coincidentMiss.Destination, coincidentCenter + Vector3.right * 0.85f),
            Is.LessThan(0.001f),
            "Coincident source and target positions require a deterministic fallback axis."
        );
        target.position = originalTargetPosition;

        Time.captureDeltaTime = 0f;
        Scene cleanup = SceneManager.CreateScene("VFX Miss Trajectory Cleanup");
        SceneManager.SetActiveScene(cleanup);
        yield return SceneManager.UnloadSceneAsync("VfxGallery");
    }

    private sealed class CoincidentStrikeCatalog : IStrikePresentationCatalog
    {
        internal CoincidentStrikeCatalog()
        {
            Item = new StrikeItemDefinition(
                new ItemId("coincident-shortbow"),
                new ItemDefinitionId("coincident-shortbow"),
                "Shortbow",
                string.Empty,
                "test",
                System.Array.Empty<Trait>(),
                8,
                new[] { new TypedDamageDice(new DiceExpression(1, 6), "piercing", "test") },
                System.Array.Empty<TypedFlatDamage>(),
                5,
                60,
                0,
                StrikeAmmunitionRequirement.None
            );
        }

        internal StrikeItemDefinition Item { get; }

        public StrikeItemDefinition GetStrikeItem(ItemId item) => Item;

        public bool TryGetWeapon(ItemId item, out EquipmentWeapon weapon)
        {
            weapon = null;
            return false;
        }
    }

    [UnityTest]
    public IEnumerator StrikeGalleryUsesCommittedHealthReactionAndTerminalDefeatPath()
    {
        yield return SceneManager.LoadSceneAsync("VfxGallery", LoadSceneMode.Single);
        VfxGalleryController gallery = Object.FindFirstObjectByType<VfxGalleryController>();
        gallery.ConfigureTestTiming(0.01f);
        Time.captureDeltaTime = 0.1f;

        int defeatCountBefore = gallery.DefeatPresentationCount;
        var cases = new[]
        {
            (
                Id: "strike/mace/hit",
                Requested: 8,
                Applied: 8,
                Facts: 1,
                HitPoints: 2,
                Active: true,
                Defeats: 0
            ),
            (
                Id: "strike/shortbow/zero-damage-contact",
                Requested: 0,
                Applied: 0,
                Facts: 0,
                HitPoints: 10,
                Active: true,
                Defeats: 0
            ),
            (
                Id: "strike/mace/critical",
                Requested: 16,
                Applied: 10,
                Facts: 1,
                HitPoints: 0,
                Active: false,
                Defeats: 1
            ),
            (
                Id: "strike/mace/miss",
                Requested: 0,
                Applied: 0,
                Facts: 0,
                HitPoints: 10,
                Active: true,
                Defeats: 0
            ),
        };
        int expectedDefeats = 0;
        foreach (var value in cases)
        {
            gallery.Select(value.Id);
            yield return gallery.PlaySelectedForTests();
            expectedDefeats += value.Defeats;
            Assert.That(
                gallery.LatestStrikeDamageOutcome.Requested,
                Is.EqualTo(value.Requested),
                value.Id
            );
            Assert.That(
                gallery.LatestStrikeDamageOutcome.Applied,
                Is.EqualTo(value.Applied),
                value.Id
            );
            Assert.That(gallery.DamageFactCount, Is.EqualTo(value.Facts), value.Id);
            Assert.That(
                gallery.LatestStrikeDefeatCommitted,
                Is.EqualTo(value.Defeats == 1),
                value.Id
            );
            Assert.That(gallery.PrimaryTargetHitPoints, Is.EqualTo(value.HitPoints), value.Id);
            Assert.That(gallery.IsPrimaryTargetActive, Is.EqualTo(value.Active), value.Id);
            Assert.That(
                gallery.DefeatPresentationCount,
                Is.EqualTo(defeatCountBefore + expectedDefeats),
                value.Id
            );
        }
        Assert.That(
            gallery.LastTrace,
            Does.Not.Contain("strike/bludgeoning/hit"),
            "A miss must not emit a contact cue."
        );

        gallery.Select("strike/shortbow/zero-damage-contact");
        yield return gallery.PlaySelectedForTests();
        Assert.That(
            gallery.LastTrace,
            Does.Contain("strike/bow/hit"),
            "Zero applied damage remains a committed contact, not a miss."
        );

        gallery.Select("strike/mace/critical");
        yield return gallery.PlaySelectedForTests();
        Assert.That(gallery.PrimaryTargetHitPoints, Is.Zero);
        Assert.That(
            gallery.IsPrimaryTargetActive,
            Is.False,
            "Replay must re-arm and drain terminal defeat presentation again."
        );
        Assert.That(
            gallery.DefeatPresentationCount,
            Is.EqualTo(defeatCountBefore + expectedDefeats + 1)
        );

        gallery.ResetGallery();
        Assert.That(gallery.LatestStrikeDamageOutcome.Requested, Is.Zero);
        Assert.That(gallery.LatestStrikeDamageOutcome.Applied, Is.Zero);
        Assert.That(gallery.DamageFactCount, Is.Zero);
        Assert.That(gallery.LatestStrikeDefeatCommitted, Is.False);
        Assert.That(gallery.PrimaryTargetHitPoints, Is.EqualTo(10));
        Assert.That(gallery.IsPrimaryTargetActive, Is.True);

        Time.captureDeltaTime = 0f;
        Scene cleanup = SceneManager.CreateScene("VFX Gallery Reactions Cleanup");
        SceneManager.SetActiveScene(cleanup);
        yield return SceneManager.UnloadSceneAsync("VfxGallery");
    }

    [UnityTest]
    public IEnumerator SpellFixturesProjectCommittedDamageHealingAndDefeatFacts()
    {
        yield return SceneManager.LoadSceneAsync("VfxGallery", LoadSceneMode.Single);
        VfxGalleryController gallery = Object.FindFirstObjectByType<VfxGalleryController>();
        gallery.ConfigureTestTiming(0.01f);
        Time.captureDeltaTime = 0.1f;
        var damageCases = new[]
        {
            (Id: "spell/divine-lance/2-action/hit", HitPoints: 2, Active: true, Facts: 1),
            (Id: "spell/divine-lance/2-action/miss", HitPoints: 10, Active: true, Facts: 0),
            (Id: "spell/divine-lance/2-action/critical", HitPoints: 0, Active: false, Facts: 1),
            (
                Id: "spell/haunting-hymn/2-action/critical-success",
                HitPoints: 10,
                Active: true,
                Facts: 0
            ),
            (Id: "spell/haunting-hymn/2-action/success", HitPoints: 6, Active: true, Facts: 1),
            (Id: "spell/haunting-hymn/2-action/failure", HitPoints: 2, Active: true, Facts: 1),
            (
                Id: "spell/haunting-hymn/2-action/critical-failure",
                HitPoints: 0,
                Active: false,
                Facts: 1
            ),
            (
                Id: "spell/heal/1-action/undead-critical-success",
                HitPoints: 10,
                Active: true,
                Facts: 0
            ),
            (Id: "spell/heal/1-action/undead-success", HitPoints: 6, Active: true, Facts: 1),
            (Id: "spell/heal/1-action/undead-failure", HitPoints: 2, Active: true, Facts: 1),
            (
                Id: "spell/heal/1-action/undead-critical-failure",
                HitPoints: 0,
                Active: false,
                Facts: 1
            ),
        };

        foreach (var value in damageCases)
        {
            gallery.Select(value.Id);
            yield return gallery.PlaySelectedForTests();
            Assert.That(gallery.DamageFactCount, Is.EqualTo(value.Facts), value.Id);
            Assert.That(gallery.HealingFactCount, Is.Zero, value.Id);
            Assert.That(gallery.PrimaryTargetHitPoints, Is.EqualTo(value.HitPoints), value.Id);
            Assert.That(gallery.IsPrimaryTargetActive, Is.EqualTo(value.Active), value.Id);
        }

        gallery.Select("spell/heal/2-action/living");
        yield return gallery.PlaySelectedForTests();
        Assert.That(gallery.DamageFactCount, Is.Zero);
        Assert.That(gallery.HealingFactCount, Is.EqualTo(1));
        Assert.That(gallery.PrimaryTargetHitPoints, Is.EqualTo(10));
        Assert.That(gallery.IsPrimaryTargetActive, Is.True);

        gallery.Select("spell/heal/3-action/area-wave");
        yield return gallery.PlaySelectedForTests();
        Assert.That(gallery.DamageFactCount, Is.EqualTo(2));
        Assert.That(
            gallery.HealingFactCount,
            Is.EqualTo(2),
            "The caster and primary living target must both receive committed area healing."
        );
        Assert.That(gallery.PrimaryTargetHitPoints, Is.EqualTo(10));
        Assert.That(gallery.IsPrimaryTargetActive, Is.True);

        gallery.ResetGallery();
        Assert.That(gallery.DamageFactCount, Is.Zero);
        Assert.That(gallery.HealingFactCount, Is.Zero);
        Assert.That(gallery.PrimaryTargetHitPoints, Is.EqualTo(10));
        Assert.That(gallery.IsPrimaryTargetActive, Is.True);

        Time.captureDeltaTime = 0f;
        Scene cleanup = SceneManager.CreateScene("VFX Gallery Spell Health Cleanup");
        SceneManager.SetActiveScene(cleanup);
        yield return SceneManager.UnloadSceneAsync("VfxGallery");
    }

    [UnityTest]
    public IEnumerator ResetCancelsSelectedAndPlayAllTimelinesWithoutOrphans()
    {
        yield return SceneManager.LoadSceneAsync("VfxGallery", LoadSceneMode.Single);
        VfxGalleryController gallery = Object.FindFirstObjectByType<VfxGalleryController>();
        gallery.Select("spell/divine-lance/2-action/critical");
        gallery.BeginSelectedForTests();
        yield return null;
        Assert.That(gallery.IsPlaybackActive, Is.True);
        gallery.ResetGallery();
        yield return null;
        yield return null;
        Assert.That(gallery.IsPlaybackActive, Is.False);
        Assert.That(gallery.LiveVfxObjectCount, Is.Zero);

        gallery.BeginPlayAllForTests();
        yield return null;
        Assert.That(gallery.IsPlaybackActive, Is.True);
        gallery.ResetGallery();
        yield return null;
        yield return null;
        Assert.That(gallery.IsPlaybackActive, Is.False);
        Assert.That(gallery.LiveVfxObjectCount, Is.Zero);

        Scene cleanup = SceneManager.CreateScene("VFX Gallery Cancellation Cleanup");
        SceneManager.SetActiveScene(cleanup);
        yield return SceneManager.UnloadSceneAsync("VfxGallery");
    }

    [UnityTest]
    public IEnumerator ResetCancelsPendingDeathBeforeRestoringActorForNextEntry()
    {
        yield return SceneManager.LoadSceneAsync("VfxGallery", LoadSceneMode.Single);
        VfxGalleryController gallery = Object.FindFirstObjectByType<VfxGalleryController>();
        gallery.ConfigureTestTiming(0.01f);
        Time.captureDeltaTime = 0.1f;

        GameObject target = GameObject.Find("Gallery Target");
        CreaturePresentation presentation = target.GetComponent<CreaturePresentation>();
        CreatureAnimationController animation =
            target.GetComponentInChildren<CreatureAnimationController>();
        Assert.That(animation, Is.Not.Null);
        presentation.Bind(animation, target.GetComponentInChildren<CreatureEquipmentVisuals>());
        Assert.That(animation.HasDeathClip, Is.True);

        gallery.Select("strike/mace/critical");
        gallery.BeginSelectedForTests();
        for (int frame = 0; frame < 120 && !animation.IsDeathPlaying; frame++)
            yield return null;

        Assert.That(animation.IsDeathPlaying, Is.True, "The real death playback never started.");
        Assert.That(target.activeSelf, Is.True, "Reset must interrupt death before its callback.");
        Assert.That(
            animation.AnimationLibrary.TryGet(
                animation.CurrentClipId,
                out KayKitAnimationEntry deathEntry
            ),
            Is.True
        );
        Assert.That(deathEntry.Duration, Is.GreaterThan(0f));
        float oldCompletionDelay = Mathf.Min(deathEntry.Duration + 0.25f, 5f);

        gallery.ResetGallery();
        Assert.That(animation.IsDeathPlaying, Is.False);
        Assert.That(animation.CurrentClipId, Is.Null);
        Assert.That(gallery.PrimaryTargetHitPoints, Is.EqualTo(10));
        Assert.That(gallery.IsPrimaryTargetActive, Is.True);

        gallery.Select("strike/mace/miss");
        yield return gallery.PlaySelectedForTests();
        float deadline = Time.time + oldCompletionDelay + 0.5f;
        while (Time.time < deadline)
            yield return null;

        Assert.That(gallery.PrimaryTargetHitPoints, Is.EqualTo(10));
        Assert.That(
            gallery.IsPrimaryTargetActive,
            Is.True,
            "The cancelled death callback must not deactivate the restored actor."
        );
        Assert.That(animation.IsDeathPlaying, Is.False);
        Assert.That(gallery.DamageFactCount, Is.Zero);
        Assert.That(gallery.LiveVfxObjectCount, Is.Zero);

        Time.captureDeltaTime = 0f;
        Scene cleanup = SceneManager.CreateScene("VFX Gallery Death Cancellation Cleanup");
        SceneManager.SetActiveScene(cleanup);
        yield return SceneManager.UnloadSceneAsync("VfxGallery");
    }

    [UnityTest]
    public IEnumerator SelectedAndPlayAllControlsOwnPlaybackExclusively()
    {
        yield return SceneManager.LoadSceneAsync("VfxGallery", LoadSceneMode.Single);
        VfxGalleryController gallery = Object.FindFirstObjectByType<VfxGalleryController>();
        gallery.ConfigureTestTiming(0.01f);
        Time.captureDeltaTime = 0.1f;
        gallery.BeginPlayAllForTests();
        yield return null;
        Assert.That(gallery.IsPlayAllActive, Is.True);
        gallery.StopPlayAllForTests();
        yield return null;
        Assert.That(gallery.IsPlaybackActive, Is.False);
        Assert.That(gallery.LiveVfxObjectCount, Is.Zero);
        gallery.BeginPlayAllForTests();
        yield return null;
        Assert.That(gallery.IsPlayAllActive, Is.True);

        gallery.Select("spell/divine-lance/2-action/critical");
        gallery.BeginSelectedForTests();
        Assert.That(gallery.IsPlayAllActive, Is.False);
        Assert.That(gallery.IsSelectedPlaybackActive, Is.True);
        for (int frame = 0; frame < 60 && gallery.IsPlaybackActive; frame++)
            yield return null;
        Assert.That(gallery.IsPlaybackActive, Is.False);

        gallery.Select("spell/light/2-action/create");
        gallery.BeginSelectedForTests();
        yield return null;
        Assert.That(gallery.IsSelectedPlaybackActive, Is.True);
        gallery.BeginPlayAllForTests();
        Assert.That(gallery.IsSelectedPlaybackActive, Is.False);
        Assert.That(gallery.IsPlayAllActive, Is.True);
        gallery.ResetGallery();
        yield return null;
        Assert.That(gallery.LiveVfxObjectCount, Is.Zero);

        Time.captureDeltaTime = 0f;
        Scene cleanup = SceneManager.CreateScene("VFX Gallery Exclusive Playback Cleanup");
        SceneManager.SetActiveScene(cleanup);
        yield return SceneManager.UnloadSceneAsync("VfxGallery");
    }

    [UnityTest]
    public IEnumerator AbortedPresentationAndDisabledOwnerDestroyTheirActiveTransients()
    {
        using UnityVfxPlayback playback = new(
            new ResourcesVfxPrefabCatalog(),
            "Transient cancellation test"
        );
        using UnityActionPresentationCoordinator coordinator = new();
        object action = new();
        coordinator.Begin(action, new OpId(44));
        coordinator.Enqueue(
            action,
            () =>
                playback.PlayTransient(
                    new VfxCueId("spell/divine-lance/projectile"),
                    Vector3.zero,
                    Vector3.right
                )
        );
        IEnumerator drain = coordinator.Drain(action);

        Assert.That(drain.MoveNext(), Is.True);
        Assert.That(playback.LiveObjectCount, Is.EqualTo(1));
        (drain as System.IDisposable)?.Dispose();
        yield return null;
        Assert.That(playback.LiveObjectCount, Is.Zero);

        GameObject owner = new("Transient lifetime owner");
        IEnumerator ownerBound = playback.PlayTransient(
            new VfxCueId("spell/divine-lance/projectile"),
            Vector3.zero,
            Vector3.right,
            lifetimeOwner: owner.transform
        );
        Assert.That(ownerBound.MoveNext(), Is.True);
        Assert.That(playback.LiveObjectCount, Is.EqualTo(1));
        owner.SetActive(false);
        yield return null;
        yield return null;
        Assert.That(playback.LiveObjectCount, Is.Zero);
        (ownerBound as System.IDisposable)?.Dispose();
        Object.Destroy(owner);
        yield return null;
    }

    [UnityTest]
    public IEnumerator PlaybackReplacementDestroyedOwnerAndDisposeAreLeakFree()
    {
        GameObject owner = new("VFX owner");
        UnityVfxPlayback playback = new(new ResourcesVfxPrefabCatalog(), "VFX lifecycle test");
        playback.SetPersistent(
            "effect-1",
            new VfxCueId("spell/shield/persistent"),
            owner.transform
        );
        playback.SetPersistent("effect-1", new VfxCueId("spell/bless/persistent"), owner.transform);
        yield return null;
        Assert.That(playback.LiveObjectCount, Is.EqualTo(1));

        owner.SetActive(false);
        yield return null;
        yield return null;
        Assert.That(playback.LiveObjectCount, Is.Zero, "Disabled owners release their VFX.");
        owner.SetActive(true);
        playback.SetPersistent(
            "effect-1",
            new VfxCueId("spell/shield/persistent"),
            owner.transform
        );
        Object.Destroy(owner);
        yield return null;
        yield return null;
        Assert.That(playback.LiveObjectCount, Is.Zero);

        playback.RemovePersistent("effect-1");
        playback.RemovePersistent("effect-1");
        playback.Dispose();
        playback.Dispose();
        yield return null;
        Assert.That(GameObject.Find("VFX lifecycle test"), Is.Null);
    }

    [UnityTest]
    public IEnumerator StressReplayMeasuresBoundedCostAndNoObjectGrowth()
    {
        GameObject owner = new("VFX stress owner");
        UnityVfxPlayback playback = new(new ResourcesVfxPrefabCatalog(), "VFX stress playback");
        Stopwatch baselineClock = Stopwatch.StartNew();
        for (int frame = 0; frame < 30; frame++)
            yield return null;
        baselineClock.Stop();
        long baselineMemory = Profiler.GetTotalAllocatedMemoryLong();

        Stopwatch vfxClock = Stopwatch.StartNew();
        for (int replay = 0; replay < 20; replay++)
        {
            playback.SetPersistent(
                "stress-a",
                new VfxCueId("spell/bless/persistent"),
                owner.transform
            );
            playback.SetPersistent("stress-b", new VfxCueId("auxiliary/rage"), owner.transform);
            playback.SetPersistent(
                "stress-c",
                new VfxCueId("spell/light/persistent"),
                owner.transform
            );
            Assert.That(playback.LiveObjectCount, Is.LessThanOrEqualTo(3));
            yield return null;
        }
        vfxClock.Stop();
        long vfxMemory = Profiler.GetTotalAllocatedMemoryLong();
        playback.Reset();
        yield return null;
        yield return null;

        Assert.That(playback.LiveObjectCount, Is.Zero);
        Debug.Log(
            $"VFX_PROFILE baseline30={baselineClock.Elapsed.TotalMilliseconds:F2}ms "
                + $"stress20={vfxClock.Elapsed.TotalMilliseconds:F2}ms "
                + $"allocatedDelta={vfxMemory - baselineMemory}B peakObjects=3 finalObjects=0"
        );
        playback.Dispose();
        Object.Destroy(owner);
        yield return null;
    }

    [UnityTest]
    public IEnumerator ProductionFixtureReplayHasBoundedObjectsMaterialsLightsAndAllocations()
    {
        yield return SceneManager.LoadSceneAsync("VfxGallery", LoadSceneMode.Single);
        VfxGalleryController gallery = Object.FindFirstObjectByType<VfxGalleryController>();
        gallery.ConfigureTestTiming(0.02f);
        Time.captureDeltaTime = 0.1f;
        for (int warmup = 0; warmup < 10; warmup++)
            yield return null;

        Stopwatch baselineClock = Stopwatch.StartNew();
        for (int frame = 0; frame < 30; frame++)
            yield return null;
        baselineClock.Stop();
        long baselineMemory = Profiler.GetTotalAllocatedMemoryLong();
        string[] representativeEntries =
        {
            "strike/shortbow/critical",
            "spell/divine-lance/2-action/critical",
            "spell/heal/3-action/living",
            "auxiliary/rotting-aura/active",
            "spell/light/2-action/create",
        };
        int peakObjects = 0;
        int peakParticles = 0;
        int peakRenderers = 0;
        int peakLights = 0;
        int peakSharedMaterials = 0;
        Stopwatch productionClock = Stopwatch.StartNew();
        for (int replay = 0; replay < 10; replay++)
        {
            gallery.Select(representativeEntries[replay % representativeEntries.Length]);
            gallery.BeginSelectedForTests();
            do
            {
                UnityVfxInstance[] instances = Object.FindObjectsByType<UnityVfxInstance>(
                    FindObjectsSortMode.None
                );
                peakObjects = Mathf.Max(peakObjects, instances.Length);
                peakParticles = Mathf.Max(
                    peakParticles,
                    instances.Sum(instance =>
                        instance.GetComponentsInChildren<ParticleSystem>(true).Length
                    )
                );
                Renderer[] renderers = instances
                    .SelectMany(instance => instance.GetComponentsInChildren<Renderer>(true))
                    .ToArray();
                peakRenderers = Mathf.Max(peakRenderers, renderers.Length);
                peakLights = Mathf.Max(
                    peakLights,
                    Object
                        .FindObjectsByType<Light>(FindObjectsSortMode.None)
                        .Count(light => light.name == "Spell Effect Light")
                );
                peakSharedMaterials = Mathf.Max(
                    peakSharedMaterials,
                    renderers
                        .SelectMany(renderer => renderer.sharedMaterials)
                        .Where(material => material != null)
                        .Distinct()
                        .Count()
                );
                yield return null;
            } while (gallery.IsPlaybackActive);
            gallery.ResetGallery();
            yield return null;
            yield return null;
            Assert.That(gallery.LiveVfxObjectCount, Is.Zero, "Replay reset leaked a VFX object.");
        }
        productionClock.Stop();
        long productionMemory = Profiler.GetTotalAllocatedMemoryLong();

        Assert.That(peakObjects, Is.InRange(1, 4));
        Assert.That(peakParticles, Is.InRange(1, 12));
        Assert.That(
            peakRenderers,
            Is.InRange(1, 32),
            "Mixed-target area Heal intentionally presents four bounded target outcomes together."
        );
        Assert.That(peakLights, Is.EqualTo(1));
        Assert.That(peakSharedMaterials, Is.InRange(1, 8));
        Assert.That(Object.FindObjectsByType<UnityVfxInstance>(FindObjectsSortMode.None), Is.Empty);
        Debug.Log(
            $"VFX_PRODUCTION_PROFILE baseline30={baselineClock.Elapsed.TotalMilliseconds:F2}ms "
                + $"replays10={productionClock.Elapsed.TotalMilliseconds:F2}ms "
                + $"allocatedDelta={productionMemory - baselineMemory}B peakObjects={peakObjects} "
                + $"peakParticles={peakParticles} peakRenderers={peakRenderers} "
                + $"peakLights={peakLights} peakSharedMaterials={peakSharedMaterials} finalObjects=0"
        );

        Time.captureDeltaTime = 0f;
        Scene cleanup = SceneManager.CreateScene("VFX Production Profile Cleanup");
        SceneManager.SetActiveScene(cleanup);
        yield return SceneManager.UnloadSceneAsync("VfxGallery");
    }
}
