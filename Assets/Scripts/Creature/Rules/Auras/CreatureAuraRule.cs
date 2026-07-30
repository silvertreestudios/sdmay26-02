using Game.Creature;
using GridPrivate;
using GridPublic;
using UnityEngine;

namespace Game.Creature.Rules
{
    public enum CreatureAuraTiming
    {
        TurnStart,
    }

    public interface IPf2eDiceRoller
    {
        int Roll(int numberOfDice, int sidesPerDie);
    }

    public sealed class UnityPf2eDiceRoller : IPf2eDiceRoller
    {
        public int Roll(int numberOfDice, int sidesPerDie)
        {
            int total = 0;
            int dice = System.Math.Max(1, numberOfDice);
            int sides = System.Math.Max(1, sidesPerDie);
            for (int i = 0; i < dice; i++)
                total += Random.Range(1, sides + 1);
            return total;
        }
    }

    public interface ICreatureAuraRule
    {
        string Slug { get; }
        CreatureAuraTiming Timing { get; }
        bool HasVisual(CreatureAura aura);
        bool CanAffect(CreatureAuraContext context);
    }

    public sealed class CreatureAuraEffectResult
    {
        public GameObject Source { get; set; }
        public GameObject Target { get; set; }
        public CreatureAura Aura { get; set; }
        public string RuleSlug { get; set; }
        public int RolledDamage { get; set; }
        public int AppliedDamage { get; set; }
        public DamageRollResolution DamageResolution { get; set; }
    }

    public sealed class CreatureAuraContext
    {
        public CreatureAuraContext(
            ActionController sourceController,
            ActionController targetController,
            CreatureComponent sourceCreature,
            CreatureComponent targetCreature,
            CreatureAura aura,
            Tile[,] tiles,
            AreaTargetResult area,
            IPf2eDiceRoller diceRoller
        )
        {
            SourceController = sourceController;
            TargetController = targetController;
            SourceCreature = sourceCreature;
            TargetCreature = targetCreature;
            Aura = aura;
            Tiles = tiles;
            Area = area;
            DiceRoller = diceRoller;
        }

        public ActionController SourceController { get; }
        public ActionController TargetController { get; }
        public CreatureComponent SourceCreature { get; }
        public CreatureComponent TargetCreature { get; }
        public CreatureAura Aura { get; }
        public Tile[,] Tiles { get; }
        public AreaTargetResult Area { get; }
        public IPf2eDiceRoller DiceRoller { get; }
        public GameObject SourceObject =>
            SourceController == null ? null : SourceController.gameObject;
        public GameObject TargetObject =>
            TargetController == null ? null : TargetController.gameObject;
    }

    public sealed class CreatureAuraInstance
    {
        public CreatureAuraInstance(
            ActionController sourceController,
            CreatureComponent sourceCreature,
            CreatureAura aura,
            ICreatureAuraRule rule
        )
        {
            SourceController = sourceController;
            SourceCreature = sourceCreature;
            Aura = aura;
            Rule = rule;
        }

        public ActionController SourceController { get; }
        public CreatureComponent SourceCreature { get; }
        public CreatureAura Aura { get; }
        public ICreatureAuraRule Rule { get; }
        public GameObject SourceObject =>
            SourceController == null ? null : SourceController.gameObject;
    }
}
