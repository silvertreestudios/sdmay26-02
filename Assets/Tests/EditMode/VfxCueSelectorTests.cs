using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity.Vfx;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using DegreeOfSuccess = Game.Rules.Runtime.DegreeOfSuccess;

public sealed class VfxCoverageManifestTests
{
    [Test]
    public void MapsExactlyEightSpellsAndNineNamedStrikeProfiles()
    {
        Assert.That(SpellVfxCueSelector.SupportedSpells.Count, Is.EqualTo(8));
        Assert.That(StrikeVfxCueSelector.SupportedProfiles.Count, Is.EqualTo(9));
        Assert.That(StrikeVfxCueSelector.SupportedProfiles, Does.Contain("Longsword"));
        Assert.That(StrikeVfxCueSelector.SupportedProfiles, Does.Not.Contain("Halberd"));
        Assert.That(StrikeVfxCueSelector.SupportedProfiles, Does.Not.Contain("Fist"));
        Assert.That(StrikeVfxCueSelector.SupportedProfiles, Does.Not.Contain("Jaws"));
        Assert.That(StrikeVfxCueSelector.SupportedProfiles, Does.Not.Contain("Claw"));
        Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() =>
            StrikeVfxCueSelector.GetTravel("Halberd")
        );
    }

    [TestCase(DegreeOfSuccess.Failure)]
    [TestCase(DegreeOfSuccess.CriticalFailure)]
    public void MissSuppressesTargetImpact(DegreeOfSuccess degree)
    {
        Assert.That(StrikeVfxCueSelector.TryGetImpact("Shortbow", degree, out _), Is.False);
    }

    [Test]
    public void CriticalAmplifiesTheSameStrikeFamily()
    {
        Assert.That(
            StrikeVfxCueSelector.TryGetImpact(
                "Greataxe",
                DegreeOfSuccess.Success,
                out VfxCueId hit
            ),
            Is.True
        );
        Assert.That(
            StrikeVfxCueSelector.TryGetImpact(
                "Greataxe",
                DegreeOfSuccess.CriticalSuccess,
                out VfxCueId critical
            ),
            Is.True
        );
        Assert.That(hit.Value, Is.EqualTo("strike/heavy-slash/hit"));
        Assert.That(critical.Value, Is.EqualTo("strike/heavy-slash/critical"));
    }

    [Test]
    public void ContributionAccentsRequireCommittedHitSources()
    {
        string[] sources = { "Dogslicer", "sneak-attack", "infuse-vitality" };

        Assert.That(StrikeVfxCueSelector.GetContributionAccents(sources, hit: false), Is.Empty);
        Assert.That(
            StrikeVfxCueSelector
                .GetContributionAccents(sources, hit: true)
                .Select(cue => cue.Value),
            Is.EquivalentTo(new[] { "auxiliary/sneak-attack", "auxiliary/infuse-vitality-strike" })
        );
        Assert.That(
            StrikeVfxCueSelector.GetContributionAccents(new[] { "Dogslicer" }, hit: true),
            Is.Empty
        );
    }

    [TestCase("light", 2)]
    [TestCase("shield", 1)]
    [TestCase("guidance", 1)]
    [TestCase("bless", 2)]
    public void LastingSpellResultsUseTheirProductionPersistentCue(string spell, int actions)
    {
        VfxCueId cue = SpellVfxCueSelector.GetResult(new SpellId(spell), actions);

        Assert.That(cue.Value, Is.EqualTo($"spell/{spell}/persistent"));
        Assert.DoesNotThrow(() => new ResourcesVfxPrefabCatalog().Require(cue));
    }

    [Test]
    public void CoverageManifestReconcilesRequiredVariantsProfilesAndExclusions()
    {
        VfxCoverageManifest manifest = VfxCoverageManifest.Load();
        Assert.That(manifest.entries, Is.Not.Empty);
        int spellVariants = manifest
            .entries.Where(entry => entry.category == "spell")
            .Select(entry => string.Join("/", entry.id.Split('/').Take(3)))
            .Distinct(StringComparer.Ordinal)
            .Count();
        string[] strikes = manifest
            .entries.Where(entry => entry.category == "strike")
            .Select(entry => entry.displayName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.That(spellVariants, Is.EqualTo(12));
        Assert.That(strikes, Has.Length.EqualTo(9));
        foreach (string strike in strikes)
        {
            string[] outcomes = manifest
                .entries.Where(entry => entry.category == "strike" && entry.displayName == strike)
                .Select(entry => entry.outcome)
                .ToArray();
            Assert.That(outcomes, Does.Contain("miss"), strike);
            Assert.That(outcomes, Does.Contain("hit"), strike);
            Assert.That(outcomes, Does.Contain("critical"), strike);
        }
        Assert.That(manifest.unsupported, Does.Contain("Halberd"));
        Assert.That(manifest.unsupported, Does.Contain("the other 76 spell JSON definitions"));
        Assert.That(
            manifest.entries.Select(entry => entry.id).Distinct(StringComparer.Ordinal).Count(),
            Is.EqualTo(manifest.entries.Count),
            "Gallery IDs must be unique and copyable."
        );

        string[] degrees = { "critical-success", "success", "failure", "critical-failure" };
        for (int actions = 1; actions <= 3; actions++)
        {
            foreach (string degree in degrees)
            {
                string id = $"spell/heal/{actions}-action/undead-{degree}";
                VfxCoverageEntry entry = manifest.entries.Single(candidate => candidate.id == id);
                Assert.That(
                    entry.cue,
                    Is.EqualTo($"spell/heal/{actions}-action-undead-{degree}"),
                    id
                );
            }
        }
        Assert.That(
            manifest
                .entries.Single(entry => entry.id == "auxiliary/rotting-aura/tick")
                .gameplayTrigger,
            Is.EqualTo("DamageAppliedFact with Rotting Aura origin")
        );
        Assert.That(
            manifest
                .entries.Single(entry => entry.id == "auxiliary/rotting-aura/resisted")
                .gameplayTrigger,
            Is.EqualTo("RottingAuraResolvedFact with Outcome.Applied zero")
        );
    }

    [Test]
    public void ManifestAndProductionPrefabCatalogHaveExactInverseCoverage()
    {
        string[] manifestCues = VfxCoverageManifest
            .Load()
            .entries.Select(entry => entry.cue)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        const string root = "Assets/Resources/Vfx/";
        string[] prefabCues = AssetDatabase
            .FindAssets("t:Prefab", new[] { "Assets/Resources/Vfx" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.EndsWith(".prefab", StringComparison.Ordinal))
            .Select(path =>
                path.Substring(root.Length, path.Length - root.Length - ".prefab".Length)
            )
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.That(manifestCues, Is.EqualTo(prefabCues));
    }

    [Test]
    public void ManifestListsEverySpellTimelinePhaseSelectedByProduction()
    {
        string[] manifestCues = VfxCoverageManifest
            .Load()
            .entries.Select(entry => entry.cue)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (string spell in SpellVfxCueSelector.SupportedSpells)
            Assert.That(
                manifestCues,
                Does.Contain(SpellVfxCueSelector.GetCast(new SpellId(spell)).Value),
                spell
            );
        foreach (
            string spell in new[] { "light", "shield", "guidance", "bless", "infuse-vitality" }
        )
        {
            Assert.That(
                SpellVfxCueSelector.TryGetPersistent(new SpellId(spell), out VfxCueId cue),
                Is.True
            );
            Assert.That(manifestCues, Does.Contain(cue.Value), spell);
        }
        for (int actions = 2; actions <= 3; actions++)
        {
            Assert.That(SpellVfxCueSelector.TryGetHealDelivery(actions, out VfxCueId cue), Is.True);
            Assert.That(manifestCues, Does.Contain(cue.Value), $"Heal {actions}-action delivery");
        }
    }

    [Test]
    public void HealTouchRangedAndAreaPhasesUseDistinctAuthoredMotion()
    {
        AssertMotion("spell/heal/1-action-living", VfxMotionKind.Burst);
        AssertMotion("spell/heal/2-action-delivery", VfxMotionKind.Projectile);
        AssertMotion("spell/heal/2-action-living", VfxMotionKind.Burst);
        AssertMotion("spell/heal/3-action-emanation", VfxMotionKind.Emanation);
        AssertMotion("spell/heal/3-action-living", VfxMotionKind.Burst);
    }

    [Test]
    public void EveryManifestEntryLoadsAnActualProductionPrefab()
    {
        ResourcesVfxPrefabCatalog catalog = new();
        foreach (VfxCoverageEntry entry in VfxCoverageManifest.Load().entries)
        {
            GameObject prefab = catalog.Require(new VfxCueId(entry.cue));
            Assert.That(prefab.GetComponent<UnityVfxInstance>(), Is.Not.Null, entry.id);
            Assert.That(
                prefab.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                entry.id + " VFX must not participate in targeting or line-of-sight physics."
            );
        }
    }

    [Test]
    public void HealAndHymnOutcomesHaveDistinctAuthoredPresentation()
    {
        ResourcesVfxPrefabCatalog catalog = new();
        GameObject living = catalog.Require(new VfxCueId("spell/heal/2-action-living"));
        GameObject undead = catalog.Require(new VfxCueId("spell/heal/2-action-undead-success"));
        Assert.That(
            living.GetComponentsInChildren<Transform>(true).Select(value => value.name),
            Does.Contain("Living Restoration Pillar")
        );
        Assert.That(
            undead.GetComponentsInChildren<Transform>(true).Select(value => value.name),
            Does.Contain("Undead Vitality Break 0")
        );
        Assert.That(
            living.GetComponentInChildren<Renderer>(true).sharedMaterial,
            Is.Not.SameAs(undead.GetComponentInChildren<Renderer>(true).sharedMaterial)
        );

        string[] degrees = { "critical-success", "success", "failure", "critical-failure" };
        string[] materials = degrees
            .Select(degree =>
                catalog
                    .Require(new VfxCueId("spell/haunting-hymn/" + degree))
                    .GetComponentInChildren<Renderer>(true)
                    .sharedMaterial.name
            )
            .ToArray();
        Assert.That(materials, Is.Unique);
        Assert.That(
            catalog
                .Require(new VfxCueId("spell/haunting-hymn/critical-failure"))
                .GetComponentsInChildren<Transform>(true)
                .Select(value => value.name),
            Does.Contain("Deafened Left Ear")
        );
    }

    [Test]
    public void LightPrefabLeavesTheSingleRealPointLightToTheProductionObserver()
    {
        GameObject prefab = new ResourcesVfxPrefabCatalog().Require(
            new VfxCueId("spell/light/persistent")
        );

        Assert.That(prefab.GetComponentsInChildren<Light>(true), Is.Empty);
    }

    [Test]
    public void TransientPlaybackCompletesDuringSynchronousEditModeDrain()
    {
        using UnityVfxPlayback playback = new(
            new ResourcesVfxPrefabCatalog(),
            "Synchronous EditMode VFX"
        );
        IEnumerator routine = playback.PlayTransient(
            new VfxCueId("strike/bow/travel"),
            Vector3.zero,
            Vector3.right
        );
        int yieldedFrames = 0;

        while (routine.MoveNext())
            yieldedFrames++;

        Assert.That(yieldedFrames, Is.Zero);
        Assert.That(playback.LiveObjectCount, Is.Zero);
    }

    [Test]
    public void RottingAuraAppliedTickStartsFromCommittedDamageBeforeFollowingProjection()
    {
        CreatureId sourceId = new("vfx-aura-source");
        CreatureId targetId = new("vfx-aura-target");
        GameObject sourceObject = new("VFX aura source");
        GameObject targetObject = new("VFX aura target");
        using UnityVfxPlayback playback = new(new ResourcesVfxPrefabCatalog(), "Aura ordering VFX");
        try
        {
            CreatureComponent source = sourceObject.AddComponent<CreatureComponent>();
            CreatureComponent target = targetObject.AddComponent<CreatureComponent>();
            Dictionary<CreatureId, CreatureComponent> creatures = new()
            {
                [sourceId] = source,
                [targetId] = target,
            };
            UnityRottingAuraModule module = new(creatures, new GridPrivate.Tile[1, 1], playback);
            List<string> trace = new();
            playback.CueStarted += cue => trace.Add(cue.Value);
            RulesSnapshot snapshot = new RulesState(
                new RulesStateSeed().SeedHealth(targetId, new HealthState(4, 10))
            ).Snapshot;

            module.OnFactCommitted(
                new DamageAppliedFact(
                    targetId,
                    new HealthChangeOriginId("rotting-aura-42"),
                    4,
                    0,
                    4
                ),
                new OpId(42),
                snapshot
            );
            trace.Add("health-reaction");
            module.OnFactCommitted(
                new RottingAuraResolvedFact(
                    sourceId,
                    targetId,
                    new RollResult(new DiceExpression(1, 6), new[] { 4 }),
                    new[] { new TypedDamagePart("void", 4, new[] { RottingAuraRules.Slug }) },
                    Array.Empty<TypedDefenseAdjustment>(),
                    Array.Empty<TypedDefenseAdjustment>(),
                    new DamageOutcome(4, 0, 4)
                ),
                new OpId(42),
                snapshot
            );

            Assert.That(
                trace,
                Is.EqualTo(new[] { "auxiliary/rotting-aura-tick", "health-reaction" })
            );
            Assert.That(playback.LiveObjectCount, Is.Zero);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(sourceObject);
            UnityEngine.Object.DestroyImmediate(targetObject);
        }
    }

    [Test]
    public void GuidanceImmunityDoesNotSelectTheConsumableGuidanceAura()
    {
        CreatureId owner = new("guidance-immunity-owner");
        ActiveEffectInstance immunity = new(
            new ActiveEffectId("guidance-immunity-effect"),
            SpellFeatureRules.GuidanceImmunity,
            owner,
            RuleSource.FromSlug("guidance-immunity-test"),
            EffectDuration.Indefinite,
            new SpellEffectState(new SpellReference(new SpellId("guidance"), 1), owner)
        );

        Assert.That(SpellPersistentVfxSelector.Select(immunity), Is.Null);
    }

    [Test]
    public void GuidanceEffectSelectsItsCommittedConsumptionPulse()
    {
        CreatureId owner = new("guidance-owner");
        ActiveEffectInstance guidance = new(
            new ActiveEffectId("guidance-effect"),
            SpellFeatureRules.GuidanceEffect,
            owner,
            RuleSource.FromSlug("guidance-test"),
            EffectDuration.Indefinite,
            new SpellEffectState(new SpellReference(new SpellId("guidance"), 1), owner)
        );

        PersistentVfxSelection selection = SpellPersistentVfxSelector.Select(guidance).Value;

        Assert.That(selection.Cue.Value, Is.EqualTo("spell/guidance/persistent"));
        Assert.That(selection.RemovalCue.Value, Is.EqualTo("spell/guidance/consume"));
    }

    private static void AssertMotion(string cue, VfxMotionKind expected)
    {
        GameObject prefab = new ResourcesVfxPrefabCatalog().Require(new VfxCueId(cue));
        SerializedObject serialized = new(prefab.GetComponent<UnityVfxInstance>());
        Assert.That(
            serialized.FindProperty("motion").enumValueIndex,
            Is.EqualTo((int)expected),
            cue
        );
    }
}

public sealed class SpellAttackVfxEditorEntrySafetyTests
{
    [Test]
    public void CancellationStopsBeforeAssetOrSceneMutation()
    {
        List<string> trace = new();

        bool proceeded = InvokeEntrySafety(
            isBatchMode: false,
            () =>
            {
                trace.Add("prompt");
                return false;
            },
            () =>
            {
                trace.Add("asset-write");
                trace.Add("scene-replacement");
            }
        );

        Assert.That(proceeded, Is.False);
        Assert.That(trace, Is.EqualTo(new[] { "prompt" }));
    }

    [Test]
    public void AcceptancePrecedesAssetAndSceneMutation()
    {
        List<string> trace = new();

        bool proceeded = InvokeEntrySafety(
            isBatchMode: false,
            () =>
            {
                trace.Add("prompt");
                return true;
            },
            () =>
            {
                trace.Add("asset-write");
                trace.Add("scene-replacement");
            }
        );

        Assert.That(proceeded, Is.True);
        Assert.That(trace, Is.EqualTo(new[] { "prompt", "asset-write", "scene-replacement" }));
    }

    [Test]
    public void BatchmodeProceedsWithoutOpeningInteractivePrompt()
    {
        List<string> trace = new();

        bool proceeded = InvokeEntrySafety(
            isBatchMode: true,
            () =>
            {
                trace.Add("prompt");
                return false;
            },
            () => trace.Add("batch-work")
        );

        Assert.That(proceeded, Is.True);
        Assert.That(trace, Is.EqualTo(new[] { "batch-work" }));
    }

    private static bool InvokeEntrySafety(
        bool isBatchMode,
        Func<bool> saveCurrentModifiedScenesIfUserWantsTo,
        Action sceneReplacingWork
    )
    {
        Type safetyType = AppDomain
            .CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("SpellAttackVfxEditorEntrySafety"))
            .Single(type => type != null);
        System.Reflection.MethodInfo method = safetyType.GetMethod(
            "TryRun",
            System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
        );
        Assert.That(method, Is.Not.Null);
        return (bool)
            method.Invoke(
                null,
                new object[]
                {
                    isBatchMode,
                    saveCurrentModifiedScenesIfUserWantsTo,
                    sceneReplacingWork,
                }
            );
    }
}
