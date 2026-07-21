namespace Game.Creature.Rules
{
    /// <summary>
    /// Identifies generic host-side effects emitted by Unity-free rules; keep this vocabulary concrete and reusable.
    /// </summary>
    public enum RuleEffectType
    {
        GainSourceTempHp,
        RemoveSourceTempHp,
        AddTempHpImmunity,
    }

    /// <summary>
    /// Represents one generic side effect that a rule wants the Unity layer to apply.
    /// </summary>
    public sealed class RuleEffect
    {
        public RuleEffectType Type { get; }
        public string Source { get; }
        public int Amount { get; }

        private RuleEffect(RuleEffectType type, string source = null, int amount = 0)
        {
            Type = type;
            Source = source;
            Amount = amount;
        }

        /// <summary>
        /// Creates an effect that grants temporary Hit Points associated with a stable rule source.
        /// </summary>
        /// <param name="source">The source key used for later removal or immunity checks.</param>
        /// <param name="amount">The amount of temporary Hit Points to grant.</param>
        /// <returns>A generic source-tracked temporary Hit Point effect.</returns>
        public static RuleEffect GainSourceTempHp(string source, int amount)
        {
            return new RuleEffect(RuleEffectType.GainSourceTempHp, source, amount);
        }

        /// <summary>
        /// Creates an effect that removes temporary Hit Points associated with a stable rule source.
        /// </summary>
        /// <param name="source">The source key to remove.</param>
        /// <returns>A generic source-tracked temporary Hit Point removal effect.</returns>
        public static RuleEffect RemoveSourceTempHp(string source)
        {
            return new RuleEffect(RuleEffectType.RemoveSourceTempHp, source);
        }

        /// <summary>
        /// Creates an effect that prevents a source from granting temporary Hit Points again until reset by game flow.
        /// </summary>
        /// <param name="source">The source key that should become temporarily immune.</param>
        /// <returns>A generic temporary Hit Point immunity effect.</returns>
        public static RuleEffect AddTempHpImmunity(string source)
        {
            return new RuleEffect(RuleEffectType.AddTempHpImmunity, source);
        }
    }
}
