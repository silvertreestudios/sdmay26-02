using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.DungeonPersistence.Repository
{
    /// <summary>
    /// Represents strict JSON parsing success or failure without exposing a nullable partial value.
    /// </summary>
    /// <typeparam name="T">The immutable persistence contract being parsed.</typeparam>
    public abstract class DungeonSaveParseResult<T>
    {
        private protected DungeonSaveParseResult(
            bool isSuccess,
            IEnumerable<DungeonSaveDiagnostic> diagnostics
        )
        {
            IsSuccess = isSuccess;
            Diagnostics = Array.AsReadOnly(
                (diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))).ToArray()
            );
        }

        /// <summary>Gets whether a complete validated contract is available.</summary>
        public bool IsSuccess { get; }

        /// <summary>Gets strict schema, compatibility, or semantic diagnostics.</summary>
        public IReadOnlyList<DungeonSaveDiagnostic> Diagnostics { get; }
    }

    /// <summary>Represents a complete immutable value parsed from strict persistence JSON.</summary>
    /// <typeparam name="T">The immutable persistence contract type.</typeparam>
    public sealed class DungeonSaveParseSuccess<T> : DungeonSaveParseResult<T>
    {
        internal DungeonSaveParseSuccess(T value)
            : base(true, Array.Empty<DungeonSaveDiagnostic>())
        {
            Value = value;
        }

        /// <summary>Gets the complete parsed value.</summary>
        public T Value { get; }
    }

    /// <summary>Represents strict JSON failure without a partial contract value.</summary>
    /// <typeparam name="T">The immutable persistence contract type.</typeparam>
    public sealed class DungeonSaveParseFailure<T> : DungeonSaveParseResult<T>
    {
        internal DungeonSaveParseFailure(DungeonSaveDiagnostic diagnostic)
            : base(false, new[] { diagnostic }) { }
    }

    /// <summary>
    /// Provides deterministic compact JSON and strict parsing for persistence contracts. Parsing
    /// rejects duplicate, missing, unknown, mistyped, semantically invalid, and incompatible data.
    /// </summary>
    public static class DungeonSaveJsonCodec
    {
        /// <summary>Serializes a complete run as a deterministic transport document.</summary>
        /// <param name="save">The complete run manifest and indexed floor documents.</param>
        /// <returns>Compact JSON with stable property, depth, path, and stable-ID ordering.</returns>
        public static string SerializeRun(DungeonRunSave save)
        {
            if (save == null)
                throw new ArgumentNullException(nameof(save));
            DungeonRunSaveValidator.RequireValid(save);
            return DungeonSaveJsonDocument.FromRun(save).ToString(Formatting.None);
        }

        /// <summary>Strictly parses a complete deterministic run transport document.</summary>
        /// <param name="json">The complete JSON source.</param>
        /// <returns>A complete run or one structured diagnostic without partial state.</returns>
        public static DungeonSaveParseResult<DungeonRunSave> ParseRun(string json) =>
            Parse(json, DungeonSaveJsonDocument.ReadRun, "run");

        /// <summary>Serializes one run manifest deterministically.</summary>
        /// <param name="manifest">The manifest to serialize.</param>
        /// <returns>Compact deterministic manifest JSON.</returns>
        public static string SerializeRunManifest(DungeonRunSaveManifest manifest)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));
            return DungeonSaveJsonDocument.FromManifest(manifest).ToString(Formatting.None);
        }

        /// <summary>Strictly parses one run manifest.</summary>
        /// <param name="json">The complete manifest JSON source.</param>
        /// <returns>A complete manifest or one structured diagnostic.</returns>
        public static DungeonSaveParseResult<DungeonRunSaveManifest> ParseRunManifest(
            string json
        ) => Parse(json, DungeonSaveJsonDocument.ReadManifest, "manifest");

        /// <summary>Serializes one per-depth state document deterministically.</summary>
        /// <param name="floor">The floor state to serialize.</param>
        /// <returns>Compact deterministic floor JSON.</returns>
        public static string SerializeFloor(DungeonFloorSaveState floor)
        {
            if (floor == null)
                throw new ArgumentNullException(nameof(floor));
            return DungeonSaveJsonDocument.FromFloor(floor).ToString(Formatting.None);
        }

        /// <summary>Strictly parses one per-depth state document.</summary>
        /// <param name="json">The complete floor JSON source.</param>
        /// <returns>A complete floor or one structured diagnostic.</returns>
        public static DungeonSaveParseResult<DungeonFloorSaveState> ParseFloor(string json) =>
            Parse(json, DungeonSaveJsonDocument.ReadFloor, "floor");

        /// <summary>
        /// Serializes one creature as a versioned canonical state token for adapters and materializers.
        /// </summary>
        /// <param name="creature">The complete actor state.</param>
        /// <returns>Compact deterministic versioned creature JSON.</returns>
        public static string SerializeCreature(DungeonCreatureSaveState creature)
        {
            if (creature == null)
                throw new ArgumentNullException(nameof(creature));
            return DungeonSaveJsonDocument.FromCreatureEnvelope(creature).ToString(Formatting.None);
        }

        /// <summary>Strictly parses a versioned canonical creature state token.</summary>
        /// <param name="json">The complete creature token.</param>
        /// <returns>A complete actor state or one structured diagnostic.</returns>
        public static DungeonSaveParseResult<DungeonCreatureSaveState> ParseCreature(string json) =>
            Parse(json, DungeonSaveJsonDocument.ReadCreatureEnvelope, "creature");

        private static DungeonSaveParseResult<T> Parse<T>(
            string json,
            Func<JObject, T> read,
            string path
        )
        {
            try
            {
                JObject root = JObject.Parse(
                    json ?? string.Empty,
                    new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        LineInfoHandling = LineInfoHandling.Ignore,
                    }
                );
                return new DungeonSaveParseSuccess<T>(read(root));
            }
            catch (DungeonSaveJsonIncompatibleException exception)
            {
                return new DungeonSaveParseFailure<T>(
                    new DungeonSaveDiagnostic(
                        DungeonSaveDiagnosticCode.IncompatibleVersion,
                        DungeonSaveDiagnosticSeverity.Error,
                        exception.Path,
                        exception.Message
                    )
                );
            }
            catch (Exception exception)
                when (exception is JsonException
                    || exception is ArgumentException
                    || exception is InvalidOperationException
                    || exception is FormatException
                    || exception is OverflowException
                )
            {
                return new DungeonSaveParseFailure<T>(
                    new DungeonSaveDiagnostic(
                        DungeonSaveDiagnosticCode.CorruptSave,
                        DungeonSaveDiagnosticSeverity.Error,
                        path,
                        "JSON is incomplete or invalid: " + exception.Message
                    )
                );
            }
        }
    }

    internal sealed class DungeonSaveJsonIncompatibleException : Exception
    {
        internal DungeonSaveJsonIncompatibleException(string path, string message)
            : base(message)
        {
            Path = path;
        }

        internal string Path { get; }
    }
}
