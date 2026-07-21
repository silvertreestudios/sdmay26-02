using System;
using System.Collections.Generic;
using System.Linq;
using Game.DungeonPersistence.Floors;
using Game.DungeonPersistence.Repository;

namespace Game.DungeonPersistence
{
    /// <summary>
    /// Owns the last committed multi-floor run and publishes current-floor replacements as one
    /// atomic repository transaction.
    /// </summary>
    /// <remarks>
    /// Candidate state becomes visible to this session only after the repository reports success.
    /// A failed capture or write therefore preserves both the on-disk save and the in-memory view
    /// of the last committed generation.
    /// </remarks>
    public sealed class DungeonRunAutosaveSession
    {
        private readonly object sync = new();
        private readonly IDungeonSaveRepository repository;
        private readonly int startingSeed;
        private readonly string generatorVersion;
        private SortedDictionary<int, DungeonFloorSaveState> committedFloors;
        private CommitState commitState;

        private DungeonRunAutosaveSession(
            int startingSeed,
            string generatorVersion,
            IDungeonSaveRepository repository,
            IEnumerable<DungeonFloorSaveState> committedFloors,
            CommitState commitState
        )
        {
            if (string.IsNullOrWhiteSpace(generatorVersion))
                throw new ArgumentException(
                    "A stable generator version is required.",
                    nameof(generatorVersion)
                );
            this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
            this.startingSeed = startingSeed;
            this.generatorVersion = generatorVersion;
            if (committedFloors == null)
                throw new ArgumentNullException(nameof(committedFloors));
            this.committedFloors = new SortedDictionary<int, DungeonFloorSaveState>(
                committedFloors.ToDictionary(floor => floor.Depth)
            );
            this.commitState = commitState ?? throw new ArgumentNullException(nameof(commitState));
        }

        /// <summary>Creates an empty session for a new run before its first floor is generated.</summary>
        /// <param name="startingSeed">The run seed supplied to deterministic generation.</param>
        /// <param name="generatorVersion">The stable generator algorithm/version identifier.</param>
        /// <param name="repository">The explicit atomic autosave repository.</param>
        /// <returns>A session that can publish its first generated floor.</returns>
        public static DungeonRunAutosaveSession CreateNew(
            int startingSeed,
            string generatorVersion,
            IDungeonSaveRepository repository
        ) =>
            new(
                startingSeed,
                generatorVersion,
                repository,
                Array.Empty<DungeonFloorSaveState>(),
                EmptyCommitState.Instance
            );

        /// <summary>Creates a session from one completely validated repository load.</summary>
        /// <param name="save">The complete run returned by a successful repository load.</param>
        /// <param name="repository">The repository that will receive later generations.</param>
        /// <returns>A session retaining every generated floor.</returns>
        public static DungeonRunAutosaveSession Restore(
            DungeonRunSave save,
            IDungeonSaveRepository repository
        )
        {
            if (save == null)
                throw new ArgumentNullException(nameof(save));
            return new DungeonRunAutosaveSession(
                save.Manifest.StartingSeed,
                save.Manifest.GeneratorVersion,
                repository,
                save.Floors,
                new CommittedCommitState(save)
            );
        }

        /// <summary>Gets whether at least one complete run generation has committed.</summary>
        public bool HasCommittedSave
        {
            get
            {
                lock (sync)
                    return commitState is CommittedCommitState;
            }
        }

        /// <summary>Gets the most recently committed complete run.</summary>
        /// <exception cref="InvalidOperationException">No floor has committed yet.</exception>
        public DungeonRunSave CommittedSave
        {
            get
            {
                lock (sync)
                {
                    if (commitState is not CommittedCommitState committed)
                        throw new InvalidOperationException(
                            "A new dungeon run has not committed its first generated floor."
                        );
                    return committed.Save;
                }
            }
        }

        /// <summary>
        /// Atomically publishes a newly generated or mutated current floor together with party
        /// state and every previously generated depth.
        /// </summary>
        /// <param name="capture">Party and floor state captured at one stable runtime boundary.</param>
        /// <returns>The repository's structured write result.</returns>
        public DungeonSaveWriteResult CommitCurrentFloor(DungeonCurrentFloorCapture capture)
        {
            if (capture == null)
                throw new ArgumentNullException(nameof(capture));

            lock (sync)
            {
                SortedDictionary<int, DungeonFloorSaveState> candidateFloors = new(committedFloors)
                {
                    [capture.Floor.Depth] = capture.Floor,
                };
                DungeonRunSaveManifest manifest = new(
                    DungeonSaveSchema.RunManifestVersion,
                    startingSeed,
                    generatorVersion,
                    capture.Floor.Depth,
                    capture.Party,
                    candidateFloors.Keys.Select(DungeonFloorSaveReference.Current)
                );
                DungeonRunSave candidate = new(manifest, candidateFloors.Values);
                DungeonSaveWriteResult result = repository.Save(candidate);
                if (!result.IsSuccess)
                    return result;

                committedFloors = candidateFloors;
                commitState = new CommittedCommitState(candidate);
                return result;
            }
        }

        /// <summary>Gets a previously committed generated depth for backtracking or recapture.</summary>
        /// <param name="depth">The nonnegative generated depth.</param>
        /// <returns>The exact committed floor state.</returns>
        /// <exception cref="KeyNotFoundException">The depth has never committed.</exception>
        public DungeonFloorSaveState RequireFloor(int depth)
        {
            lock (sync)
                return committedFloors[depth];
        }

        private abstract class CommitState { }

        private sealed class EmptyCommitState : CommitState
        {
            internal static readonly EmptyCommitState Instance = new();
        }

        private sealed class CommittedCommitState : CommitState
        {
            internal CommittedCommitState(DungeonRunSave save)
            {
                Save = save ?? throw new ArgumentNullException(nameof(save));
            }

            internal DungeonRunSave Save { get; }
        }
    }
}
