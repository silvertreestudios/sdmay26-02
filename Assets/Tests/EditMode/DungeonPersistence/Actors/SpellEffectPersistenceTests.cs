using System.Linq;
using Game.DungeonPersistence.Actors;
using Game.Rules.Runtime;
using NUnit.Framework;

public sealed class SpellEffectPersistenceTests
{
    [Test]
    public void InfuseVitalityCodecPreservesRankUsedByDuplicateEffectPrecedence()
    {
        CreatureId originalTarget = new("infuse-persistence-target");
        CreatureId restoredTarget = new("infuse-persistence-restored-target");
        SpellEffectState state = new(
            new SpellReference(new SpellId("infuse-vitality"), 5),
            originalTarget
        );
        DungeonRulesActorReference actor = DungeonRulesActorReference.Party("infuse-target-actor");
        DungeonEffectStateCodecCatalog catalog = DungeonEffectStateCodecCatalog.CreateProduction();

        (string kind, string payload) = catalog.Capture(
            SpellFeatureRules.InfuseVitalityEffect,
            state,
            creature => creature == originalTarget ? actor : default
        );
        SpellEffectState restored = (SpellEffectState)
            catalog.Restore(
                SpellFeatureRules.InfuseVitalityEffect,
                kind,
                payload,
                reference => reference.Equals(actor) ? restoredTarget : default
            );

        Assert.That(restored.Spell, Is.EqualTo(state.Spell));
        Assert.That(restored.Spell.Rank, Is.EqualTo(5));
        Assert.That(restored.Target, Is.EqualTo(restoredTarget));
    }

    [Test]
    public void ProductionCodecRoundTripsEveryLastingSpellDefinitionAndTargetReference()
    {
        CreatureId originalTarget = new("spell-persistence-target");
        CreatureId restoredTarget = new("spell-persistence-restored-target");
        SpellEffectState state = new(
            new SpellReference(new SpellId("guidance"), 1),
            originalTarget
        );
        DungeonRulesActorReference actor = DungeonRulesActorReference.Party("target-actor");
        DungeonEffectStateCodecCatalog catalog = DungeonEffectStateCodecCatalog.CreateProduction();

        foreach (RuleDefinitionId definition in SpellFeatureRules.PersistentDefinitionIds)
        {
            (string kind, string payload) = catalog.Capture(
                definition,
                state,
                creature => creature == originalTarget ? actor : default
            );
            SpellEffectState restored = (SpellEffectState)
                catalog.Restore(
                    definition,
                    kind,
                    payload,
                    reference => reference.Equals(actor) ? restoredTarget : default
                );

            Assert.That(kind, Is.EqualTo("spell"));
            Assert.That(restored.Spell, Is.EqualTo(state.Spell));
            Assert.That(restored.Target, Is.EqualTo(restoredTarget));
            Assert.That(
                catalog.GetReferencedActors(definition, kind, payload).Single(),
                Is.EqualTo(actor)
            );
        }
    }
}
