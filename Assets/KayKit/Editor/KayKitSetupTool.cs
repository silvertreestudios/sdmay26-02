using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.KayKit;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.KayKit.Editor
{
    public static class KayKitSetupTool
    {
        public const string ProjectRoot = "Assets/KayKit";
        public const string CatalogRoot = ProjectRoot + "/Catalogs";
        public const string MaterialRoot = ProjectRoot + "/Materials";
        public const string PrefabRoot = ProjectRoot + "/Prefabs";
        public const string DungeonCatalogPath = CatalogRoot + "/KayKitDungeonCatalog.asset";
        public const string AnimationLibraryPath = CatalogRoot + "/KayKitAnimationLibrary.asset";
        public const string SourceManifestPath = CatalogRoot + "/KayKitSourceManifest.asset";

        private const string DownloadDate = "2026-07-14";
        private const string LicenseName = "CC0-1.0";

        private static readonly IReadOnlyDictionary<string, string> SourceAtlasByModel =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "arrow_bow", "ranger_texture" }
            };

        private static readonly PackDescriptor[] Packs =
        {
            new(
                KayKitPathUtility.DungeonRoot,
                "Dungeon Remastered",
                "1.1",
                "https://kaylousberg.itch.io/kaykit-dungeon-remastered",
                211,
                1),
            new(
                KayKitPathUtility.AdventurersRoot,
                "Adventurers",
                "2.0",
                "https://kaylousberg.itch.io/kaykit-adventurers",
                37,
                5),
            new(
                KayKitPathUtility.SkeletonsRoot,
                "Skeletons",
                "1.1",
                "https://kaylousberg.itch.io/kaykit-skeletons",
                17,
                1),
            new(
                KayKitPathUtility.AnimationsRoot,
                "Character Animations",
                "1.1",
                "https://kaylousberg.itch.io/kaykit-character-animations",
                8,
                0)
        };

        [MenuItem("Tools/KayKit/Reimport Vendor Tree")]
        public static void ReimportVendorTree()
        {
            AssetDatabase.ImportAsset(
                KayKitPathUtility.VendorRoot,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
        }

        [MenuItem("Tools/KayKit/Regenerate Project Assets")]
        public static void RegenerateProjectAssets()
        {
            try
            {
                EnsureProjectFolders();
                Dictionary<string, Material[]> materials = GenerateMaterials();
                GenerateDungeonCatalog();
                GenerateAnimationLibrary();
                GenerateSourceManifest();
                GenerateRepresentativePrefabs(materials);
                AssetDatabase.SaveAssets();

                KayKitValidationReport report = Validate();
                if (!report.IsValid)
                    throw new InvalidOperationException(report.Format());

                Debug.Log($"KayKit setup complete. {report.Format()}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"KayKit setup failed: {exception.Message}");
                throw;
            }
        }

        [MenuItem("Tools/KayKit/Validate Setup")]
        public static void ValidateFromMenu()
        {
            KayKitValidationReport report = Validate();
            if (report.IsValid)
                Debug.Log(report.Format());
            else
                Debug.LogError(report.Format());
        }

        public static KayKitValidationReport Validate()
        {
            List<string> errors = new();
            List<string> warnings = new();

            foreach (PackDescriptor pack in Packs)
            {
                if (!AssetDatabase.IsValidFolder(pack.Root))
                {
                    errors.Add($"Missing vendor pack folder: {pack.Root}");
                    continue;
                }

                string[] packFiles = AssetDatabase.FindAssets(string.Empty, new[] { pack.Root })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(File.Exists)
                    .ToArray();
                int fbxCount = packFiles.Count(path =>
                    string.Equals(Path.GetExtension(path), ".fbx", StringComparison.OrdinalIgnoreCase));
                int pngCount = packFiles.Count(path =>
                    string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase));
                if (fbxCount != pack.ExpectedFbxCount || pngCount != pack.ExpectedPngCount)
                {
                    errors.Add(
                        $"{pack.Name} inventory is {fbxCount} FBX/{pngCount} PNG; " +
                        $"expected {pack.ExpectedFbxCount} FBX/{pack.ExpectedPngCount} PNG.");
                }

                string licensePath = pack.Root + "/License.txt";
                if (!File.Exists(licensePath))
                    errors.Add($"Missing retained vendor license: {licensePath}");
            }

            string[] vendorFiles = GetVendorFilePaths();
            string[] forbiddenExtensions =
            {
                ".gltf", ".glb", ".obj", ".bin", ".blend", ".zip", ".rar",
                ".7z", ".url", ".unity", ".unitypackage"
            };
            foreach (string path in vendorFiles.Where(path =>
                         forbiddenExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)))
            {
                errors.Add($"Excluded file type is present: {path}");
            }

            string[] animationSources = GetFbxPaths(KayKitPathUtility.AnimationsRoot);
            string[] categories = animationSources
                .Select(KayKitPathUtility.GetAnimationCategory)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            string[] expectedCategories =
            {
                "General", "MovementBasic", "MovementAdvanced", "CombatMelee",
                "CombatRanged", "Simulation", "Special", "Tools"
            };
            foreach (string category in expectedCategories)
            {
                if (!categories.Contains(category, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"Missing Rig_Medium animation set: {category}");
            }
            if (animationSources.Length != expectedCategories.Length)
                errors.Add($"Expected 8 Rig_Medium animation FBXs, found {animationSources.Length}.");

            KayKitDungeonCatalog dungeonCatalog =
                AssetDatabase.LoadAssetAtPath<KayKitDungeonCatalog>(DungeonCatalogPath);
            ValidateDungeonCatalog(dungeonCatalog, errors);

            KayKitAnimationLibrary animationLibrary =
                AssetDatabase.LoadAssetAtPath<KayKitAnimationLibrary>(AnimationLibraryPath);
            ValidateAnimationLibrary(animationLibrary, errors);

            KayKitSourceManifest manifest =
                AssetDatabase.LoadAssetAtPath<KayKitSourceManifest>(SourceManifestPath);
            ValidateManifest(manifest, vendorFiles, errors);

            ValidateImporters(vendorFiles, errors);

            return new KayKitValidationReport(errors, warnings);
        }

        private static Dictionary<string, Material[]> GenerateMaterials()
        {
            Dictionary<string, Material[]> materials = new(StringComparer.OrdinalIgnoreCase);
            GenerateMaterialsForPack(KayKitPathUtility.DungeonRoot, "KayKitDungeon", materials);
            GenerateMaterialsForPack(KayKitPathUtility.AdventurersRoot, "KayKitAdventurers", materials);
            GenerateMaterialsForPack(KayKitPathUtility.SkeletonsRoot, "KayKitSkeletons", materials);
            return materials;
        }

        private static void GenerateMaterialsForPack(
            string packRoot,
            string materialPrefix,
            IDictionary<string, Material[]> output)
        {
            string[] texturePaths = AssetDatabase.FindAssets("t:Texture2D", new[] { packRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (texturePaths.Length == 0)
                throw new InvalidOperationException($"No PNG atlas found below {packRoot}.");

            List<Material> materials = new();
            foreach (string texturePath in texturePaths)
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                string materialName = $"{materialPrefix}_{Path.GetFileNameWithoutExtension(texturePath)}";
                string materialPath = $"{MaterialRoot}/{materialName}.mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    Shader shader = Shader.Find("Standard");
                    if (shader == null)
                        throw new InvalidOperationException("Built-in Render Pipeline Standard shader was not found.");
                    material = new Material(shader) { name = materialName };
                    AssetDatabase.CreateAsset(material, materialPath);
                }

                if (material.mainTexture != texture)
                {
                    material.mainTexture = texture;
                    EditorUtility.SetDirty(material);
                }
                materials.Add(material);
            }

            output[packRoot] = materials.ToArray();
        }

        private static void GenerateDungeonCatalog()
        {
            List<KayKitDungeonCatalogEntry> entries = new();
            HashSet<string> ids = new(StringComparer.Ordinal);
            foreach (string path in GetFbxPaths(KayKitPathUtility.DungeonRoot))
            {
                string id = KayKitPathUtility.GetDungeonId(path);
                if (!ids.Add(id))
                    throw new InvalidOperationException($"Duplicate Dungeon catalog ID: {id}");

                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null)
                    throw new InvalidOperationException($"Dungeon model could not be loaded: {path}");
                entries.Add(new KayKitDungeonCatalogEntry(id, model));
            }

            KayKitDungeonCatalog catalog = GetOrCreate<KayKitDungeonCatalog>(DungeonCatalogPath);
            catalog.ReplaceEntries(entries.OrderBy(entry => entry.Id, StringComparer.Ordinal));
            EditorUtility.SetDirty(catalog);
        }

        private static void GenerateAnimationLibrary()
        {
            List<KayKitAnimationEntry> entries = new();
            List<string> ambiguous = new();
            HashSet<string> ids = new(StringComparer.Ordinal);

            foreach (string path in GetFbxPaths(KayKitPathUtility.AnimationsRoot))
            {
                string category = KayKitPathUtility.GetAnimationCategory(path);
                AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<AnimationClip>()
                    .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(clip => clip.name, StringComparer.Ordinal)
                    .ToArray();
                if (clips.Length == 0)
                    throw new InvalidOperationException($"No AnimationClips found in {path}.");

                foreach (AnimationClip clip in clips)
                {
                    KayKitClipSemantics semantics = KayKitPathUtility.ClassifyClip(clip.name);
                    if (semantics == KayKitClipSemantics.SetupPose)
                        continue;
                    if (semantics == KayKitClipSemantics.Ambiguous)
                    {
                        ambiguous.Add($"{category}/{clip.name}");
                        continue;
                    }

                    string id = KayKitPathUtility.GetAnimationId(path, clip.name);
                    if (!ids.Add(id))
                        throw new InvalidOperationException($"Duplicate animation clip ID: {id}");
                    entries.Add(new KayKitAnimationEntry(
                        id,
                        category,
                        clip,
                        semantics == KayKitClipSemantics.Loop,
                        clip.length));
                }
            }

            if (ambiguous.Count > 0)
            {
                throw new InvalidOperationException(
                    "Ambiguous animation semantics; add an explicit classification before generating: " +
                    string.Join(", ", ambiguous.OrderBy(value => value, StringComparer.Ordinal)));
            }

            KayKitAnimationLibrary library = GetOrCreate<KayKitAnimationLibrary>(AnimationLibraryPath);
            library.ReplaceEntries(entries.OrderBy(entry => entry.Id, StringComparer.Ordinal));
            EditorUtility.SetDirty(library);
        }

        private static void GenerateSourceManifest()
        {
            List<KayKitSourceManifestEntry> entries = new();
            HashSet<string> ids = new(StringComparer.Ordinal);
            foreach (string path in GetVendorFilePaths())
            {
                PackDescriptor pack = Packs.Single(candidate =>
                    path.StartsWith(candidate.Root + "/", StringComparison.OrdinalIgnoreCase));
                string relativePath = KayKitPathUtility.GetRelativePath(path, pack.Root);
                string stableId = KayKitPathUtility.GetStableAssetId(path);
                if (!ids.Add(stableId))
                    throw new InvalidOperationException($"Duplicate source-manifest ID: {stableId}");

                Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset == null)
                    throw new InvalidOperationException($"Vendor asset could not be loaded: {path}");
                entries.Add(new KayKitSourceManifestEntry(
                    pack.SourceUrl,
                    pack.Name,
                    pack.Version,
                    DownloadDate,
                    LicenseName,
                    relativePath,
                    stableId,
                    asset));
            }

            KayKitSourceManifest manifest = GetOrCreate<KayKitSourceManifest>(SourceManifestPath);
            manifest.ReplaceEntries(entries.OrderBy(entry => entry.StableId, StringComparer.Ordinal));
            EditorUtility.SetDirty(manifest);
        }

        private static void GenerateRepresentativePrefabs(
            IReadOnlyDictionary<string, Material[]> materials)
        {
            string adventurer = GetFbxPaths(KayKitPathUtility.AdventurersRoot)
                .FirstOrDefault(KayKitPathUtility.IsCharacterModel);
            string skeleton = GetFbxPaths(KayKitPathUtility.SkeletonsRoot)
                .FirstOrDefault(KayKitPathUtility.IsCharacterModel);
            string accessory = GetFbxPaths(KayKitPathUtility.AdventurersRoot)
                .FirstOrDefault(path => !KayKitPathUtility.IsCharacterModel(path));
            string[] dungeonModels = GetFbxPaths(KayKitPathUtility.DungeonRoot);
            string dungeonFloor = dungeonModels.FirstOrDefault(path =>
                string.Equals(
                    Path.GetFileNameWithoutExtension(path),
                    "floor_tile_large",
                    StringComparison.OrdinalIgnoreCase));
            string dungeonWall = dungeonModels.FirstOrDefault(path =>
                string.Equals(
                    Path.GetFileNameWithoutExtension(path),
                    "wall",
                    StringComparison.OrdinalIgnoreCase));
            string dungeonProp = dungeonModels.FirstOrDefault(path =>
                string.Equals(
                    Path.GetFileNameWithoutExtension(path),
                    "table_medium",
                    StringComparison.OrdinalIgnoreCase));

            CreateWrapper(
                "RepresentativeAdventurer",
                adventurer,
                SelectMaterial(materials[KayKitPathUtility.AdventurersRoot], adventurer),
                true);
            CreateWrapper(
                "RepresentativeSkeleton",
                skeleton,
                SelectMaterial(materials[KayKitPathUtility.SkeletonsRoot], skeleton),
                true);
            CreateWrapper(
                "RepresentativeAccessory",
                accessory,
                SelectMaterial(materials[KayKitPathUtility.AdventurersRoot], accessory),
                false);
            CreateWrapper(
                "RepresentativeDungeonPiece",
                dungeonFloor,
                SelectMaterial(materials[KayKitPathUtility.DungeonRoot], dungeonFloor),
                false);
            CreateWrapper(
                "RepresentativeDungeonWall",
                dungeonWall,
                SelectMaterial(materials[KayKitPathUtility.DungeonRoot], dungeonWall),
                false);
            CreateWrapper(
                "RepresentativeDungeonProp",
                dungeonProp,
                SelectMaterial(materials[KayKitPathUtility.DungeonRoot], dungeonProp),
                false);
        }

        private static Material SelectMaterial(IReadOnlyList<Material> materials, string modelPath)
        {
            if (materials.Count == 0)
                throw new InvalidOperationException($"No project materials are available for {modelPath}.");
            if (materials.Count == 1)
                return materials[0];

            string modelName = Path.GetFileNameWithoutExtension(modelPath);
            SourceAtlasByModel.TryGetValue(modelName, out string sourceAtlasName);
            Material match = materials.FirstOrDefault(material =>
            {
                string textureName = material.mainTexture == null
                    ? string.Empty
                    : material.mainTexture.name;
                if (!string.IsNullOrEmpty(sourceAtlasName))
                    return string.Equals(textureName, sourceAtlasName, StringComparison.OrdinalIgnoreCase);

                const string suffix = "_texture";
                string key = textureName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                    ? textureName.Substring(0, textureName.Length - suffix.Length)
                    : textureName;
                return modelName.StartsWith(key, StringComparison.OrdinalIgnoreCase);
            });
            if (match == null)
            {
                throw new InvalidOperationException(
                    $"Could not determine the source texture atlas for model: {modelPath}");
            }

            return match;
        }

        private static void CreateWrapper(
            string name,
            string modelPath,
            Material material,
            bool ensureAnimator)
        {
            if (string.IsNullOrEmpty(modelPath))
                throw new InvalidOperationException($"Could not select a model for {name}.");

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            GameObject root = new(name);
            try
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.name = "Model";
                instance.transform.SetParent(root.transform, false);

                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] replacements = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                    renderer.sharedMaterials = replacements;
                }

                Animator[] animators = root.GetComponentsInChildren<Animator>(true);
                if (ensureAnimator && animators.Length == 0)
                    animators = new[] { root.AddComponent<Animator>() };
                foreach (Animator animator in animators)
                    animator.applyRootMotion = false;

                PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabRoot}/{name}.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void ValidateDungeonCatalog(KayKitDungeonCatalog catalog, ICollection<string> errors)
        {
            if (catalog == null)
            {
                errors.Add($"Missing generated catalog: {DungeonCatalogPath}");
                return;
            }

            int expected = GetFbxPaths(KayKitPathUtility.DungeonRoot).Length;
            if (catalog.Entries.Count != expected)
                errors.Add($"Dungeon catalog has {catalog.Entries.Count} entries; expected {expected}.");
            ValidateUniqueReferences(
                catalog.Entries.Select(entry => (entry.Id, (Object)entry.Model)),
                "Dungeon catalog",
                errors);
        }

        private static void ValidateAnimationLibrary(
            KayKitAnimationLibrary library,
            ICollection<string> errors)
        {
            if (library == null)
            {
                errors.Add($"Missing generated library: {AnimationLibraryPath}");
                return;
            }

            ValidateUniqueReferences(
                library.Entries.Select(entry => (entry.Id, (Object)entry.Clip)),
                "Animation library",
                errors);
            foreach (KayKitAnimationEntry entry in library.Entries)
            {
                if (entry.Clip == null)
                    continue;

                KayKitClipSemantics semantics = KayKitPathUtility.ClassifyClip(entry.Clip.name);
                bool shouldLoop = semantics == KayKitClipSemantics.Loop;
                if (semantics == KayKitClipSemantics.SetupPose)
                    errors.Add($"Setup T-pose is playable in the animation library: {entry.Id}");
                if (entry.Loop != shouldLoop)
                    errors.Add($"Animation loop metadata is stale for {entry.Id}.");
                if (entry.Clip.isLooping != shouldLoop)
                    errors.Add($"Imported animation loop setting is incorrect for {entry.Id}.");
                if (!Mathf.Approximately(entry.Duration, entry.Clip.length))
                    errors.Add($"Animation duration is stale for {entry.Id}.");
            }
        }

        private static void ValidateManifest(
            KayKitSourceManifest manifest,
            IReadOnlyCollection<string> vendorFiles,
            ICollection<string> errors)
        {
            if (manifest == null)
            {
                errors.Add($"Missing generated manifest: {SourceManifestPath}");
                return;
            }

            if (manifest.Entries.Count != vendorFiles.Count)
                errors.Add($"Source manifest has {manifest.Entries.Count} entries; expected {vendorFiles.Count}.");
            ValidateUniqueReferences(
                manifest.Entries.Select(entry => (entry.StableId, entry.Asset)),
                "Source manifest",
                errors);
            foreach (KayKitSourceManifestEntry entry in manifest.Entries)
            {
                if (entry.License != LicenseName || string.IsNullOrWhiteSpace(entry.SourceUrl))
                    errors.Add($"Incomplete provenance for {entry.StableId}.");
            }
        }

        private static void ValidateImporters(IEnumerable<string> vendorFiles, ICollection<string> errors)
        {
            foreach (string path in vendorFiles)
            {
                if (string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                {
                    TextureImporter texture = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (texture == null || !texture.sRGBTexture || !texture.mipmapEnabled ||
                        texture.maxTextureSize != 1024 || texture.isReadable)
                    {
                        errors.Add($"Texture importer does not match KayKit policy: {path}");
                    }
                }
                else if (string.Equals(Path.GetExtension(path), ".fbx", StringComparison.OrdinalIgnoreCase))
                {
                    ModelImporter model = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (model == null || !Mathf.Approximately(model.globalScale, 1.0f) ||
                        model.addCollider || model.materialImportMode != ModelImporterMaterialImportMode.None)
                    {
                        errors.Add($"Model importer does not match KayKit policy: {path}");
                        continue;
                    }

                    bool humanoid = KayKitPathUtility.IsAnimationSource(path) ||
                        KayKitPathUtility.IsCharacterModel(path);
                    if (humanoid && model.animationType != ModelImporterAnimationType.Human)
                        errors.Add($"Humanoid import is required for: {path}");
                    if (!humanoid && model.importAnimation)
                        errors.Add($"Animation import must be disabled for: {path}");
                    if (KayKitPathUtility.IsAnimationSource(path))
                        ValidateAnimationImporter(path, model, errors);
                }
            }
        }

        private static void ValidateAnimationImporter(
            string path,
            ModelImporter importer,
            ICollection<string> errors)
        {
            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips.Length == 0)
            {
                errors.Add($"Animation clip overrides are missing for: {path}");
                return;
            }

            AnimationClip[] importedClips = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (AnimationClip importedClip in importedClips)
            {
                ModelImporterClipAnimation settings = clips.FirstOrDefault(clip =>
                    string.Equals(clip.name, importedClip.name, StringComparison.Ordinal));
                if (settings == null)
                {
                    errors.Add($"Animation clip override is missing for {path}/{importedClip.name}.");
                    continue;
                }

                bool shouldLoop =
                    KayKitPathUtility.ClassifyClip(importedClip.name) == KayKitClipSemantics.Loop;
                if (settings.loopTime != shouldLoop || settings.loopPose != shouldLoop ||
                    !settings.lockRootRotation || !settings.lockRootHeightY ||
                    !settings.lockRootPositionXZ)
                {
                    errors.Add(
                        $"Animation clip import settings are incorrect for {path}/{importedClip.name}.");
                }
                if (importedClip.isLooping != shouldLoop)
                    errors.Add($"Imported animation loop setting is incorrect for {path}/{importedClip.name}.");
            }
        }

        private static void ValidateUniqueReferences(
            IEnumerable<(string Id, Object Asset)> values,
            string label,
            ICollection<string> errors)
        {
            HashSet<string> ids = new(StringComparer.Ordinal);
            foreach ((string id, Object asset) in values)
            {
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                    errors.Add($"{label} contains an empty or duplicate ID: {id}");
                if (asset == null)
                    errors.Add($"{label} contains a missing reference: {id}");
            }
        }

        private static T GetOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static string[] GetFbxPaths(string root)
        {
            if (!AssetDatabase.IsValidFolder(root))
                return Array.Empty<string>();

            return AssetDatabase.FindAssets(string.Empty, new[] { root })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => string.Equals(Path.GetExtension(path), ".fbx", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] GetVendorFilePaths()
        {
            return KayKitPathUtility.PackRoots
                .Where(AssetDatabase.IsValidFolder)
                .SelectMany(root => AssetDatabase.FindAssets(string.Empty, new[] { root }))
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(File.Exists)
                .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        private static void EnsureProjectFolders()
        {
            EnsureFolder(ProjectRoot);
            EnsureFolder(CatalogRoot);
            EnsureFolder(MaterialRoot);
            EnsureFolder(PrefabRoot);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path)?.Replace((char)92, '/');
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent))
                throw new InvalidOperationException($"Invalid asset folder path: {path}");
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private sealed class PackDescriptor
        {
            public string Root { get; }
            public string Name { get; }
            public string Version { get; }
            public string SourceUrl { get; }
            public int ExpectedFbxCount { get; }
            public int ExpectedPngCount { get; }

            public PackDescriptor(
                string root,
                string name,
                string version,
                string sourceUrl,
                int expectedFbxCount,
                int expectedPngCount)
            {
                Root = root;
                Name = name;
                Version = version;
                SourceUrl = sourceUrl;
                ExpectedFbxCount = expectedFbxCount;
                ExpectedPngCount = expectedPngCount;
            }
        }
    }

    public sealed class KayKitValidationReport
    {
        public IReadOnlyList<string> Errors { get; }
        public IReadOnlyList<string> Warnings { get; }
        public bool IsValid => Errors.Count == 0;

        public KayKitValidationReport(IEnumerable<string> errors, IEnumerable<string> warnings)
        {
            Errors = errors.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            Warnings = warnings.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

        public string Format()
        {
            List<string> parts = new()
            {
                IsValid ? "KayKit validation passed." : $"KayKit validation failed with {Errors.Count} error(s)."
            };
            parts.AddRange(Errors.Select(error => "ERROR: " + error));
            parts.AddRange(Warnings.Select(warning => "WARNING: " + warning));
            return string.Join(Environment.NewLine, parts);
        }
    }
}
