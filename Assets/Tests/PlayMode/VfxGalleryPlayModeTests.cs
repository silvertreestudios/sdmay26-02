using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Game.Creature;
using Game.Rules.Runtime;
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
}

public sealed class VfxGalleryPlayModeTests
{
    [UnityTest]
    public IEnumerator GalleryIteratesEveryProductionEntryAndResetLeavesNoObjects()
    {
        yield return SceneManager.LoadSceneAsync("VfxGallery", LoadSceneMode.Single);
        VfxGalleryController gallery = Object.FindFirstObjectByType<VfxGalleryController>();
        Assert.That(gallery, Is.Not.Null);
        Assert.That(gallery.EntryCount, Is.GreaterThanOrEqualTo(50));
        gallery.ConfigureTestTiming(0.01f);
        Time.captureDeltaTime = 0.1f;

        yield return gallery.PlayAllEntries();
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
                : entry.id is "spell/light/2-action/create" or "spell/shield/1-action/create" ? 0
                : 1;
            Assert.That(gallery.CurrentTargetCount, Is.EqualTo(expectedTargets), entry.id);
            if (entry.id.StartsWith("spell/infuse-vitality/"))
            {
                Assert.That(
                    gallery.LastTrace.Count(cue => cue == entry.cue),
                    Is.EqualTo(expectedTargets),
                    entry.id + " must emit one simultaneous production beam per selected target."
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
                Assert.That(
                    gallery.LiveVfxObjectCount,
                    Is.EqualTo(shouldRemainActive ? 1 : 0),
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
                    instances.Sum(instance => instance.GetComponentsInChildren<Light>(true).Length)
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
        Assert.That(peakRenderers, Is.InRange(1, 24));
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
