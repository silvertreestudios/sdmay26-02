using System;
using Game.Rules.Runtime;
using Game.Rules.Unity.Composition;

namespace Game.Rules.Unity.Door
{
    /// <summary>Installs Open Door's action lifecycle against one floor-lived door authority.</summary>
    internal sealed class UnityDoorEncounterModule : IUnityEncounterDispatcherModule
    {
        private readonly DoorWorldRuntime doorWorld;

        internal UnityDoorEncounterModule(DoorWorldRuntime doorWorld) =>
            this.doorWorld = doorWorld ?? throw new ArgumentNullException(nameof(doorWorld));

        /// <inheritdoc/>
        public void ConfigureDispatcher(RuleDispatcherBuilder builder) =>
            builder.UseDoorInteractRules(doorWorld);

        internal UnityEncounterExtension CreateExtension() =>
            new(this, new DoorInteractActionDefinition());
    }
}
