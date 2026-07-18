using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Immutable committed state. Live Unity components remain authoritative until a later migration seeds a slice.
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
