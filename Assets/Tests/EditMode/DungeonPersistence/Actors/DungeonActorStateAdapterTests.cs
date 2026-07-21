using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.DungeonPersistence.Actors;
using Game.DungeonPersistence.Repository;
using Game.Rules;
using Game.Rules.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class DungeonActorStateAdapterTests
{
    private readonly List<GameObject> createdObjects = new();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject createdObject in createdObjects)
            UnityEngine.Object.DestroyImmediate(createdObject);
        createdObjects.Clear();
    }

    [Test]
    public void Capture_IsDeterministicAndIncludesDurableActorState()
    {
        TestActionController source = CreateActor("Source", 9, 9);
        TestActionController target = CreateActor("Target", 7, 12);
        ConditionSource sharedConditionSource = new();
        source
            .gameObject.AddComponent<Conditions>()
            .AddPersistent("frightened", 1, sharedConditionSource);
        ConfigureTargetState(target, source.gameObject, sharedConditionSource);

        IReadOnlyList<DungeonCreatureSaveState> first = DungeonActorStateAdapter.Capture(
            new[]
            {
                new DungeonActorCaptureTarget(target, "actor-b", "creature-b"),
                new DungeonActorCaptureTarget(source, "actor-a", "creature-a"),
            }
        );
        IReadOnlyList<DungeonCreatureSaveState> second = DungeonActorStateAdapter.Capture(
            new[]
            {
                new DungeonActorCaptureTarget(source, "actor-a", "creature-a"),
                new DungeonActorCaptureTarget(target, "actor-b", "creature-b"),
            }
        );

        Assert.That(
            first.Select(state => state.InstanceId),
            Is.EqualTo(new[] { "actor-a", "actor-b" })
        );
        Assert.That(
            first.Select(DungeonSaveJsonCodec.SerializeCreature),
            Is.EqualTo(second.Select(DungeonSaveJsonCodec.SerializeCreature))
        );
        DungeonCreatureSaveState savedTarget = first.Single(state => state.InstanceId == "actor-b");
        DungeonCreatureSaveState savedSource = first.Single(state => state.InstanceId == "actor-a");
        Assert.That(savedTarget.Cell, Is.EqualTo(new DungeonSaveCell(2, -3)));
        Assert.That(savedTarget.Health.CurrentHitPoints, Is.EqualTo(7));
        Assert.That(savedTarget.Health.TemporaryHitPoints, Is.EqualTo(3));
        Assert.That(savedTarget.Health.TemporaryHitPointSourceId, Is.EqualTo("false-life"));
        Assert.That(
            savedTarget.Health.TemporaryHitPointImmunitySourceIds,
            Is.EqualTo(new[] { "rage" })
        );
        Assert.That(savedTarget.Conditions.Single().Value, Is.EqualTo(2));
        Assert.That(
            savedTarget.Conditions.Single().SourceInstanceId,
            Is.EqualTo(savedSource.Conditions.Single().SourceInstanceId)
        );
        Assert.That(savedTarget.TimedEffects.Single().SourceCreatureId, Is.EqualTo("actor-a"));
        Assert.That(savedTarget.TimedEffects.Single().RemainingTargetTurnStarts, Is.EqualTo(4));
        Assert.That(savedTarget.PreparedRules.RollOptions, Does.Contain("custom:option"));
        Assert.That(savedTarget.PreparedRules.ActiveEffects.Single().Slug, Is.EqualTo("effect-a"));
        Assert.That(savedTarget.PreparedRules.SpellPools.Single().RemainingUses, Is.EqualTo(1));
        Assert.That(savedTarget.Equipment.Items.Count, Is.EqualTo(3));
        Assert.That(
            savedTarget.Equipment.Items.Count(item => item.ItemDefinitionId == "crossbow"),
            Is.EqualTo(2)
        );
        Assert.That(savedTarget.Equipment.Items.Count(item => !item.IsLoaded), Is.EqualTo(1));

        string json = DungeonSaveJsonCodec.SerializeCreature(savedTarget);
        Assert.That(json, Does.Not.Contain("ActionPoints"));
        Assert.That(json, Does.Not.Contain("StrikePenalty"));
        Assert.That(json, Does.Not.Contain("Reacted"));
    }

    [Test]
    public void Restore_PreservesAbsentSourceIdsAndDuplicateWeaponLoading()
    {
        TestActionController source = CreateActor("Source", 9, 9);
        TestActionController original = CreateActor("Original", 7, 12);
        ConfigureTargetState(original, source.gameObject);
        DungeonCreatureSaveState saved = WithNonCanonicalPersistenceIds(
            DungeonActorStateAdapter
                .Capture(
                    new[]
                    {
                        new DungeonActorCaptureTarget(source, "actor-a", "creature-a"),
                        new DungeonActorCaptureTarget(original, "actor-b", "creature-b"),
                    }
                )
                .Single(state => state.InstanceId == "actor-b")
        );

        TestActionController restored = CreateActor("Restored", 12, 12);
        CreatureComponent restoredCreature = restored.GetComponent<CreatureComponent>();
        ConfigureDefinitionState(restoredCreature);
        restored.transform.position = new Vector3(99, 6, 99);
        DungeonActorStateAdapter.PreflightRestore(restored, saved).Apply();

        Assert.That(restored.transform.position, Is.EqualTo(new Vector3(2, 6, -3)));
        Assert.That(restoredCreature.Health.Current, Is.EqualTo(7));
        Assert.That(restoredCreature.Health.Temporary, Is.EqualTo(3));
        Assert.That(restoredCreature.Health.TemporarySource.Slug, Is.EqualTo("false-life"));
        Assert.That(
            restoredCreature.Health.TemporaryHitPointImmunities.Single().Slug,
            Is.EqualTo("rage")
        );
        Assert.That(restoredCreature.weapons.Count, Is.EqualTo(2));
        Assert.That(restoredCreature.IsWeaponLoaded(restoredCreature.weapons[0]), Is.True);
        Assert.That(restoredCreature.IsWeaponLoaded(restoredCreature.weapons[1]), Is.False);
        Assert.That(restoredCreature.Prepared.RollOptions, Does.Contain("custom:option"));
        Assert.That(restoredCreature.Prepared.ActiveEffects.Single().Slug, Is.EqualTo("effect-a"));
        Assert.That(
            restoredCreature.Prepared.Spellcasting.Pools["rank-1"].UsesRemaining,
            Is.EqualTo(1)
        );

        ActiveSpellEffect effect = restored.GetComponent<SpellEffectController>().Effects.Single();
        Assert.That(effect.Source, Is.Null);
        Assert.That(effect.PersistentSourceActorId, Is.EqualTo("actor-a"));
        DungeonCreatureSaveState recaptured = DungeonActorStateAdapter.Capture(
            restored,
            "actor-b",
            "creature-b"
        );
        Assert.That(recaptured.TimedEffects.Single().SourceCreatureId, Is.EqualTo("actor-a"));
        Assert.That(
            recaptured.Conditions.Single().SourceInstanceId,
            Is.EqualTo(saved.Conditions.Single().SourceInstanceId)
        );
        Assert.That(
            recaptured.Conditions.Single().ApplicationId,
            Is.EqualTo(saved.Conditions.Single().ApplicationId)
        );
        Assert.That(
            DungeonSaveJsonCodec.SerializeCreature(recaptured),
            Is.EqualTo(DungeonSaveJsonCodec.SerializeCreature(saved)),
            "Every meaningful actor value and stable schema identity must survive save-load-save."
        );
    }

    [Test]
    public void PreflightRestore_DoesNotMutateWhenContentCannotResolve()
    {
        TestActionController original = CreateActor("Original", 7, 12);
        ConfigureTargetState(original, original.gameObject);
        DungeonCreatureSaveState saved = DungeonActorStateAdapter.Capture(
            original,
            "actor-a",
            "creature-a"
        );
        TestActionController target = CreateActor("Target", 12, 12);
        Vector3 originalPosition = new(44, 2, 55);
        target.transform.position = originalPosition;

        Assert.Throws<InvalidOperationException>(() =>
            DungeonActorStateAdapter.PreflightRestore(target, saved)
        );
        Assert.That(target.transform.position, Is.EqualTo(originalPosition));
        Assert.That(target.GetComponent<CreatureComponent>().Health.Current, Is.EqualTo(12));
        Assert.That(target.GetComponent<Conditions>(), Is.Null);
        Assert.That(target.GetComponent<SpellEffectController>(), Is.Null);
    }

    [Test]
    public void Capture_OmitsConsumedLegacyTimedEffects()
    {
        TestActionController actor = CreateActor("Actor", 8, 8);
        SpellEffectController effects = actor.gameObject.AddComponent<SpellEffectController>();
        GuidanceSpellEffect guidance = new(actor.gameObject);
        effects.AddOrRefresh(guidance);
        guidance.GetModifiers(Pf2eStatistic.AttackRoll, effects).ToArray();

        DungeonCreatureSaveState saved = DungeonActorStateAdapter.Capture(
            actor,
            "actor-a",
            "creature-a"
        );

        Assert.That(
            saved.TimedEffects.Select(effect => effect.Kind),
            Is.EqualTo(new[] { "guidance-immunity" })
        );
    }

    [Test]
    public void ValidateForRestore_RejectsUnsupportedTimedEffectWithoutUnityMutation()
    {
        DungeonTimedEffectSaveState unsupported = new(
            "actor-a/timed-effect/0000",
            "future-effect",
            "legacy-spell-effect/v1",
            "missing-defeated-source",
            "actor-a",
            "actor-a",
            0,
            1,
            "{}"
        );
        DungeonCreatureSaveState state = new(
            "actor-a",
            "creature-a",
            new DungeonSaveCell(1, 2),
            new DungeonHealthSaveState(5, 5, 0, string.Empty, Array.Empty<string>()),
            isDefeated: false,
            Array.Empty<DungeonConditionSaveState>(),
            new[] { unsupported },
            new DungeonPreparedRuleSaveState(
                Array.Empty<string>(),
                Array.Empty<DungeonPreparedEffectSaveState>(),
                Array.Empty<DungeonSpellPoolSaveState>()
            ),
            new DungeonEquipmentSaveState(
                Array.Empty<DungeonInventoryItemSaveState>(),
                Array.Empty<DungeonAmmunitionSaveState>()
            )
        );

        Assert.Throws<InvalidOperationException>(() =>
            DungeonActorStateAdapter.ValidateForRestore(new[] { state })
        );
    }

    [Test]
    public void PartyIdentity_ValidatesAtomicallyAndConfiguresOnce()
    {
        GameObject owner = CreateObject("Identity");
        DungeonPartyMemberIdentity identity = owner.AddComponent<DungeonPartyMemberIdentity>();

        Assert.Throws<ArgumentException>(() => identity.Configure("slot-a", "actor-a", " "));
        Assert.That(identity.IsConfigured, Is.False);
        Assert.That(identity.RosterSlotId, Is.Empty);

        identity.Configure("slot-a", "actor-a", "creature-a");
        Assert.That(identity.RosterSlotId, Is.EqualTo("slot-a"));
        Assert.That(identity.ActorInstanceId, Is.EqualTo("actor-a"));
        Assert.That(identity.CreatureContentId, Is.EqualTo("creature-a"));
        Assert.Throws<InvalidOperationException>(() =>
            identity.Configure("slot-b", "actor-b", "creature-b")
        );
    }

    [Test]
    public void Conditions_NormalizeIdsForLookupAndRemoval()
    {
        Conditions conditions = CreateObject("Normalized conditions").AddComponent<Conditions>();
        ConditionSource source = new();
        conditions.AddPersistent("  frightened  ", 1, source);

        Assert.That(conditions.Contains(" frightened "), Is.True);
        Assert.That(conditions.Contains(" frightened ", source), Is.True);

        conditions.Remove(" frightened ", source);

        Assert.That(conditions.Contains("frightened"), Is.False);
        Assert.That(conditions.CapturePersistentState(), Is.Empty);
    }

    [Test]
    public void AuthoredPlayerPrefabs_ProvideDistinctStableDungeonIdentities()
    {
        string[] paths =
        {
            "Assets/Prefabs/Creatures/Lena.prefab",
            "Assets/Prefabs/Creatures/Torgrim.prefab",
        };
        DungeonPartyMemberIdentity[] identities = paths
            .Select(path => AssetDatabase.LoadAssetAtPath<GameObject>(path))
            .Select(prefab => prefab.GetComponent<DungeonPartyMemberIdentity>())
            .ToArray();

        Assert.That(identities, Has.All.Not.Null);
        Assert.That(
            identities,
            Has.All.Matches<DungeonPartyMemberIdentity>(identity => identity.IsConfigured)
        );
        Assert.That(identities.Select(identity => identity.RosterSlotId), Is.Unique);
        Assert.That(identities.Select(identity => identity.ActorInstanceId), Is.Unique);
        Assert.That(identities.Select(identity => identity.CreatureContentId), Is.Unique);
    }

    private TestActionController CreateActor(string name, int currentHp, int maximumHp)
    {
        GameObject owner = CreateObject(name);
        TestActionController controller = owner.AddComponent<TestActionController>();
        CreatureComponent creature = owner.AddComponent<CreatureComponent>();
        creature.InitializeHealthBeforeEncounter(currentHp, maximumHp);
        return controller;
    }

    private GameObject CreateObject(string name)
    {
        GameObject created = new(name);
        createdObjects.Add(created);
        return created;
    }

    private static void ConfigureTargetState(
        TestActionController target,
        GameObject timedEffectSource,
        ConditionSource conditionSource = null
    )
    {
        CreatureComponent creature = target.GetComponent<CreatureComponent>();
        creature.InitializeHealthBeforeEncounter(
            new HealthState(
                7,
                12,
                3,
                RuleSource.FromSlug("false-life"),
                new[] { RuleSource.FromSlug("rage") }
            )
        );
        ConfigureDefinitionState(creature);
        creature.MarkWeaponFired(creature.weapons[1]);
        target.transform.position = new Vector3(2, 5, -3);

        Conditions conditions = target.gameObject.AddComponent<Conditions>();
        conditions.AddPersistent("frightened", 2, conditionSource ?? new ConditionSource());
        SpellEffectController effects = target.gameObject.AddComponent<SpellEffectController>();
        BlessSpellEffect bless = new(timedEffectSource);
        bless.RestorePersistenceState(4, consumed: false);
        effects.AddOrRefresh(bless);
    }

    private static void ConfigureDefinitionState(CreatureComponent creature)
    {
        EquipmentWeapon first = NewCrossbow();
        EquipmentWeapon second = NewCrossbow();
        EquipmentArmor armor = new() { name = "Leather Armor" };
        creature.weapons = new List<EquipmentWeapon> { first, second };
        creature.armor = new List<EquipmentArmor> { armor };
        creature.equippedLeftHand = first;
        creature.equippedArmor = armor;
        creature.ammunition = new List<AmmoCount>
        {
            new() { ammoName = "Bolt", quantity = 6 },
        };

        PreparedCharacter prepared = new(new CharacterBuild());
        prepared.RollOptions.Add("custom:option");
        prepared.ActiveEffects.Add(new ActivePf2eEffect("Effect A", "effect-a", "source-a"));
        prepared.Spellcasting = new SpellcastingState();
        SpellSlotPool pool = new("rank-1", SpellSlotKind.Prepared, 1, 2);
        pool.Spend();
        prepared.Spellcasting.AddPool(pool);
        creature.Prepared = prepared;
    }

    private static EquipmentWeapon NewCrossbow() =>
        new()
        {
            name = "Crossbow",
            reload = "1",
            ammo = "Bolt",
            hands = 1,
            damage = new Dice(1, 8, "piercing"),
        };

    private static DungeonCreatureSaveState WithNonCanonicalPersistenceIds(
        DungeonCreatureSaveState state
    )
    {
        DungeonConditionSaveState[] conditions = state
            .Conditions.Select(
                (condition, index) =>
                    new DungeonConditionSaveState(
                        $"condition-application-custom-{index}",
                        condition.ConditionId,
                        condition.SourceInstanceId,
                        condition.Value
                    )
            )
            .ToArray();
        DungeonTimedEffectSaveState[] timedEffects = state
            .TimedEffects.Select(
                (effect, index) =>
                    new DungeonTimedEffectSaveState(
                        $"timed-effect-custom-{index}",
                        effect.Kind,
                        effect.StateDiscriminator,
                        effect.SourceCreatureId,
                        effect.OwnerCreatureId,
                        effect.TargetCreatureId,
                        100 + index,
                        effect.RemainingTargetTurnStarts,
                        effect.StateJson
                    )
            )
            .ToArray();
        DungeonPreparedEffectSaveState[] preparedEffects = state
            .PreparedRules.ActiveEffects.Select(
                (effect, index) =>
                    new DungeonPreparedEffectSaveState(
                        $"prepared-effect-custom-{index}",
                        effect.Name,
                        effect.Slug,
                        effect.SourceSlug
                    )
            )
            .ToArray();
        DungeonInventoryItemSaveState[] items = state
            .Equipment.Items.Select(
                (item, index) =>
                    new DungeonInventoryItemSaveState(
                        $"inventory-entry-custom-{index}",
                        item.ItemDefinitionId,
                        item.Quantity,
                        item.Slot,
                        item.IsLoaded
                    )
            )
            .ToArray();
        return new DungeonCreatureSaveState(
            state.InstanceId,
            state.CreatureContentId,
            state.Cell,
            state.Health,
            state.IsDefeated,
            conditions,
            timedEffects,
            new DungeonPreparedRuleSaveState(
                state.PreparedRules.RollOptions,
                preparedEffects,
                state.PreparedRules.SpellPools
            ),
            new DungeonEquipmentSaveState(items, state.Equipment.Ammunition)
        );
    }

    private sealed class TestActionController : ActionController
    {
        public override void EndTurn() { }
    }
}
