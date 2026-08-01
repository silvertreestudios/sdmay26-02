namespace Game.Rules.Runtime
{
    internal abstract class ConditionFact : RuleFact
    {
        private protected ConditionFact(ActiveEffectId effectId, RuleDefinitionId definitionId)
        {
            EffectId = effectId;
            DefinitionId = definitionId;
        }

        internal ActiveEffectId EffectId { get; }
        internal RuleDefinitionId DefinitionId { get; }
    }

    internal sealed class ConditionCreatedFact : ConditionFact
    {
        internal ConditionCreatedFact(ActiveEffectInstance effect, ActiveRuleBinding binding)
            : base(effect.Id, effect.DefinitionId)
        {
            BindingId = binding.Id;
            Owner = binding.Owner;
            ConditionSource = effect.Source;
            Version = effect.EffectStateVersion;
            State = effect.State;
        }

        internal BindingId BindingId { get; }
        internal CreatureId Owner { get; }
        internal RuleSource ConditionSource { get; }
        internal EffectStateVersion Version { get; }
        internal IEffectState State { get; }
    }

    internal sealed class ConditionStateUpdatedFact : ConditionFact
    {
        internal ConditionStateUpdatedFact(
            ActiveEffectId effectId,
            RuleDefinitionId definitionId,
            EffectStateVersion previousVersion,
            EffectStateVersion currentVersion,
            IEffectState state
        )
            : base(effectId, definitionId)
        {
            PreviousVersion = previousVersion;
            CurrentVersion = currentVersion;
            State = state;
        }

        internal EffectStateVersion PreviousVersion { get; }
        internal EffectStateVersion CurrentVersion { get; }
        internal IEffectState State { get; }
    }

    internal sealed class ConditionExpiredFact : ConditionFact
    {
        internal ConditionExpiredFact(
            ActiveEffectId effectId,
            RuleDefinitionId definitionId,
            BindingId bindingId,
            EffectStateVersion previousVersion,
            EffectStateVersion currentVersion
        )
            : base(effectId, definitionId)
        {
            BindingId = bindingId;
            PreviousVersion = previousVersion;
            CurrentVersion = currentVersion;
        }

        internal BindingId BindingId { get; }
        internal EffectStateVersion PreviousVersion { get; }
        internal EffectStateVersion CurrentVersion { get; }
    }

    internal sealed class ConditionRemovedFact : ConditionFact
    {
        internal ConditionRemovedFact(
            ActiveEffectId effectId,
            RuleDefinitionId definitionId,
            BindingId bindingId,
            EffectStateVersion removedVersion,
            ActiveEffectStatus removedStatus
        )
            : base(effectId, definitionId)
        {
            BindingId = bindingId;
            RemovedVersion = removedVersion;
            RemovedStatus = removedStatus;
        }

        internal BindingId BindingId { get; }
        internal EffectStateVersion RemovedVersion { get; }
        internal ActiveEffectStatus RemovedStatus { get; }
    }
}
