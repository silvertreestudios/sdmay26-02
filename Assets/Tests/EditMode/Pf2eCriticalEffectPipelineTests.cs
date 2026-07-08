using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Creature;
using Game.Strikes;
using GridPublic;
using NUnit.Framework;
using UnityEngine;

namespace TestsCombat
{
    public class Pf2eCriticalEffectPipelineTests
    {
        [Test]
        public void StrikeDefaultsSourceInfoToUnspecified()
        {
            Strike strike = new Strike(new List<Dice> { new Dice(1, 6, "slashing") }, new List<DamageValue>());

            Assert.AreSame(AttackSourceInfo.Unspecified, strike.SourceInfo);
        }

        [Test]
        public void DeadlyTraitAddsExtraDieOnlyOnCriticalThroughPipeline()
        {
            // PF2e sources:
            // Strike critical damage: https://2e.aonprd.com/Rules.aspx?ID=2343
            // Deadly trait: https://2e.aonprd.com/Traits.aspx?ID=570
            GameObject logObject = new GameObject("test-combat-log");
            TestCombatLog log = InstallTestCombatLog(logObject);
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            UnityEngine.Random.InitState(76);
            GameObject attacker = CreateCreature("attacker", 100);
            GameObject target = CreateCreature("target", 100);

            AttackResultContext normal = BuildPipelineContext(attacker, target, DegreeOfSuccess.Success, new List<string> { "deadly-d10" });
            AttackResultPipeline.ProcessHit(normal);

            Assert.AreEqual(1u, normal.FinalAppliedDamage);
            Assert.IsFalse(log.Messages.Any(message => message.Contains("deadly-d10 critical damage")));

            AttackResultContext critical = BuildPipelineContext(attacker, target, DegreeOfSuccess.CriticalSuccess, new List<string> { "deadly-d10" });
            AttackResultPipeline.ProcessHit(critical);

            Assert.Greater(critical.FinalAppliedDamage, 2u);
            Assert.LessOrEqual(critical.FinalAppliedDamage, 12u);
            Assert.IsTrue(log.Messages.Any(message => message.Contains("deadly-d10 critical damage")));

            UnityEngine.Random.state = randomState;
            UnityEngine.Object.DestroyImmediate(attacker);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(logObject);
        }

        [Test]
        public void FatalTraitUsesPreRollUpgradeAndPostDoubleExtraDieOnlyOnCritical()
        {
            // PF2e sources:
            // Strike critical damage: https://2e.aonprd.com/Rules.aspx?ID=2343
            // Fatal trait: https://2e.aonprd.com/Traits.aspx?ID=597
            GameObject logObject = new GameObject("test-combat-log");
            TestCombatLog log = InstallTestCombatLog(logObject);
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            UnityEngine.Random.InitState(32);
            GameObject attacker = CreateCreature("attacker", 100);
            GameObject target = CreateCreature("target", 100);

            AttackResultContext normal = BuildPipelineContext(attacker, target, DegreeOfSuccess.Success, new List<string> { "fatal-d12" }, null, new List<Dice> { new Dice(1, 6, "piercing") });
            AttackResultPipeline.ProcessHit(normal);

            Assert.AreEqual(6, normal.DamageDice[0].sidesPerDie);
            Assert.GreaterOrEqual(normal.FinalAppliedDamage, 1u);
            Assert.LessOrEqual(normal.FinalAppliedDamage, 6u);

            AttackResultContext critical = BuildPipelineContext(attacker, target, DegreeOfSuccess.CriticalSuccess, new List<string> { "fatal-d12" }, null, new List<Dice> { new Dice(1, 6, "piercing") });
            AttackResultPipeline.ProcessHit(critical);

            Assert.AreEqual(12, critical.DamageDice[0].sidesPerDie);
            Assert.GreaterOrEqual(critical.FinalAppliedDamage, 3u);
            Assert.LessOrEqual(critical.FinalAppliedDamage, 36u);
            Assert.IsTrue(log.Messages.Any(message => message.Contains("fatal-d12 upgrades critical damage dice")));
            Assert.IsTrue(log.Messages.Any(message => message.Contains("fatal-d12 critical damage")));

            UnityEngine.Random.state = randomState;
            UnityEngine.Object.DestroyImmediate(attacker);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(logObject);
        }

        [Test]
        public void PipelineRunsEffectsInPhaseOrderAndAppliesDefenseBeforeFinalDamage()
        {
            GameObject logObject = new GameObject("test-combat-log");
            InstallTestCombatLog(logObject);
            GameObject attacker = CreateCreature("attacker", 100);
            GameObject target = CreateCreature("target", 100);
            target.GetComponent<CreatureComponent>().resistances = new List<DamageValue> { new DamageValue("piercing", 3) };
            List<string> events = new();
            TestAttackResultEffectProvider provider = attacker.AddComponent<TestAttackResultEffectProvider>();
            provider.Effects.Add(new RecordingAttackResultEffect(AttackResultEffectPhase.BeforeDamageRoll, context =>
            {
                events.Add("before-roll");
                context.FlatDamages.Add(new DamageValue("piercing", 4));
            }));
            provider.Effects.Add(new RecordingAttackResultEffect(AttackResultEffectPhase.AfterCriticalDoubling, context =>
            {
                events.Add("after-critical:" + context.DamageValues[0].DamageAmount);
                context.DamageValues = DamageRoller.AddOrMergeDamage(context.DamageValues, new DamageValue("piercing", 2));
            }));
            provider.Effects.Add(new RecordingAttackResultEffect(AttackResultEffectPhase.BeforeDefenseAdjustments, context => events.Add("before-defense:" + context.DamageValues[0].DamageAmount)));
            provider.Effects.Add(new RecordingAttackResultEffect(AttackResultEffectPhase.AfterDamageApplied, context => events.Add("after-damage:" + context.FinalAppliedDamage)));

            AttackResultContext result = BuildPipelineContext(attacker, target, DegreeOfSuccess.CriticalSuccess, null, null, new List<Dice>(), new List<DamageValue> { new DamageValue("piercing", 6) });
            AttackResultPipeline.ProcessHit(result);

            CollectionAssert.AreEqual(new[] { "before-roll", "after-critical:20", "before-defense:22", "after-damage:19" }, events);
            Assert.AreEqual(19u, result.FinalAppliedDamage);
            Assert.AreEqual(81, target.GetComponent<CreatureComponent>().hp);

            UnityEngine.Object.DestroyImmediate(attacker);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(logObject);
        }

        [Test]
        public void WeaponStrikePopulatesAttackResultContextSourceInfo()
        {
            GameObject attacker = CreateCreature("attacker", 100);
            GameObject target = CreateCreature("target", 100);
            EquipmentWeapon shortbow = new EquipmentWeapon
            {
                name = "Shortbow",
                group = "bow",
                category = "martial",
                range = 60,
                reload = "0",
                ammo = "arrows",
                damage = new Dice(1, 6, "piercing"),
                traits = new List<string> { "deadly-d10" }
            };

            StrikeWeapon action = new StrikeWeapon(1, shortbow, attacker);
            AttackResultContext context = BuildPipelineContext(attacker, target, DegreeOfSuccess.CriticalSuccess, action.GetStrike().Traits, action.GetStrike().SourceInfo);

            Assert.AreSame(attacker, context.AttackerObject);
            Assert.AreSame(target, context.TargetObject);
            Assert.AreSame(attacker.GetComponent<CreatureComponent>(), context.AttackerCreature);
            Assert.AreSame(target.GetComponent<CreatureComponent>(), context.TargetCreature);
            Assert.AreEqual("Shortbow", context.SourceInfo.Name);
            Assert.AreEqual("bow", context.SourceInfo.Group);
            Assert.AreEqual("martial", context.SourceInfo.Category);
            Assert.AreSame(shortbow, context.SourceInfo.EquipmentWeapon);
            Assert.Contains("deadly-d10", context.Traits);
            Assert.AreEqual(DegreeOfSuccess.CriticalSuccess, context.Degree);
            Assert.AreEqual(target, context.TargetingResult.Target);

            UnityEngine.Object.DestroyImmediate(attacker);
            UnityEngine.Object.DestroyImmediate(target);
        }

        [Test]
        public void TargetProviderReceivesContextAfterCriticalDamageApplied()
        {
            GameObject logObject = new GameObject("test-combat-log");
            InstallTestCombatLog(logObject);
            GameObject attacker = CreateCreature("attacker", 100);
            GameObject target = CreateCreature("target", 100);
            TestAttackResultEffectProvider provider = target.AddComponent<TestAttackResultEffectProvider>();
            provider.Effects.Add(new CriticalSpecializationAttackResultEffect("bow", context =>
            {
                provider.Calls += 1;
                provider.LastContext = context;
            }));
            AttackSourceInfo source = new AttackSourceInfo("Shortbow", "bow", "martial", new List<string> { "deadly-d10" });

            AttackResultContext normal = BuildPipelineContext(attacker, target, DegreeOfSuccess.Success, null, source);
            AttackResultPipeline.ProcessHit(normal);
            Assert.AreEqual(0, provider.Calls);

            AttackResultContext critical = BuildPipelineContext(attacker, target, DegreeOfSuccess.CriticalSuccess, null, source);
            AttackResultPipeline.ProcessHit(critical);
            Assert.AreEqual(1, provider.Calls);
            Assert.AreSame(critical, provider.LastContext);
            Assert.Greater(critical.FinalAppliedDamage, 0u);

            UnityEngine.Object.DestroyImmediate(attacker);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(logObject);
        }

        [Test]
        public void DamageMergeReturnsNewListWithoutMutatingInput()
        {
            List<DamageValue> original = new() { new DamageValue("piercing", 2) };

            List<DamageValue> merged = DamageRoller.AddOrMergeDamage(original, new DamageValue("piercing", 3));

            Assert.AreEqual(2, original[0].DamageAmount);
            Assert.AreEqual(5, merged[0].DamageAmount);
            Assert.AreNotSame(original, merged);
        }

        private static GameObject CreateCreature(string name, int hp)
        {
            GameObject creature = new GameObject(name);
            CreatureComponent component = creature.AddComponent<CreatureComponent>();
            component.hp = hp;
            component.maxHp = hp;
            component.weaknesses = new List<DamageValue>();
            component.resistances = new List<DamageValue>();
            return creature;
        }

        private static AttackResultContext BuildPipelineContext(
            GameObject attacker,
            GameObject target,
            DegreeOfSuccess degree,
            List<string> traits = null,
            AttackSourceInfo sourceInfo = null,
            List<Dice> damageDice = null,
            List<DamageValue> flatDamages = null)
        {
            List<Dice> dice = damageDice ?? new List<Dice> { new Dice(1, 1, "piercing") };
            List<DamageValue> flats = flatDamages ?? new List<DamageValue>();
            Strike strike = new Strike(dice, flats)
            {
                Traits = traits ?? new List<string>(),
                SourceInfo = sourceInfo
            };

            return new AttackResultContext
            {
                AttackerObject = attacker,
                TargetObject = target,
                AttackerCreature = attacker.GetComponent<CreatureComponent>(),
                TargetCreature = target.GetComponent<CreatureComponent>(),
                Strike = strike,
                SourceInfo = sourceInfo,
                Traits = traits,
                D20Result = new D20Result { roll = degree == DegreeOfSuccess.CriticalSuccess ? 20 : 10, total = 30, degree = degree },
                Degree = degree,
                DamageDice = dice,
                FlatDamages = flats,
                DamageValues = new List<DamageValue>(),
                TargetingResult = new StrikeTargetResult
                {
                    Target = target,
                    LineOfEffect = StrikeLineOfEffect.Clear,
                    Cover = StrikeCover.None
                },
                BaseArmorClass = 20,
                TargetArmorClass = 20,
                AttackBonus = 10,
                TotalAttackModifier = 10
            };
        }

        private static TestCombatLog InstallTestCombatLog(GameObject logObject)
        {
            TestCombatLog log = logObject.AddComponent<TestCombatLog>();
            FieldInfo field = typeof(SingletonMonoBehaviour<CombatLogInterface>).GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(null, log);
            return log;
        }

        private class TestAttackResultEffectProvider : MonoBehaviour, IAttackResultEffectProvider
        {
            public readonly List<IAttackResultEffect> Effects = new();
            public int Calls;
            public AttackResultContext LastContext;

            public IEnumerable<IAttackResultEffect> GetAttackResultEffects(AttackResultContext context)
            {
                return Effects;
            }
        }

        private class RecordingAttackResultEffect : IAttackResultEffect
        {
            private readonly Action<AttackResultContext> apply;

            public RecordingAttackResultEffect(AttackResultEffectPhase phase, Action<AttackResultContext> apply)
            {
                Phase = phase;
                this.apply = apply;
            }

            public AttackResultEffectPhase Phase { get; }

            public void Apply(AttackResultContext context)
            {
                apply?.Invoke(context);
            }
        }

        private class TestCombatLog : CombatLogInterface
        {
            public readonly List<string> Messages = new();

            public override void DevMode() { }
            public override void ReleaseMode() { }
            public override void AddWhiteList(string tag) { }
            public override void AddBlackList(string tag) { }
            public override void DevLog(string msg) => Messages.Add(msg);
            public override void DevLog(string msg, string tag) => Messages.Add(msg);
            public override void DevLog(string msg, List<string> tags) => Messages.Add(msg);
            public override void Log(string msg) => Messages.Add(msg);
            public override void Log(string msg, string tag) => Messages.Add(msg);
            public override void Log(string msg, List<string> tags) => Messages.Add(msg);
            public override List<string> GetMessages() => new(Messages);
        }
    }
}
