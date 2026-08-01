using System;
using System.Collections.Generic;
using System.Linq;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using UnityEngine;

/// <summary>Stores persistence inputs and projects authoritative store conditions read-only.</summary>
/// <remarks>
/// This component is not rules authority. Before enrollment it carries persistence input; after
/// attachment every read is derived from the one encounter bridge snapshot.
/// </remarks>
public sealed class Conditions : MonoBehaviour
{
    private static readonly RuleDefinitionId[] Definitions =
    {
        ConditionRuleDefinitions.OffGuard,
        ConditionRuleDefinitions.Deafened,
        ConditionRuleDefinitions.Fatigued,
        ConditionRuleDefinitions.Encumbered,
        ConditionRuleDefinitions.Slowed,
        ConditionRuleDefinitions.Stunned,
        ConditionRuleDefinitions.Quickened,
    };

    private IReadOnlyList<ConditionApplicationSnapshot> restored = Array.AsReadOnly(
        Array.Empty<ConditionApplicationSnapshot>()
    );

    /// <summary>Gets canonical active condition slugs from the authoritative store.</summary>
    public IReadOnlyCollection<string> ActiveConditionNames
    {
        get
        {
            ActionController controller = GetComponent<ActionController>();
            if (
                controller != null
                && controller.TryGetCombatRules(
                    out UnityCombatRulesBridge bridge,
                    out CreatureId owner
                )
            )
                return ConditionSelectors.GetActiveSlugs(bridge.Snapshot, owner);
            return restored.Select(entry => entry.ConditionId).Distinct().ToArray();
        }
    }

    internal IReadOnlyList<ConditionApplicationSnapshot> CaptureApplications()
    {
        ActionController controller = GetComponent<ActionController>();
        if (
            controller == null
            || !controller.TryGetCombatRules(
                out UnityCombatRulesBridge bridge,
                out CreatureId owner
            )
        )
            return restored;

        return Definitions
            .SelectMany(definition =>
                ConditionSelectors.GetActiveInstances(bridge.Snapshot, owner, definition)
            )
            .Select(condition => new ConditionApplicationSnapshot(
                condition.DefinitionIdSlug(),
                condition.Source.Slug
            ))
            .OrderBy(entry => entry.ConditionId, StringComparer.Ordinal)
            .ThenBy(entry => entry.SourceKey, StringComparer.Ordinal)
            .ToArray();
    }

    internal void RestoreApplications(IEnumerable<ConditionApplicationSnapshot> applications)
    {
        if (applications == null)
            throw new ArgumentNullException(nameof(applications));
        ConditionApplicationSnapshot[] copied = applications.ToArray();
        if (
            copied.Any(application =>
                string.IsNullOrWhiteSpace(application.ConditionId)
                || string.IsNullOrWhiteSpace(application.SourceKey)
                || !ConditionInputNormalizer.TryNormalize(application.ConditionId, out _)
            )
        )
            throw new ArgumentException(
                "Restored conditions must be canonical and sourced.",
                nameof(applications)
            );
        restored = Array.AsReadOnly(
            copied
                .Select(application =>
                {
                    ConditionInputNormalizer.TryNormalize(
                        application.ConditionId,
                        out RuleDefinitionId definition
                    );
                    return new ConditionApplicationSnapshot(
                        definition.Value.Substring("condition-".Length),
                        application.SourceKey
                    );
                })
                .ToArray()
        );
    }

    internal IReadOnlyList<ConditionRegistration> CreateRegistrations(CreatureId owner)
    {
        List<ConditionRegistration> registrations = new List<ConditionRegistration>();
        int ordinal = 0;
        foreach (ConditionApplicationSnapshot application in restored)
        {
            ConditionInputNormalizer.TryNormalize(
                application.ConditionId,
                out RuleDefinitionId definition
            );
            RuleSource source = RuleSource.FromSlug(application.SourceKey);
            string suffix = $"{owner.Value}-{definition.Value}-{source.Slug}-{ordinal++}";
            ActiveEffectInstance effect = new ActiveEffectInstance(
                new ActiveEffectId($"condition-effect-{suffix}"),
                definition,
                owner,
                source,
                EffectDuration.Indefinite,
                CreateState(definition)
            );
            ActiveRuleBinding binding = new ActiveRuleBinding(
                new BindingId($"condition-binding-{suffix}"),
                definition,
                owner,
                effect.Id,
                source,
                ordinal
            );
            registrations.Add(new ConditionRegistration(effect, binding));
        }
        return registrations;
    }

    private static IEffectState CreateState(RuleDefinitionId definition)
    {
        if (
            definition == ConditionRuleDefinitions.OffGuard
            || definition == ConditionRuleDefinitions.Deafened
            || definition == ConditionRuleDefinitions.Fatigued
            || definition == ConditionRuleDefinitions.Encumbered
        )
            return ConditionMarkerState.Instance;
        if (definition == ConditionRuleDefinitions.Slowed)
            return new SlowedConditionState(1);
        if (definition == ConditionRuleDefinitions.Stunned)
            return new ValuedStunnedConditionState(1);
        return QuickenedConditionState.Unrestricted;
    }
}

internal static class ConditionProjectionExtensions
{
    internal static string DefinitionIdSlug(this ConditionSelection<IEffectState> condition) =>
        condition.Effect.DefinitionId.Value.Substring("condition-".Length);
}

/// <summary>One serialized condition/source pair at the persistence boundary.</summary>
internal readonly struct ConditionApplicationSnapshot
{
    internal ConditionApplicationSnapshot(string conditionId, string sourceKey)
    {
        ConditionId = conditionId;
        SourceKey = sourceKey;
    }

    internal string ConditionId { get; }
    internal string SourceKey { get; }
}
