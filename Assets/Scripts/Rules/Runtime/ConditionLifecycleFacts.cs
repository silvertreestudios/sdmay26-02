namespace Game.Rules.Runtime
{
    internal abstract class ConditionFact : RuleFact
    {
        private protected ConditionFact(ActiveEffectInstance effect, ActiveRuleBinding binding)
        {
            EffectId = effect.Id;
            DefinitionId = effect.DefinitionId;
            BindingId = binding.Id;
            Owner = binding.Owner;
            StableSource = effect.Source;
            SourceCreature = effect.SourceCreature;
        }

        internal ActiveEffectId EffectId { get; }
        internal RuleDefinitionId DefinitionId { get; }
        internal BindingId BindingId { get; }
        internal CreatureId Owner { get; }
        internal RuleSource StableSource { get; }
        internal CreatureId SourceCreature { get; }
    }

    internal sealed class ConditionCreatedFact : ConditionFact
    {
        internal ConditionCreatedFact(ActiveEffectInstance effect, ActiveRuleBinding binding)
            : base(effect, binding)
        {
            Version = effect.EffectStateVersion;
            State = effect.State;
        }

        internal EffectStateVersion Version { get; }
        internal IEffectState State { get; }
    }

    internal sealed class ConditionStateUpdatedFact : ConditionFact
    {
        internal ConditionStateUpdatedFact(
            ActiveEffectInstance effect,
            ActiveRuleBinding binding,
            EffectStateVersion previousVersion,
            EffectStateVersion currentVersion,
            IEffectState previousState,
            IEffectState currentState
        )
            : base(effect, binding)
        {
            PreviousVersion = previousVersion;
            CurrentVersion = currentVersion;
            PreviousState = previousState;
            CurrentState = currentState;
        }

        internal EffectStateVersion PreviousVersion { get; }
        internal EffectStateVersion CurrentVersion { get; }
        internal IEffectState PreviousState { get; }
        internal IEffectState CurrentState { get; }
    }

    internal sealed class ConditionExpiredFact : ConditionFact
    {
        internal ConditionExpiredFact(
            ActiveEffectInstance effect,
            ActiveRuleBinding binding,
            EffectStateVersion previousVersion,
            EffectStateVersion currentVersion
        )
            : base(effect, binding)
        {
            PreviousVersion = previousVersion;
            CurrentVersion = currentVersion;
            State = effect.State;
        }

        internal EffectStateVersion PreviousVersion { get; }
        internal EffectStateVersion CurrentVersion { get; }
        internal IEffectState State { get; }
    }

    internal sealed class ConditionRemovedFact : ConditionFact
    {
        internal ConditionRemovedFact(ActiveEffectInstance effect, ActiveRuleBinding binding)
            : base(effect, binding)
        {
            RemovedVersion = effect.EffectStateVersion;
            RemovedStatus = effect.Status;
            RemovedState = effect.State;
        }

        internal EffectStateVersion RemovedVersion { get; }
        internal ActiveEffectStatus RemovedStatus { get; }
        internal IEffectState RemovedState { get; }
    }
}
