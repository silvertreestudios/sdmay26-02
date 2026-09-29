using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Game.Combat.Spells;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Light;
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

        yield return gallery.PlayAllEntries();
        Assert.That(
            gallery.DefeatPresentationCount - defeatCountBefore,
            Is.EqualTo(expectedDefeats),
            "Play All must replay terminal presentation for every critical Strike fixture."
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

        gallery.Select("spell/heal/1-action/living");
        yield return gallery.PlaySelectedForTests();
        var touch = gallery.LastTransientStarts.Single(value =>
            value.Cue == "spell/heal/1-action-living"
        );
        Assert.That(
            Vector3.Distance(touch.Origin, firstTarget.position + Vector3.up * 0.6f),
            Is.LessThan(0.01f)
        );
        Assert.That(touch.Destination, Is.EqualTo(touch.Origin));

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
                    "spell/heal/3-action-undead-success",
                    "spell/heal/3-action-undead-critical-failure",
                }
            )
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
    public IEnumerator StrikeGalleryUsesCommittedHealthReactionAndTerminalDefeatPath()
    {
        yield return SceneManager.LoadSceneAsync("VfxGallery", LoadSceneMode.Single);
        VfxGalleryController gallery = Object.FindFirstObjectByType<VfxGalleryController>();
        gallery.ConfigureTestTiming(0.01f);
        Time.captureDeltaTime = 0.1f;

        gallery.Select("strike/mace/hit");
        yield return gallery.PlaySelectedForTests();
        Assert.That(gallery.PrimaryTargetHitPoints, Is.EqualTo(2));
        Assert.That(gallery.IsPrimaryTargetActive, Is.True);

        gallery.Select("strike/mace/critical");
        int defeatCountBefore = gallery.DefeatPresentationCount;
        yield return gallery.PlaySelectedForTests();
        Assert.That(gallery.PrimaryTargetHitPoints, Is.Zero);
        Assert.That(
            gallery.IsPrimaryTargetActive,
            Is.False,
            "Critical fixture must drain terminal defeat after its impact presentation."
        );
        Assert.That(gallery.DefeatPresentationCount, Is.EqualTo(defeatCountBefore + 1));

        yield return gallery.PlaySelectedForTests();
        Assert.That(gallery.PrimaryTargetHitPoints, Is.Zero);
        Assert.That(
            gallery.IsPrimaryTargetActive,
            Is.False,
            "Replay must re-arm and drain terminal defeat presentation again."
        );
        Assert.That(gallery.DefeatPresentationCount, Is.EqualTo(defeatCountBefore + 2));

        gallery.ResetGallery();
        Assert.That(gallery.PrimaryTargetHitPoints, Is.EqualTo(10));
        Assert.That(gallery.IsPrimaryTargetActive, Is.True);
        gallery.Select("strike/mace/miss");
        yield return gallery.PlaySelectedForTests();
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
        Assert.That(gallery.HealingFactCount, Is.EqualTo(1));
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

        Assert.That(peakObjects, Is.InRange(1, 3));
        Assert.That(peakParticles, Is.InRange(1, 12));
        Assert.That(
            peakRenderers,
            Is.InRange(1, 32),
            "Mixed-target area Heal intentionally presents three bounded target outcomes together."
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
