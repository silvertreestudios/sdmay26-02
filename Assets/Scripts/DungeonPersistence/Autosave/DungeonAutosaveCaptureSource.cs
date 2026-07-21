using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Encounters;
using Game.DungeonGeneration;
using Game.DungeonPersistence.Floors;
using Game.DungeonPersistence.Repository;

namespace Game.DungeonPersistence.Autosave
{
    /// <summary>
    /// Supplies exact current-floor captures while allowing the scheduler to observe actor stability.
    /// </summary>
    /// <remarks>
    /// Production uses <see cref="DungeonRuntimeAutosaveCaptureSource"/>. The interface also keeps
    /// tests and non-Unity composition roots isolated from scene objects and filesystem locations.
    /// </remarks>
    public interface IDungeonAutosaveCaptureSource
    {
        /// <summary>Raised only after a durable generated-floor mutation has completed.</summary>
        event Action<DungeonPersistentStateChangeKind> PersistentStateChanged;

        /// <summary>Gets the generated depth owned by this runtime.</summary>
        int Depth { get; }

        /// <summary>Gets whether no party or materialized encounter actor is applying an action.</summary>
        bool AreActorsStable { get; }

        /// <summary>Captures a generated depth that has never committed actor state.</summary>
        /// <returns>The exact party and floor transaction input.</returns>
        DungeonCurrentFloorCapture CaptureNew();

        /// <summary>Recaptures a generated depth while retaining defeated actor records.</summary>
        /// <param name="previousFloor">The last atomically committed state for this depth.</param>
        /// <returns>The exact party and replacement floor transaction input.</returns>
        DungeonCurrentFloorCapture CaptureExisting(DungeonFloorSaveState previousFloor);
    }

    /// <summary>Adapts one initialized generated-floor runtime to the autosave scheduler.</summary>
    public sealed class DungeonRuntimeAutosaveCaptureSource : IDungeonAutosaveCaptureSource
    {
        private readonly DungeonEncounterRuntimeController runtime;
        private readonly string staticFloorJson;

        /// <summary>Creates a capture source for one initialized runtime and immutable topology.</summary>
        /// <param name="staticFloorJson">Validated pristine generator JSON without runtime state.</param>
        /// <param name="runtime">The initialized party, door, and encounter runtime.</param>
        public DungeonRuntimeAutosaveCaptureSource(
            string staticFloorJson,
            DungeonEncounterRuntimeController runtime
        )
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            if (!runtime.IsInitialized)
                throw new ArgumentException(
                    "The dungeon runtime must be initialized before autosave composition.",
                    nameof(runtime)
                );

            DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(staticFloorJson);
            if (!parsed.IsSuccess)
            {
                throw new ArgumentException(
                    "Static floor JSON is invalid: "
                        + string.Join(" ", parsed.Diagnostics.Select(item => item.Message)),
                    nameof(staticFloorJson)
                );
            }
            if (parsed.Document.RuntimeState != null)
                throw new ArgumentException(
                    "The autosave capture source requires pristine static floor JSON.",
                    nameof(staticFloorJson)
                );

            this.staticFloorJson = DungeonLevelJsonSerializer.Serialize(parsed.Document);
            Depth = parsed.Document.Generation.Depth;
        }

        /// <inheritdoc/>
        public event Action<DungeonPersistentStateChangeKind> PersistentStateChanged
        {
            add => runtime.PersistentStateChanged += value;
            remove => runtime.PersistentStateChanged -= value;
        }

        /// <inheritdoc/>
        public int Depth { get; }

        /// <inheritdoc/>
        public bool AreActorsStable
        {
            get
            {
                if (!runtime.IsInitialized)
                    return false;
                IReadOnlyList<ActionController> party = runtime.CapturePartyControllers();
                if (party.Any(controller => controller == null))
                    throw new InvalidOperationException(
                        "The configured dungeon party lost a materialized actor."
                    );
                return !party.Any(controller => controller.IsTakingAction)
                    && !runtime
                        .CaptureMaterializedCreatures()
                        .Any(actor => actor.Controller != null && actor.Controller.IsTakingAction);
            }
        }

        /// <inheritdoc/>
        public DungeonCurrentFloorCapture CaptureNew() =>
            DungeonCurrentFloorCaptureService.CaptureNew(staticFloorJson, runtime);

        /// <inheritdoc/>
        public DungeonCurrentFloorCapture CaptureExisting(DungeonFloorSaveState previousFloor) =>
            DungeonCurrentFloorCaptureService.CaptureExisting(
                staticFloorJson,
                runtime,
                previousFloor
            );
    }
}
