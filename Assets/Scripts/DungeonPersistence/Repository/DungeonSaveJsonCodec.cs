using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Game.DungeonPersistence.Repository
{
    /// <summary>
    /// Provides deterministic compact JSON and strict parsing for persistence contracts. Parsing
    /// rejects duplicate, missing, unknown, mistyped, semantically invalid, and incompatible data.
    /// </summary>
    internal static class DungeonSaveJsonCodec
    {
        private static readonly JsonSerializerSettings ActorSettings = new()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Culture = CultureInfo.InvariantCulture,
            MissingMemberHandling = MissingMemberHandling.Error,
            NullValueHandling = NullValueHandling.Include,
            TypeNameHandling = TypeNameHandling.None,
        };

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
        public static DungeonSaveResult<DungeonRunSave> ParseRun(string json) =>
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
        public static DungeonSaveResult<DungeonRunSaveManifest> ParseRunManifest(string json) =>
            Parse(json, DungeonSaveJsonDocument.ReadManifest, "manifest");

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
        public static DungeonSaveResult<DungeonFloorSaveState> ParseFloor(string json) =>
            Parse(json, DungeonSaveJsonDocument.ReadFloor, "floor");

        /// <summary>
        /// Serializes one creature as a versioned canonical state token for adapters and materializers.
        /// </summary>
        /// <param name="creature">The complete actor state.</param>
        /// <returns>Compact deterministic versioned creature JSON.</returns>
        internal static string SerializeCreature(DungeonCreatureSaveState creature)
        {
            if (creature == null)
                throw new ArgumentNullException(nameof(creature));
            return new JObject
            {
                ["documentVersion"] = DungeonSaveSchema.CreatureStateVersion,
                ["creature"] = JToken.FromObject(creature, JsonSerializer.Create(ActorSettings)),
            }.ToString(Formatting.None);
        }

        /// <summary>Strictly parses a versioned canonical creature state token.</summary>
        /// <param name="json">The complete creature token.</param>
        /// <returns>A complete actor state or one structured diagnostic.</returns>
        internal static DungeonSaveResult<DungeonCreatureSaveState> ParseCreature(string json) =>
            Parse(json, ReadCreatureEnvelope, "creature");

        private static DungeonCreatureSaveState ReadCreatureEnvelope(JObject source)
        {
            const string path = "creature";
            DungeonSaveJsonDocument.ValidateProperties(source, path, "documentVersion", "creature");
            DungeonSaveJsonDocument.RequireVersion(
                DungeonSaveJsonDocument.RequiredInt(source, "documentVersion", path),
                DungeonSaveSchema.CreatureStateVersion,
                path + ".documentVersion"
            );
            DungeonCreatureSaveState creature = DungeonSaveJsonDocument
                .RequiredObject(source, "creature", path)
                .ToObject<DungeonCreatureSaveState>(JsonSerializer.Create(ActorSettings));
            return creature ?? throw new JsonSerializationException("Creature state is required.");
        }

        private static DungeonSaveResult<T> Parse<T>(
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
                return DungeonSaveResult<T>.Success(read(root));
            }
            catch (DungeonSaveJsonIncompatibleException exception)
            {
                return DungeonSaveResult<T>.Failure(
                    new[]
                    {
                        new DungeonSaveDiagnostic(
                            DungeonSaveDiagnosticCode.IncompatibleVersion,
                            DungeonSaveDiagnosticSeverity.Error,
                            exception.Path,
                            exception.Message
                        ),
                    }
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
                return DungeonSaveResult<T>.Failure(
                    new[]
                    {
                        new DungeonSaveDiagnostic(
                            DungeonSaveDiagnosticCode.CorruptSave,
                            DungeonSaveDiagnosticSeverity.Error,
                            path,
                            "JSON is incomplete or invalid: " + exception.Message
                        ),
                    }
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
