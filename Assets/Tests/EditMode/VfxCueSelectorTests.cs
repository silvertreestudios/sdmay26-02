using System;
using System.Collections;
using System.Linq;
using Game.Combat.Spells;
using Game.Rules.Runtime;
using Game.Rules.Unity.Vfx;
using NUnit.Framework;
using UnityEngine;

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
    }

    [Test]
    public void EveryManifestEntryLoadsAnActualProductionPrefab()
    {
        ResourcesVfxPrefabCatalog catalog = new();
        foreach (VfxCoverageEntry entry in VfxCoverageManifest.Load().entries)
        {
            GameObject prefab = catalog.Require(new VfxCueId(entry.cue));
            Assert.That(prefab.GetComponent<UnityVfxInstance>(), Is.Not.Null, entry.id);
        }
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
}
