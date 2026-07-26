using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Supplies the immutable preparation state needed to authorize Light.</summary>
    /// <remarks>
    /// Hosts extract this value from their character data at the Unity boundary. The rules runtime
    /// depends only on the resulting plain value and never reads scene objects or legacy spell state.
    /// </remarks>
    public interface ILightActorStateProvider
    {
        /// <summary>Gets the current Light preparation state for one rules creature.</summary>
        /// <param name="actor">The creature whose preparation is being checked.</param>
        /// <returns>The immutable Light-specific actor state.</returns>
        LightActorState Get(CreatureId actor);
    }

    /// <summary>Contains the complete actor-specific state used by the Light rules slice.</summary>
    public readonly struct LightActorState : IEquatable<LightActorState>
    {
        /// <summary>Creates Light preparation state for one actor.</summary>
        /// <param name="isPrepared">Whether the actor currently has Light prepared.</param>
        public LightActorState(bool isPrepared) => IsPrepared = isPrepared;

        /// <summary>Gets whether the actor currently has Light prepared.</summary>
        public bool IsPrepared { get; }

        /// <inheritdoc/>
        public bool Equals(LightActorState other) => IsPrepared == other.IsPrepared;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is LightActorState other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => IsPrepared.GetHashCode();

        /// <summary>Compares two Light actor states.</summary>
        public static bool operator ==(LightActorState left, LightActorState right) =>
            left.Equals(right);

        /// <summary>Compares two Light actor states.</summary>
        public static bool operator !=(LightActorState left, LightActorState right) =>
            !left.Equals(right);
    }

    /// <summary>Confirms that one self-only Light cast completed its rules lifecycle.</summary>
    public readonly struct LightCastOutcome : IEquatable<LightCastOutcome>
    {
        /// <summary>Creates a committed Light result.</summary>
        /// <param name="actor">The creature that cast Light on itself.</param>
        public LightCastOutcome(CreatureId actor)
        {
            if (actor.IsEmpty)
                throw new ArgumentException("A Light actor is required.", nameof(actor));
            Actor = actor;
        }

        /// <summary>Gets the creature that cast Light on itself.</summary>
        public CreatureId Actor { get; }

        /// <inheritdoc/>
        public bool Equals(LightCastOutcome other) => Actor == other.Actor;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is LightCastOutcome other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => Actor.GetHashCode();

        /// <summary>Compares two Light outcomes.</summary>
        public static bool operator ==(LightCastOutcome left, LightCastOutcome right) =>
            left.Equals(right);

        /// <summary>Compares two Light outcomes.</summary>
        public static bool operator !=(LightCastOutcome left, LightCastOutcome right) =>
            !left.Equals(right);
    }

    /// <summary>Defines the no-selection, self-only Light action and its immutable profile.</summary>
    public sealed class LightActionDefinition : IActionCatalog
    {
        private readonly ILightActorStateProvider actorStateProvider;
        private readonly ActionProfile profile;

        /// <summary>Gets Light's stable action-definition identity.</summary>
        public static ActionDefinitionId DefinitionId { get; } = new ActionDefinitionId("light");

        /// <summary>Creates Light from extracted actor state and definition-backed traits.</summary>
        /// <param name="actorStateProvider">The boundary supplying current preparation ownership.</param>
        /// <param name="traits">The exact traits extracted from the Light spell definition.</param>
        public LightActionDefinition(
            ILightActorStateProvider actorStateProvider,
            IEnumerable<Trait> traits
        )
        {
            this.actorStateProvider =
                actorStateProvider ?? throw new ArgumentNullException(nameof(actorStateProvider));
            profile = ActionProfile.Create(
                ActionCost.Two,
                traits ?? throw new ArgumentNullException(nameof(traits))
            );
        }

        /// <summary>Gets current Light availability for presentation.</summary>
        /// <param name="snapshot">The authoritative encounter snapshot.</param>
        /// <param name="actor">The creature considering Light.</param>
        /// <returns>A typed available or unavailable result.</returns>
        public ActionAvailability GetAvailability(RulesSnapshot snapshot, CreatureId actor) =>
            LightRules.GetAvailability(snapshot, actor, actorStateProvider.Get(actor));

        /// <inheritdoc/>
        public ActionProfile GetBaseProfile(ActionDefinitionId definitionId)
        {
            if (definitionId != DefinitionId)
                throw new KeyNotFoundException($"Unknown action definition '{definitionId}'.");
            return profile;
        }

        internal ActionValidationResult Validate(RulesSnapshot snapshot, CreatureId actor) =>
            LightRules.Validate(snapshot, actor, actorStateProvider.Get(actor));
    }

    /// <summary>Requests one trusted, no-selection Light cast by and upon the actor.</summary>
    public sealed class LightActionOp : ActionOp<LightCastOutcome>
    {
        /// <summary>Creates a Light action request.</summary>
        /// <param name="actor">The prepared creature casting Light on itself.</param>
        public LightActionOp(CreatureId actor)
            : base(actor, LightActionDefinition.DefinitionId) { }
    }

    /// <summary>Owns Light availability, validation, resolution, and dispatcher composition.</summary>
    public static class LightRules
    {
        /// <summary>Gets Light availability from authoritative actions and extracted preparation.</summary>
        /// <param name="snapshot">The authoritative encounter snapshot.</param>
        /// <param name="actor">The creature considering Light.</param>
        /// <param name="state">The actor's immutable Light preparation state.</param>
        /// <returns>A typed available or unavailable result.</returns>
        public static ActionAvailability GetAvailability(
            RulesSnapshot snapshot,
            CreatureId actor,
            LightActorState state
        )
        {
            ActionValidationResult validation = Validate(snapshot, actor, state);
            if (validation is ActionValidationResult.InvalidActionValidationResult invalid)
                return ActionAvailability.Unavailable(invalid.Reason);
            if (
                !snapshot.ActionEconomy.TryGet(actor, out ActionEconomyState economy)
                || economy.ActionsRemaining < ActionCost.Two.Amount
            )
            {
                return ActionAvailability.Unavailable(
                    "The actor does not have two actions available."
                );
            }
            return ActionAvailability.Available;
        }

        internal static ActionValidationResult Validate(
            RulesSnapshot snapshot,
            CreatureId actor,
            LightActorState state
        )
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (actor.IsEmpty || !snapshot.Creatures.Contains(actor))
                return ActionValidationResult.Invalid("The Light actor is not registered.");
            if (!state.IsPrepared)
                return ActionValidationResult.Invalid("The actor does not have Light prepared.");
            return ActionValidationResult.Valid;
        }

        /// <summary>Adds Light validation and resolution to a configured action runtime.</summary>
        /// <param name="builder">The dispatcher builder being composed.</param>
        /// <param name="definition">The shared Light definition and actor-state boundary.</param>
        /// <returns>The supplied builder for fluent composition.</returns>
        public static RuleDispatcherBuilder UseLightRules(
            this RuleDispatcherBuilder builder,
            LightActionDefinition definition
        )
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            return builder
                .RegisterHandler<LightActionOp, LightCastOutcome>(new LightActionHandler())
                .RegisterActionValidator(new LightActionValidator(definition));
        }
    }

    internal sealed class LightActionValidator : IActionValidator<LightActionOp>
    {
        private readonly LightActionDefinition definition;

        public LightActionValidator(LightActionDefinition definition) =>
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));

        public ActionValidationResult Validate(
            OpFrame<LightActionOp> frame,
            RulesSnapshot snapshot
        ) => definition.Validate(snapshot, frame.Op.Actor);
    }

    internal sealed class LightActionHandler : IOpHandler<LightActionOp, LightCastOutcome>
    {
        public ValueTask<LightCastOutcome> Handle(
            OpFrame<LightActionOp> frame,
            OpHandlerContext context
        ) => new ValueTask<LightCastOutcome>(new LightCastOutcome(frame.Op.Actor));
    }
}
