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
        public void StrikeProfileDefaultsSourceInfoToUnspecified()
        {
            StrikeProfile profile = new StrikeProfile(new List<Dice> { new Dice(1, 6, "slashing") }, new List<DamageValue>());

            Assert.AreSame(AttackSourceInfo.Unspecified, profile.SourceInfo);
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

            StrikeResolutionResult normal = ResolveForcedStrike(attacker, target, DegreeOfSuccess.Success, new List<string> { "deadly-d10" });

            Assert.AreEqual(1u, normal.FinalAppliedDamage);
            Assert.IsFalse(normal.LogDetails.Any(detail => detail.Value.Contains("deadly-d10 critical damage")));

            StrikeResolutionResult critical = ResolveForcedStrike(attacker, target, DegreeOfSuccess.CriticalSuccess, new List<string> { "deadly-d10" });

            Assert.Greater(critical.FinalAppliedDamage, 2u);
            Assert.LessOrEqual(critical.FinalAppliedDamage, 12u);
            Assert.IsTrue(critical.LogDetails.Any(detail => detail.Value.Contains("deadly-d10 critical damage")));

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

            StrikeResolutionResult normal = ResolveForcedStrike(attacker, target, DegreeOfSuccess.Success, new List<string> { "fatal-d12" }, null, new List<Dice> { new Dice(1, 6, "piercing") });

            Assert.AreEqual(6, normal.Context.DamageDice[0].sidesPerDie);
            Assert.GreaterOrEqual(normal.FinalAppliedDamage, 1u);
            Assert.LessOrEqual(normal.FinalAppliedDamage, 6u);

            StrikeResolutionResult critical = ResolveForcedStrike(attacker, target, DegreeOfSuccess.CriticalSuccess, new List<string> { "fatal-d12" }, null, new List<Dice> { new Dice(1, 6, "piercing") });

            Assert.AreEqual(12, critical.Context.DamageDice[0].sidesPerDie);
            Assert.GreaterOrEqual(critical.FinalAppliedDamage, 3u);
            Assert.LessOrEqual(critical.FinalAppliedDamage, 36u);
            Assert.IsTrue(critical.LogDetails.Any(detail => detail.Value.Contains("fatal-d12 upgrades critical damage dice")));
            Assert.IsTrue(critical.LogDetails.Any(detail => detail.Value.Contains("fatal-d12 critical damage")));

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
            TestStrikeAdjustmentProvider provider = attacker.AddComponent<TestStrikeAdjustmentProvider>();
            provider.Effects.Add(new ForceDegreeStrikeAdjustment(DegreeOfSuccess.CriticalSuccess));
            provider.Effects.Add(new RecordingStrikeAdjustment(StrikeAdjustmentPhase.BeforeDamageRoll, 0, context =>
            {
                events.Add("before-roll");
                context.FlatDamages.Add(new DamageValue("piercing", 4));
            }));
            provider.Effects.Add(new RecordingStrikeAdjustment(StrikeAdjustmentPhase.AfterCriticalDoubling, 50, context =>
            {
                events.Add("after-critical:" + context.DamageValues[0].DamageAmount);
                context.DamageValues = DamageRoller.AddOrMergeDamage(context.DamageValues, new DamageValue("piercing", 2));
            }));
            provider.Effects.Add(new RecordingStrikeAdjustment(StrikeAdjustmentPhase.BeforeDefenseAdjustments, 0, context => events.Add("before-defense:" + context.DamageValues[0].DamageAmount)));
            provider.Effects.Add(new RecordingStrikeAdjustment(StrikeAdjustmentPhase.AfterDamageApplied, 0, context => events.Add("after-damage:" + context.FinalAppliedDamage)));

            StrikeResolutionResult result = ResolveStrike(attacker, target, null, null, new List<Dice>(), new List<DamageValue> { new DamageValue("piercing", 6) });

            CollectionAssert.AreEqual(new[] { "before-roll", "after-critical:20", "before-defense:22", "after-damage:19" }, events);
            Assert.AreEqual(19u, result.FinalAppliedDamage);
            Assert.AreEqual(81, target.GetComponent<CreatureComponent>().hp);

            UnityEngine.Object.DestroyImmediate(attacker);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(logObject);
        }

        [Test]
        public void WeaponStrikePopulatesStrikeResolutionContextSourceInfo()
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
            StrikeResolutionContext context = BuildContext(attacker, target, action.GetStrikeProfile().Traits, action.GetStrikeProfile().SourceInfo);

            Assert.AreSame(attacker, context.AttackerObject);
            Assert.AreSame(target, context.TargetObject);
            Assert.AreSame(attacker.GetComponent<CreatureComponent>(), context.AttackerCreature);
            Assert.AreSame(target.GetComponent<CreatureComponent>(), context.TargetCreature);
            Assert.AreEqual("Shortbow", context.SourceInfo.Name);
            Assert.AreEqual("bow", context.SourceInfo.Group);
            Assert.AreEqual("martial", context.SourceInfo.Category);
            Assert.AreSame(shortbow, context.SourceInfo.EquipmentWeapon);
            Assert.Contains("deadly-d10", context.Traits);
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
            TestStrikeAdjustmentProvider provider = target.AddComponent<TestStrikeAdjustmentProvider>();
            provider.Effects.Add(new CriticalSpecializationStrikeAdjustment("bow", context =>
            {
                provider.Calls += 1;
                provider.LastContext = context;
            }));
            AttackSourceInfo source = new AttackSourceInfo("Shortbow", "bow", "martial", new List<string> { "deadly-d10" });

            StrikeResolutionResult normal = ResolveForcedStrike(attacker, target, DegreeOfSuccess.Success, null, source);
            Assert.AreEqual(0, provider.Calls);

            StrikeResolutionResult critical = ResolveForcedStrike(attacker, target, DegreeOfSuccess.CriticalSuccess, null, source);
            Assert.AreEqual(1, provider.Calls);
            Assert.AreSame(critical.Context, provider.LastContext);
            Assert.Greater(critical.FinalAppliedDamage, 0u);
            Assert.IsTrue(normal.Hit);

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
            creature.AddComponent<TestActionController>();
            component.hp = hp;
            component.maxHp = hp;
            component.ac = 10;
            component.attackBonus = 10;
            component.weaknesses = new List<DamageValue>();
            component.resistances = new List<DamageValue>();
            return creature;
        }

        private static StrikeResolutionResult ResolveForcedStrike(
            GameObject attacker,
            GameObject target,
            DegreeOfSuccess degree,
            List<string> traits = null,
            AttackSourceInfo sourceInfo = null,
            List<Dice> damageDice = null,
            List<DamageValue> flatDamages = null)
        {
            TestStrikeAdjustmentProvider provider = attacker.GetComponent<TestStrikeAdjustmentProvider>() ?? attacker.AddComponent<TestStrikeAdjustmentProvider>();
            provider.Effects.Add(new ForceDegreeStrikeAdjustment(degree));
            return ResolveStrike(attacker, target, traits, sourceInfo, damageDice, flatDamages);
        }

        private static StrikeResolutionResult ResolveStrike(
            GameObject attacker,
            GameObject target,
            List<string> traits = null,
            AttackSourceInfo sourceInfo = null,
            List<Dice> damageDice = null,
            List<DamageValue> flatDamages = null)
        {
            StrikeProfile profile = BuildProfile(traits, sourceInfo, damageDice, flatDamages);
            return StrikeResolutionPipeline.Resolve(new StrikeResolutionRequest
            {
                Attacker = attacker,
                Target = target,
                Profile = profile,
                TargetingResult = new StrikeTargetResult
                {
                    Target = target,
                    LineOfEffect = StrikeLineOfEffect.Clear,
                    Cover = StrikeCover.None
                }
            });
        }

        private static StrikeResolutionContext BuildContext(
            GameObject attacker,
            GameObject target,
            List<string> traits = null,
            AttackSourceInfo sourceInfo = null,
            List<Dice> damageDice = null,
            List<DamageValue> flatDamages = null)
        {
            return StrikeResolutionContext.FromRequest(new StrikeResolutionRequest
            {
                Attacker = attacker,
                Target = target,
                Profile = BuildProfile(traits, sourceInfo, damageDice, flatDamages),
                TargetingResult = new StrikeTargetResult
                {
                    Target = target,
                    LineOfEffect = StrikeLineOfEffect.Clear,
                    Cover = StrikeCover.None
                }
            });
        }

        private static StrikeProfile BuildProfile(List<string> traits, AttackSourceInfo sourceInfo, List<Dice> damageDice, List<DamageValue> flatDamages)
        {
            List<Dice> dice = damageDice ?? new List<Dice> { new Dice(1, 1, "piercing") };
            List<DamageValue> flats = flatDamages ?? new List<DamageValue>();
            return new StrikeProfile(dice, flats)
            {
                Traits = traits ?? new List<string>(),
                SourceInfo = sourceInfo
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

        private class TestActionController : ActionController
        {
            public override void EndTurn() { }
        }

        private class TestStrikeAdjustmentProvider : MonoBehaviour, IStrikeAdjustmentProvider
        {
            public readonly List<IStrikeAdjustment> Effects = new();
            public int Calls;
            public StrikeResolutionContext LastContext;

            public IEnumerable<IStrikeAdjustment> GetStrikeAdjustments(StrikeResolutionContext context)
            {
                return Effects;
            }
        }

        private class ForceDegreeStrikeAdjustment : StrikeAdjustmentBase
        {
            private readonly DegreeOfSuccess degree;

            public ForceDegreeStrikeAdjustment(DegreeOfSuccess degree)
                : base(StrikeAdjustmentPhase.AfterAttackRoll, -100, "Force degree")
            {
                this.degree = degree;
            }

            public override void Apply(StrikeResolutionContext context)
            {
                context.Degree = degree;
                context.D20Result = new D20Result { roll = degree == DegreeOfSuccess.CriticalSuccess ? 20 : 10, total = 30, degree = degree };
                context.IsHit = degree == DegreeOfSuccess.Success || degree == DegreeOfSuccess.CriticalSuccess;
            }
        }

        private class RecordingStrikeAdjustment : StrikeAdjustmentBase
        {
            private readonly Action<StrikeResolutionContext> apply;

            public RecordingStrikeAdjustment(StrikeAdjustmentPhase phase, int order, Action<StrikeResolutionContext> apply)
                : base(phase, order, "Recording")
            {
                this.apply = apply;
            }

            public override void Apply(StrikeResolutionContext context)
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
