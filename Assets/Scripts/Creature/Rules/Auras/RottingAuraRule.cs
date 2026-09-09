using Game.Creature;

namespace Game.Creature.Rules
{
    /// <summary>Identifies Rotting Aura configurations used by Unity visualization.</summary>
    public sealed class RottingAuraRule : ICreatureAuraRule
    {
        /// <summary>Stable slug used by creature data and rules composition.</summary>
        public const string RuleSlug = "rotting-aura";

        /// <inheritdoc/>
        public string Slug => RuleSlug;

        /// <inheritdoc/>
        public bool HasVisual(CreatureAura aura) => aura != null && aura.radiusFeet > 0;
    }
}
