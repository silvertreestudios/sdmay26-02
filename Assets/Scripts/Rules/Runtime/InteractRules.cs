using System;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Defines the rules-native one-action Interact used by combat doors.</summary>
    public static class InteractActionDefinition
    {
        /// <summary>Gets Interact's stable top-level action definition.</summary>
        public static ActionDefinitionId DefinitionId { get; } = new("interact");

        /// <summary>Gets Interact's immutable one-action manipulate profile.</summary>
        public static ActionProfile Profile { get; } =
            ActionProfile.OneAction(new[] { Trait.FromSlug("manipulate") });
    }

    /// <summary>Requests one rules-native Interact with no caller-supplied privilege.</summary>
    public sealed class InteractActionOp : ActionOp<InteractOutcome>
    {
        /// <summary>Creates an Interact for the exact acting creature.</summary>
        public InteractActionOp(CreatureId actor)
            : base(actor, InteractActionDefinition.DefinitionId) { }
    }

    /// <summary>Reports that Interact completed its authoritative action lifecycle.</summary>
    public readonly struct InteractOutcome
    {
        internal InteractOutcome(CreatureId actor) => Actor = actor;

        /// <summary>Gets the creature that paid for Interact.</summary>
        public CreatureId Actor { get; }
    }

    internal sealed class InteractActionHandler : IOpHandler<InteractActionOp, InteractOutcome>
    {
        public ValueTask<InteractOutcome> Handle(
            OpFrame<InteractActionOp> frame,
            OpHandlerContext context
        ) => new(new InteractOutcome(frame.Op.Actor));
    }

    internal sealed class InteractActionValidator : IActionValidator<InteractActionOp>
    {
        public ActionValidationResult Validate(
            OpFrame<InteractActionOp> frame,
            RulesSnapshot snapshot
        )
        {
            CreatureId actor = frame.Op.Actor;
            if (!snapshot.Creatures.Contains(actor))
                return ActionValidationResult.Invalid("The actor is not registered.");
            if (!snapshot.Health.IsAlive(actor))
                return ActionValidationResult.Invalid("The actor cannot act.");
            bool ownsTurn = snapshot.Encounters.Any(pair =>
                pair.Value.Phase == EncounterPhase.Active
                && pair.Value.CurrentTurn.HasValue
                && pair.Value.CurrentTurn.Value.Actor == actor
            );
            return ownsTurn
                ? ActionValidationResult.Valid
                : ActionValidationResult.Invalid("The actor does not own the active turn.");
        }
    }

    /// <summary>Composes the typed Interact action into a dispatcher.</summary>
    public static class InteractRuleDispatcherExtensions
    {
        /// <summary>Adds Interact's handler and pure validator.</summary>
        public static RuleDispatcherBuilder UseInteractRules(this RuleDispatcherBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            return builder
                .RegisterHandler<InteractActionOp, InteractOutcome>(new InteractActionHandler())
                .RegisterActionValidator(new InteractActionValidator());
        }
    }
}
