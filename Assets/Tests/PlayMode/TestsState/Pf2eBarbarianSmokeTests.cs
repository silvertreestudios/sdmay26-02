using System.Collections;
using System.Collections.Generic;
using Game.Creature;
using Game.Creature.Rules;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class Pf2eBarbarianSmokeTests
{
    private readonly List<GameObject> created = new();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in created)
            if (go != null)
                Object.Destroy(go);
        created.Clear();
        OnCombatStart.RemoveAllListeners();
        OnCombatEnd.RemoveAllListeners();
        OnNextTurn.RemoveAllListeners();
        Pf2eItemCatalog.ResetForTests();
    }

    [UnityTest]
    public IEnumerator TorgrimQuickTemperedRagesAtCombatStart()
    {
        GameObject teamRulesGo = Create("TeamRules");
        teamRulesGo.AddComponent<TeamRules>();
        GameObject logGo = Create("TestCombatLog");
        logGo.AddComponent<TestCombatLog>();
        GameObject managerGo = Create("CombatManager");
        CombatManager manager = managerGo.AddComponent<CombatManager>();

        GameObject torgrim = CreatureJsonConverter.CreateFromFile(
            "DataFiles/playerCharacters/Torgrim"
        );
        created.Add(torgrim);
        torgrim.AddComponent<Conditions>();
        torgrim.AddComponent<TestActionController>();
        Team torgrimTeam = torgrim.AddComponent<Team>();
        torgrimTeam.Name = "Players";

        GameObject enemy = Create("Enemy");
        CreatureComponent enemyCreature = enemy.AddComponent<CreatureComponent>();
        enemyCreature.name = "Enemy";
        enemyCreature.level = 1;
        enemyCreature.InitializeHealthBeforeEncounter(10, 10);
        enemy.AddComponent<Conditions>();
        enemy.AddComponent<TestActionController>();
        Team enemyTeam = enemy.AddComponent<Team>();
        enemyTeam.Name = "Enemies";

        manager.AddCombatant(torgrim.GetComponent<ActionController>());
        manager.AddCombatant(enemy.GetComponent<ActionController>());
        manager.StartCombat();
        yield return null;

        CreatureComponent torgrimCreature = torgrim.GetComponent<CreatureComponent>();
        Assert.That(torgrimCreature.Prepared.HasOwnedItem("quick-tempered"), Is.True);
        Assert.That(torgrimCreature.Prepared.HasActiveEffect("rage"), Is.True);
        Assert.That(
            torgrimCreature.tempHp,
            Is.EqualTo(torgrimCreature.level + torgrimCreature.conMod)
        );
    }

    [UnityTest]
    public IEnumerator LenaRoguePreparedCharacterSurvivesCombatStart()
    {
        GameObject teamRulesGo = Create("TeamRules");
        teamRulesGo.AddComponent<TeamRules>();
        GameObject logGo = Create("TestCombatLog");
        logGo.AddComponent<TestCombatLog>();
        GameObject managerGo = Create("CombatManager");
        CombatManager manager = managerGo.AddComponent<CombatManager>();

        GameObject lena = CreatureJsonConverter.CreateFromFile("DataFiles/playerCharacters/Lena");
        created.Add(lena);
        lena.AddComponent<Conditions>();
        lena.AddComponent<TestActionController>();
        Team lenaTeam = lena.AddComponent<Team>();
        lenaTeam.Name = "Players";

        GameObject enemy = Create("Enemy");
        CreatureComponent enemyCreature = enemy.AddComponent<CreatureComponent>();
        enemyCreature.name = "Enemy";
        enemyCreature.level = 1;
        enemyCreature.InitializeHealthBeforeEncounter(10, 10);
        enemy.AddComponent<Conditions>();
        enemy.AddComponent<TestActionController>();
        Team enemyTeam = enemy.AddComponent<Team>();
        enemyTeam.Name = "Enemies";

        manager.AddCombatant(lena.GetComponent<ActionController>());
        manager.AddCombatant(enemy.GetComponent<ActionController>());
        manager.StartCombat();
        yield return null;

        CreatureComponent lenaCreature = lena.GetComponent<CreatureComponent>();
        Assert.That(lenaCreature.Prepared.HasOwnedItem("rogue"), Is.True);
        Assert.That(lenaCreature.Prepared.HasOwnedItem("sneak-attack"), Is.True);
        Assert.That(lenaCreature.Prepared.HasOwnedItem("thief"), Is.True);
        Assert.That(lenaCreature.Prepared.HasActiveEffect("rage"), Is.False);
    }

    private GameObject Create(string name)
    {
        GameObject go = new(name);
        created.Add(go);
        return go;
    }

    private sealed class TestActionController : ActionController
    {
        public override void EndTurn() { }
    }

    private sealed class TestCombatLog : CombatLogInterface
    {
        private readonly List<string> messages = new();

        public override void DevMode() { }

        public override void ReleaseMode() { }

        public override void AddWhiteList(string tag) { }

        public override void AddBlackList(string tag) { }

        public override void Log(string msg) => messages.Add(msg);

        public override void DevLog(string msg) => messages.Add(msg);

        public override void DevLog(string msg, string tag) => messages.Add(msg);

        public override void DevLog(string msg, List<string> tags) => messages.Add(msg);

        public override void Log(string msg, string tag) => messages.Add(msg);

        public override void Log(string msg, List<string> tags) => messages.Add(msg);

        public override List<string> GetMessages() => new(messages);
    }
}
