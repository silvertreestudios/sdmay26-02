using System;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Immutable committed rules state. Each seeded slice is authoritative for its domain;
    /// in particular, seeded health replaces Unity component fields as the live health authority.
    /// </summary>
    public sealed class RulesState
    {
        private readonly RulesStateData data;

        public RulesSnapshot Snapshot { get; }
        internal long Version => data.Version;

        public RulesState(RulesStateSeed seed)
        {
            if (seed == null)
                throw new ArgumentNullException(nameof(seed));
            data = new RulesStateData(seed);
            Snapshot = new RulesSnapshot(data);
        }

        internal RulesState(RulesStateData data)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            Snapshot = new RulesSnapshot(data);
        }

        internal RulesStateDraft CreateDraft() => new RulesStateDraft(data);
    }
}
