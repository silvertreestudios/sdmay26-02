using System;
using System.Collections.Generic;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using GridPrivate;
using NUnit.Framework;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace Game.Rules.Unity.Tests
{
    /// <summary>
    /// Verifies that Unity presentation references retain explicit one-to-one rules identities.
    /// </summary>
    public sealed class UnityRulesIdentityMapTests
    {
        private readonly List<GameObject> createdObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject value in createdObjects)
            {
                if (value != null)
                    UnityObject.DestroyImmediate(value);
            }
            createdObjects.Clear();
        }

        [Test]
        public void ExplicitBindingsResolveBothDirectionsForMultipleCombatantsAndAllCategories()
        {
            UnityRulesIdentityMap map = new UnityRulesIdentityMap();
            GameObject firstCreature = CreateGameObject("first creature");
            GameObject secondCreature = CreateGameObject("second creature");
            CreatureId firstCreatureId = new CreatureId("creature-encounter-a");
            CreatureId secondCreatureId = new CreatureId("creature-encounter-b");

            ReferenceEqualValue firstEquipment = new ReferenceEqualValue("equipment");
            ReferenceEqualValue secondEquipment = new ReferenceEqualValue("equipment");
            ItemId firstItemId = new ItemId("item-a");
            ItemId secondItemId = new ItemId("item-b");

            Team firstTeam = CreateGameObject("first team").AddComponent<Team>();
            Team secondTeam = CreateGameObject("second team").AddComponent<Team>();
            TeamId firstTeamId = new TeamId("team-a");
            TeamId secondTeamId = new TeamId("team-b");

            UnityObject firstPlayer = CreateGameObject("first player adapter");
            UnityObject secondPlayer = CreateGameObject("second player adapter");
            PlayerId firstPlayerId = new PlayerId("player-a");
            PlayerId secondPlayerId = new PlayerId("player-b");

            ReferenceEqualValue firstDefinition = new ReferenceEqualValue("definition");
            ReferenceEqualValue secondDefinition = new ReferenceEqualValue("definition");
            RuleDefinitionId firstDefinitionId = new RuleDefinitionId("definition-a");
            RuleDefinitionId secondDefinitionId = new RuleDefinitionId("definition-b");

            Tile firstTile = new Tile();
            Tile secondTile = new Tile();
            GridPosition firstPosition = new GridPosition(1, 0, 2);
            GridPosition secondPosition = new GridPosition(4, 1, 7);

            map.RegisterCreature(firstCreature, firstCreatureId);
            map.RegisterCreature(secondCreature, secondCreatureId);
            map.RegisterEquipment(firstEquipment, firstItemId);
            map.RegisterEquipment(secondEquipment, secondItemId);
            map.RegisterTeam(firstTeam, firstTeamId);
            map.RegisterTeam(secondTeam, secondTeamId);
            map.RegisterPlayerAdapter(firstPlayer, firstPlayerId);
            map.RegisterPlayerAdapter(secondPlayer, secondPlayerId);
            map.RegisterDefinition(firstDefinition, firstDefinitionId);
            map.RegisterDefinition(secondDefinition, secondDefinitionId);
            map.RegisterGridCell(firstTile, firstPosition);
            map.RegisterGridCell(secondTile, secondPosition);

            Assert.That(map.GetCreatureId(firstCreature), Is.EqualTo(firstCreatureId));
            Assert.That(map.GetCreatureObject(secondCreatureId), Is.SameAs(secondCreature));
            Assert.That(map.GetItemId(firstEquipment), Is.EqualTo(firstItemId));
            Assert.That(map.GetEquipment(secondItemId), Is.SameAs(secondEquipment));
            Assert.That(map.GetTeamId(firstTeam), Is.EqualTo(firstTeamId));
            Assert.That(map.GetTeamComponent(secondTeamId), Is.SameAs(secondTeam));
            Assert.That(map.GetPlayerId(firstPlayer), Is.EqualTo(firstPlayerId));
            Assert.That(map.GetPlayerAdapter(secondPlayerId), Is.SameAs(secondPlayer));
            Assert.That(map.GetRuleDefinitionId(firstDefinition), Is.EqualTo(firstDefinitionId));
            Assert.That(map.GetDefinition(secondDefinitionId), Is.SameAs(secondDefinition));
            Assert.That(map.GetGridPosition(firstTile), Is.EqualTo(firstPosition));
            Assert.That(map.GetTile(secondPosition), Is.SameAs(secondTile));
        }

        [Test]
        public void ExactReregistrationIsIdempotentAndConflictingPairsAreRejectedForEveryCategory()
        {
            UnityRulesIdentityMap map = new UnityRulesIdentityMap();

            AssertRegistrationConflicts(
                map.RegisterCreature,
                CreateGameObject("creature a"),
                CreateGameObject("creature b"),
                new CreatureId("creature-a"),
                new CreatureId("creature-b"));
            AssertRegistrationConflicts<object, ItemId>(
                map.RegisterEquipment,
                new object(),
                new object(),
                new ItemId("item-a"),
                new ItemId("item-b"));
            AssertRegistrationConflicts(
                map.RegisterTeam,
                CreateGameObject("team a").AddComponent<Team>(),
                CreateGameObject("team b").AddComponent<Team>(),
                new TeamId("team-a"),
                new TeamId("team-b"));
            AssertRegistrationConflicts<UnityObject, PlayerId>(
                map.RegisterPlayerAdapter,
                CreateGameObject("player a"),
                CreateGameObject("player b"),
                new PlayerId("player-a"),
                new PlayerId("player-b"));
            AssertRegistrationConflicts<object, RuleDefinitionId>(
                map.RegisterDefinition,
                new object(),
                new object(),
                new RuleDefinitionId("definition-a"),
                new RuleDefinitionId("definition-b"));
            AssertRegistrationConflicts(
                map.RegisterGridCell,
                new Tile(),
                new Tile(),
                new GridPosition(1, 0, 1),
                new GridPosition(2, 0, 2));
        }

        [Test]
        public void MappingsRemainStableWhenMappedAndUnrelatedGameObjectsChangeActivation()
        {
            UnityRulesIdentityMap map = new UnityRulesIdentityMap();
            GameObject creature = CreateGameObject("mapped creature");
            GameObject unrelated = CreateGameObject("unrelated object");
            CreatureId creatureId = new CreatureId("stable-creature");
            map.RegisterCreature(creature, creatureId);

            creature.SetActive(false);
            unrelated.SetActive(false);
            unrelated.SetActive(true);

            Assert.That(map.GetCreatureId(creature), Is.EqualTo(creatureId));
            Assert.That(map.GetCreatureObject(creatureId), Is.SameAs(creature));

            creature.SetActive(true);
            Assert.That(map.GetCreatureId(creature), Is.EqualTo(creatureId));
            Assert.That(map.GetCreatureObject(creatureId), Is.SameAs(creature));
        }

        [Test]
        public void RegistrationRequiresExplicitLiveObjectsAndNonEmptyIds()
        {
            UnityRulesIdentityMap map = new UnityRulesIdentityMap();
            GameObject creature = CreateGameObject("creature");

            Assert.Throws<ArgumentNullException>(() =>
                map.RegisterCreature(null, new CreatureId("creature-a")));
            Assert.Throws<ArgumentException>(() =>
                map.RegisterCreature(creature, default));
            Assert.Throws<ArgumentNullException>(() =>
                map.RegisterEquipment(null, new ItemId("item-a")));
            Assert.Throws<ArgumentException>(() =>
                map.RegisterDefinition(new object(), default));
            Assert.Throws<KeyNotFoundException>(() =>
                map.GetGridPosition(new Tile()));
        }

        private GameObject CreateGameObject(string name)
        {
            GameObject value = new GameObject(name);
            createdObjects.Add(value);
            return value;
        }

        private static void AssertRegistrationConflicts<TObject, TId>(
            Action<TObject, TId> register,
            TObject firstObject,
            TObject secondObject,
            TId firstId,
            TId secondId)
        {
            register(firstObject, firstId);

            Assert.DoesNotThrow(() => register(firstObject, firstId));
            Assert.Throws<InvalidOperationException>(() => register(firstObject, secondId));
            Assert.Throws<InvalidOperationException>(() => register(secondObject, firstId));
        }

        private sealed class ReferenceEqualValue
        {
            private readonly string value;

            public ReferenceEqualValue(string value) => this.value = value;

            public override bool Equals(object obj) =>
                obj is ReferenceEqualValue other && string.Equals(value, other.value, StringComparison.Ordinal);

            public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(value);
        }
    }
}
