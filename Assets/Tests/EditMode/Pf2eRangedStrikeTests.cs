using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.AbilityActions;
using Game.Creature;
using Game.Creature.Rules;
using Game.Strikes;
using GridPrivate;
using GridPublic;
using NUnit.Framework;
using UnityEngine;

namespace TestsCombat
{
    public class Pf2eRangedStrikeTests
    {
        [Test]
        public void NaturalTwentyAndOneAdjustDegreeByOneStep()
        {
            // PF2e source: https://2e.aonprd.com/Rules.aspx?ID=2287
            D20Result naturalTwenty = D20.Evaluate(20, -20, 25);
            D20Result naturalOne = D20.Evaluate(1, 30, 20);

            Assert.AreEqual(DegreeOfSuccess.Fail, naturalTwenty.degree);
            Assert.AreEqual(DegreeOfSuccess.Success, naturalOne.degree);
        }

        [Test]
        public void D20DegreeBoundariesUseDcAndTenOverUnder()
        {
            // PF2e source: https://2e.aonprd.com/Rules.aspx?ID=2286
            Assert.AreEqual(DegreeOfSuccess.CriticalFail, D20.Evaluate(1, 8, 20).degree);
            Assert.AreEqual(DegreeOfSuccess.Fail, D20.Evaluate(10, 9, 20).degree);
            Assert.AreEqual(DegreeOfSuccess.Success, D20.Evaluate(10, 10, 20).degree);
            Assert.AreEqual(DegreeOfSuccess.CriticalSuccess, D20.Evaluate(20, 10, 20).degree);
        }

        [Test]
        public void GridDistanceUsesAlternatingDiagonalCosts()
        {
            // PF2e source: https://2e.aonprd.com/Rules.aspx?ID=2357
            Assert.AreEqual(5, StrikeTargeting.MeasureGridDistanceFeet(Vector3Int.zero, new Vector3Int(1, 0, 1)));
            Assert.AreEqual(15, StrikeTargeting.MeasureGridDistanceFeet(Vector3Int.zero, new Vector3Int(2, 0, 2)));
            Assert.AreEqual(30, StrikeTargeting.MeasureGridDistanceFeet(Vector3Int.zero, new Vector3Int(4, 0, 4)));
        }

        [Test]
        public void RangedIncrementPenaltyCapsAtSixIncrements()
        {
            // PF2e source: https://2e.aonprd.com/Rules.aspx?ID=2288
            Assert.AreEqual(0, StrikeTargeting.CalculateRangePenalty(60, 60));
            Assert.AreEqual(-2, StrikeTargeting.CalculateRangePenalty(65, 60));
            Assert.AreEqual(-10, StrikeTargeting.CalculateRangePenalty(360, 60));
            Assert.Throws<ArgumentOutOfRangeException>(() => StrikeTargeting.CalculateRangePenalty(365, 60));
        }

        [Test]
        public void EvaluateEnforcesSixRangedIncrements()
        {
            // PF2e source: https://2e.aonprd.com/Rules.aspx?ID=2288
            Tile[,] tiles = BuildTiles(75, 1);
            GameObject attacker = new GameObject("attacker");
            GameObject target = new GameObject("target");
            attacker.transform.position = new Vector3(0, 0, 0);
            target.transform.position = new Vector3(72, 0, 0);

            StrikeTargetResult legal = StrikeTargeting.Evaluate(attacker, target, tiles, new StrikeTargetRequest
            {
                IsRanged = true,
                RangeIncrementFeet = 60
            });

            Assert.IsNotNull(legal);
            Assert.AreEqual(360, legal.DistanceFeet);
            Assert.AreEqual(-10, legal.RangePenalty);

            target.transform.position = new Vector3(73, 0, 0);
            Assert.IsNull(StrikeTargeting.Evaluate(attacker, target, tiles, new StrikeTargetRequest
            {
                IsRanged = true,
                RangeIncrementFeet = 60
            }));

            UnityEngine.Object.DestroyImmediate(attacker);
            UnityEngine.Object.DestroyImmediate(target);
        }

        [Test]
        public void SolidWallBlocksLineOfEffect()
        {
            // PF2e source: https://2e.aonprd.com/Rules.aspx?ID=2372
            Tile[,] tiles = BuildTiles(5, 1);
            tiles[2, 0] = null;
            GameObject attacker = new GameObject("attacker");
            GameObject target = new GameObject("target");
            attacker.transform.position = new Vector3(0, 0, 0);
            target.transform.position = new Vector3(4, 0, 0);

            StrikeTargetResult result = StrikeTargeting.Evaluate(attacker, target, tiles, new StrikeTargetRequest
            {
                IsRanged = true,
                RangeIncrementFeet = 60
            });

            Assert.IsNull(result);
            UnityEngine.Object.DestroyImmediate(attacker);
            UnityEngine.Object.DestroyImmediate(target);
        }

        [Test]
        public void PartialWallGivesStandardCover()
        {
            // PF2e source: https://2e.aonprd.com/Rules.aspx?ID=2372
            Tile[,] tiles = BuildTiles(3, 2);
            tiles[1, 0] = null;
            GameObject attacker = new GameObject("attacker");
            GameObject target = new GameObject("target");
            attacker.transform.position = new Vector3(0, 0, 0);
            target.transform.position = new Vector3(2, 0, 1);

            StrikeTargetResult result = StrikeTargeting.Evaluate(attacker, target, tiles, new StrikeTargetRequest
            {
                IsRanged = true,
                RangeIncrementFeet = 60
            });

            Assert.IsNotNull(result);
            Assert.AreEqual(StrikeCover.Standard, result.Cover);
            Assert.AreEqual(2, result.CoverAcBonus);
            UnityEngine.Object.DestroyImmediate(attacker);
            UnityEngine.Object.DestroyImmediate(target);
        }

        [Test]
        public void ClearLineHasNoCover()
        {
            // PF2e source: https://2e.aonprd.com/Rules.aspx?ID=2372
            Tile[,] tiles = BuildTiles(5, 1);
            GameObject attacker = new GameObject("attacker");
            GameObject target = new GameObject("target");
            attacker.transform.position = new Vector3(0, 0, 0);
            target.transform.position = new Vector3(4, 0, 0);

            StrikeTargetResult result = StrikeTargeting.Evaluate(attacker, target, tiles, new StrikeTargetRequest
            {
                IsRanged = true,
                RangeIncrementFeet = 60
            });

            Assert.IsNotNull(result);
            Assert.AreEqual(StrikeCover.None, result.Cover);
            Assert.AreEqual(0, result.CoverAcBonus);
            UnityEngine.Object.DestroyImmediate(attacker);
            UnityEngine.Object.DestroyImmediate(target);
        }

        [Test]
        public void BlockedCornerPreventsLineOfEffect()
        {
            // PF2e source: https://2e.aonprd.com/Rules.aspx?ID=2372
            Tile[,] tiles = BuildTiles(2, 2);
            tiles[0, 1] = null;
            tiles[1, 0] = null;
            GameObject attacker = new GameObject("attacker");
            GameObject target = new GameObject("target");
            attacker.transform.position = new Vector3(0, 0, 0);
            target.transform.position = new Vector3(1, 0, 1);

            StrikeTargetResult result = StrikeTargeting.Evaluate(attacker, target, tiles, new StrikeTargetRequest
            {
                IsRanged = false,
                ReachFeet = 5
            });

            Assert.IsNull(result);
            UnityEngine.Object.DestroyImmediate(attacker);
            UnityEngine.Object.DestroyImmediate(target);
        }

        [Test]
        public void MultipleAttackPenaltyUsesAgileValues()
        {
            // PF2e source: https://2e.aonprd.com/Rules.aspx?ID=2288
            MethodInfo method = typeof(Strike).GetMethod("CalculateMultipleAttackPenalty", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);

            Strike normal = new Strike(new List<Dice> { new Dice(1, 6, "slashing") }, new List<DamageValue>());
            Strike agile = new Strike(new List<Dice> { new Dice(1, 6, "piercing") }, new List<DamageValue>())
            {
                Traits = new List<string> { "agile" }
            };

            Assert.AreEqual(0, (int)method.Invoke(normal, new object[] { 0u }));
            Assert.AreEqual(5, (int)method.Invoke(normal, new object[] { 1u }));
            Assert.AreEqual(10, (int)method.Invoke(normal, new object[] { 2u }));
            Assert.AreEqual(4, (int)method.Invoke(agile, new object[] { 1u }));
            Assert.AreEqual(8, (int)method.Invoke(agile, new object[] { 2u }));
        }

        [Test]
        public void StrikeAttackLogIncludesMapRangePenaltyAndCoverAc()
        {
            // PF2e sources:
            // Attack rolls, MAP, and range penalty: https://2e.aonprd.com/Rules.aspx?ID=2288
            // Cover AC bonus: https://2e.aonprd.com/Rules.aspx?ID=2372
            GameObject logObject = new GameObject("test-combat-log");
            TestCombatLog log = InstallTestCombatLog(logObject);
            GameObject attacker = new GameObject("attacker");
            GameObject target = new GameObject("target");
            attacker.AddComponent<TestActionController>().StrikePenalty = 1;
            CreatureComponent attackerCreature = attacker.AddComponent<CreatureComponent>();
            CreatureComponent targetCreature = target.AddComponent<CreatureComponent>();
            attackerCreature.attackBonus = 7;
            targetCreature.ac = 100;
            targetCreature.hp = 100;

            Strike strike = new Strike(new List<Dice> { new Dice(1, 6, "piercing") }, new List<DamageValue>());
            strike.Damage(attacker, target, new StrikeTargetResult
            {
                Target = target,
                LineOfEffect = StrikeLineOfEffect.Clear,
                Cover = StrikeCover.Standard,
                RangePenalty = -2
            });

            string attackLog = log.Messages.FirstOrDefault(message => message.StartsWith("Attack:", StringComparison.Ordinal));
            Assert.IsNotNull(attackLog);
            StringAssert.Contains("AC: 102 (100 + 2 cover)", attackLog);
            StringAssert.Contains("+ 7 - 5 -2", attackLog);
            StringAssert.Contains("Attack Modifiers: total 0", attackLog);
            StringAssert.Contains("AC Modifiers: total 102", attackLog);

            UnityEngine.Object.DestroyImmediate(attacker);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(logObject);
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
                DamageRoller.AddOrMergeDamage(context.DamageValues, new DamageValue("piercing", 2));
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
        public void AmmoAndReloadStateAreEnforced()
        {
            GameObject creature = new GameObject("archer");
            CreatureComponent component = creature.AddComponent<CreatureComponent>();
            EquipmentWeapon sling = new EquipmentWeapon { name = "Sling", range = 50, reload = "1", ammo = "sling-bullets", damage = new Dice(1, 6, "bludgeoning") };

            component.SetAmmoQuantity("Sling Bullets", 2);

            Assert.IsTrue(component.HasAmmoFor(sling));
            Assert.IsTrue(component.IsWeaponLoaded(sling));
            Assert.IsTrue(component.ConsumeAmmoFor(sling));
            Assert.AreEqual(1, component.GetAmmoQuantity("sling-bullets"));

            component.MarkWeaponFired(sling);
            Assert.IsFalse(component.IsWeaponLoaded(sling));
            Assert.IsTrue(component.ReloadWeapon(sling));
            Assert.IsTrue(component.IsWeaponLoaded(sling));
            UnityEngine.Object.DestroyImmediate(creature);
        }

        [Test]
        public void CreatureJsonImportsRangedWeaponBonusAmmoAndReload()
        {
            GameObject goblin = CreatureJsonConverter.CreateFromFile("DataFiles/pathfinder-monster-core/goblin-warrior");
            Assert.IsNotNull(goblin);
            CreatureComponent component = goblin.GetComponent<CreatureComponent>();
            EquipmentWeapon shortbow = component.weapons.FirstOrDefault(weapon => weapon.name == "Shortbow");

            Assert.IsNotNull(shortbow);
            Assert.AreEqual(60, shortbow.range);
            Assert.AreEqual("0", shortbow.reload);
            Assert.AreEqual("arrows", shortbow.ammo);
            Assert.AreEqual(10, component.GetAmmoQuantity("arrows"));
            Assert.AreEqual(7, component.GetAttackBonusForWeapon(shortbow));

            UnityEngine.Object.DestroyImmediate(goblin);
        }

        [Test]
        public void WeaponStrikeAdderAutomaticAddsRangedAndReloadActionsWithoutDuplicates()
        {
            GameObject kobold = CreatureJsonConverter.CreateFromFile("DataFiles/pathfinder-monster-core/kobold-warrior");
            Assert.IsNotNull(kobold);
            kobold.AddComponent<TestActionController>();

            StrikeWeapon.WeaponStrikeAdderAutomatic(kobold);
            StrikeWeapon.WeaponStrikeAdderAutomatic(kobold);

            List<EntityAction> actions = kobold.GetComponent<ActionController>().GetActions();
            Assert.AreEqual(1, actions.OfType<StrikeWeapon>().Count(action => action.ActionName == "Sling"));
            Assert.AreEqual(1, actions.Count(action => action.ActionName == "Reload Sling"));
            Assert.AreEqual(20, kobold.GetComponent<CreatureComponent>().GetAmmoQuantity("sling-bullets"));

            UnityEngine.Object.DestroyImmediate(kobold);
        }

        [Test]
        public void ProjectileRangedWeaponDoesNotAddMeleeFlatDamage()
        {
            GameObject creature = new GameObject("archer");
            CreatureComponent component = creature.AddComponent<CreatureComponent>();
            component.damageBonus = 4;
            EquipmentWeapon shortbow = new EquipmentWeapon
            {
                name = "Shortbow",
                range = 60,
                reload = "0",
                ammo = "arrows",
                damage = new Dice(1, 6, "piercing"),
                traits = new System.Collections.Generic.List<string> { "deadly-d10" }
            };

            StrikeWeapon action = new StrikeWeapon(1, shortbow, creature);

            Assert.AreEqual(0, action.GetStrike().FlatDamages.Count);
            Assert.AreEqual(3.5f, action.GetStrike().GetAvgDmg());
            UnityEngine.Object.DestroyImmediate(creature);
        }

        [Test]
        public void RageDamageIgnoresProjectileStrikeWithoutMeleeFlatDamage()
        {
            // PF2e source for Rage applying additional damage only to melee Strikes: https://2e.aonprd.com/Actions.aspx?ID=2802
            GameObject creatureObject = new GameObject("raging archer");
            try
            {
                CreatureComponent creature = creatureObject.AddComponent<CreatureComponent>();
                creatureObject.AddComponent<Conditions>();
                creature.level = 1;
                creature.conMod = 1;
                creature.Build = new CharacterBuild
                {
                    ClassName = "Barbarian",
                    SubclassName = "Fury Instinct",
                    ClassFeatName = "Raging Intimidation"
                };
                creature.Prepared = Pf2eCharacterPreparer.Prepare(creature, creature.Build);
                Assert.IsTrue(new Rage(0).UseRage(creatureObject));

                Strike projectileStrike = new Strike(new List<Dice> { new Dice(1, 6, "piercing") }, new List<DamageValue>());
                projectileStrike.Traits.Add("ranged");

                Assert.DoesNotThrow(() => Pf2eRulesEngine.ApplyStrikeDamageModifiers(creature, projectileStrike));
                Assert.AreEqual(0, projectileStrike.FlatDamages.Count);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(creatureObject);
                Pf2eItemCatalog.ResetForTests();
            }
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
        private static Tile[,] BuildTiles(int width, int height)
        {
            Tile[,] tiles = new Tile[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                    tiles[x, z] = new Tile();
            }
            return tiles;
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
        private class TestActionController : ActionController
        {
            public override void EndTurn() { }
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
