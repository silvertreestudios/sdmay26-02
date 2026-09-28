using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>Owns selection policy over scene-extracted spell reachability and authoritative creature state.</summary>
    public static class SpellTargetingRules
    {
        /// <summary>Validates selected membership after the host captures only available geometry.</summary>
        /// <param name="snapshot">Current creature and health authority.</param>
        /// <param name="actor">The caster.</param>
        /// <param name="profile">Immutable supported selection rules.</param>
        /// <param name="selection">The player's or AI's selected targets.</param>
        /// <param name="reachable">Available scene creatures within range and line of effect or the selected area.</param>
        /// <param name="friendships">The encounter's explicit faction relationships.</param>
        public static ActionValidationResult ValidateSelection(
            RulesSnapshot snapshot,
            CreatureId actor,
            SpellSelectionProfile profile,
            SpellCastSelection selection,
            IEnumerable<CreatureId> reachable,
            ICombatantFriendshipProvider friendships
        )
        {
            if (profile.Kind == SpellSelectionKind.None)
                return selection.Creatures.Count == 0
                    ? ActionValidationResult.Valid
                    : ActionValidationResult.Invalid("The spell does not select a target.");
            HashSet<CreatureId> available = new(reachable);
            if (
                profile.Kind == SpellSelectionKind.SingleCreature
                || profile.Kind == SpellSelectionKind.ExactCreatureCount
            )
            {
                foreach (CreatureId target in selection.Creatures)
                {
                    if (profile.FriendlyOnly && !AreFriendly(snapshot, actor, target, friendships))
                        return ActionValidationResult.Invalid(
                            "The selected spell target is not friendly."
                        );
                    if (!available.Contains(target))
                        return ActionValidationResult.Invalid(
                            "A selected spell target is unavailable, out of range, or has no line of effect."
                        );
                }
                return ActionValidationResult.Valid;
            }
            if (
                profile.Kind != SpellSelectionKind.Cone
                && profile.Kind != SpellSelectionKind.Emanation
            )
                return ActionValidationResult.Invalid("The spell selection shape is unsupported.");
            if (profile.Kind == SpellSelectionKind.Cone && !selection.HasAreaDirection)
                return ActionValidationResult.Invalid(
                    "A directed spell area requires its chosen direction."
                );
            available.RemoveWhere(target =>
                !snapshot.Health.TryGet(target, out HealthState health)
                || !health.IsLiving
                || (profile.FriendlyOnly && !AreFriendly(snapshot, actor, target, friendships))
            );
            if (profile.IncludeCaster)
                available.Add(actor);
            return available.SetEquals(selection.Creatures)
                ? ActionValidationResult.Valid
                : ActionValidationResult.Invalid(
                    "The selected creatures no longer match the spell area."
                );
        }

        private static bool AreFriendly(
            RulesSnapshot snapshot,
            CreatureId actor,
            CreatureId target,
            ICombatantFriendshipProvider friendships
        ) =>
            snapshot.Creatures.TryGet(actor, out CreatureState source)
            && snapshot.Creatures.TryGet(target, out CreatureState destination)
            && friendships.IsFriendly(source.Player, destination.Player);
    }
}
