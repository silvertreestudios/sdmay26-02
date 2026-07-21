using System;
using System.Collections.Generic;

namespace Game.DungeonPersistence.Repository
{
    /// <summary>
    /// Records one active prepared PF2e effect. Time-based expiration belongs to the matching
    /// <see cref="DungeonTimedEffectSaveState"/> so this record only preserves prepared membership.
    /// </summary>
    public sealed class DungeonPreparedEffectSaveState
    {
        /// <summary>Creates an active prepared-effect record.</summary>
        /// <param name="effectId">The stable prepared-effect identity.</param>
        /// <param name="name">The display name retained for diagnostics and menus.</param>
        /// <param name="slug">The runtime effect slug used by rules predicates.</param>
        /// <param name="sourceSlug">The source item or rule slug used for cleanup.</param>
        public DungeonPreparedEffectSaveState(
            string effectId,
            string name,
            string slug,
            string sourceSlug
        )
        {
            EffectId = DungeonSaveContractGuard.RequiredId(effectId, nameof(effectId));
            Name = DungeonSaveContractGuard.RequiredId(name, nameof(name));
            Slug = DungeonSaveContractGuard.RequiredId(slug, nameof(slug));
            SourceSlug = DungeonSaveContractGuard.RequiredId(sourceSlug, nameof(sourceSlug));
        }

        /// <summary>Gets the stable prepared-effect identity.</summary>
        public string EffectId { get; }

        /// <summary>Gets the display name.</summary>
        public string Name { get; }

        /// <summary>Gets the runtime effect slug.</summary>
        public string Slug { get; }

        /// <summary>Gets the source item or rule slug.</summary>
        public string SourceSlug { get; }
    }

    /// <summary>Records one spell-slot or spell-resource pool's meaningful use state.</summary>
    public sealed class DungeonSpellPoolSaveState
    {
        /// <summary>Creates a spell-resource pool record.</summary>
        /// <param name="poolId">The stable pool identity.</param>
        /// <param name="remainingUses">The currently available uses.</param>
        /// <param name="maximumUses">The maximum uses represented by the prepared pool.</param>
        public DungeonSpellPoolSaveState(string poolId, int remainingUses, int maximumUses)
        {
            PoolId = DungeonSaveContractGuard.RequiredId(poolId, nameof(poolId));
            if (maximumUses < 0)
                throw new ArgumentOutOfRangeException(nameof(maximumUses));
            if (remainingUses < 0 || remainingUses > maximumUses)
                throw new ArgumentOutOfRangeException(nameof(remainingUses));
            RemainingUses = remainingUses;
            MaximumUses = maximumUses;
        }

        /// <summary>Gets the stable pool identity.</summary>
        public string PoolId { get; }

        /// <summary>Gets the currently available uses.</summary>
        public int RemainingUses { get; }

        /// <summary>Gets the maximum uses represented by the prepared pool.</summary>
        public int MaximumUses { get; }
    }

    /// <summary>
    /// Records prepared rule state that is mutated during a run rather than regenerated from the
    /// character build alone.
    /// </summary>
    public sealed class DungeonPreparedRuleSaveState
    {
        /// <summary>Creates a deterministic prepared-rule snapshot.</summary>
        /// <param name="rollOptions">Every active prepared roll option.</param>
        /// <param name="activeEffects">Active prepared effects with unique stable IDs.</param>
        /// <param name="spellPools">Spell-resource pools with unique stable IDs.</param>
        public DungeonPreparedRuleSaveState(
            IEnumerable<string> rollOptions,
            IEnumerable<DungeonPreparedEffectSaveState> activeEffects,
            IEnumerable<DungeonSpellPoolSaveState> spellPools
        )
        {
            RollOptions = DungeonSaveContractGuard.UniqueStrings(rollOptions, nameof(rollOptions));
            ActiveEffects = DungeonSaveContractGuard.UniqueSorted(
                activeEffects,
                effect => effect.EffectId,
                nameof(activeEffects)
            );
            SpellPools = DungeonSaveContractGuard.UniqueSorted(
                spellPools,
                pool => pool.PoolId,
                nameof(spellPools)
            );
        }

        /// <summary>Gets every active prepared roll option in ordinal order.</summary>
        public IReadOnlyList<string> RollOptions { get; }

        /// <summary>Gets active prepared effects ordered by stable ID.</summary>
        public IReadOnlyList<DungeonPreparedEffectSaveState> ActiveEffects { get; }

        /// <summary>Gets spell-resource pools ordered by stable ID.</summary>
        public IReadOnlyList<DungeonSpellPoolSaveState> SpellPools { get; }
    }
}
