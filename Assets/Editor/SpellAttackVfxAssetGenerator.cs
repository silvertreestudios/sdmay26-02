#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.KayKit;
using Game.Rules.Unity.Vfx;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Creates the committed built-in-render-pipeline VFX prefabs, materials, and gallery scene.</summary>
public static class SpellAttackVfxAssetGenerator
{
    private const string MaterialRoot = "Assets/Materials/Vfx";
    private const string PrefabRoot = "Assets/Resources/Vfx";
    private const string GalleryScene = "Assets/Scenes/VfxGallery.unity";
    private const string SoftParticlePath = "Assets/Textures/Vfx/soft-particle.png";
    private static Texture2D softParticle;

    /// <summary>
    /// Regenerates all serialized VFX content through Unity's asset APIs after protecting modified
    /// scenes in interactive Editor sessions. Batchmode generation remains non-interactive.
    /// </summary>
    [MenuItem("Tools/VFX/Regenerate Production Gallery")]
    public static void Generate()
    {
        SpellAttackVfxEditorEntrySafety.TryRun(
            Application.isBatchMode,
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo,
            GenerateAssetsAndScene
        );
    }

    private static void GenerateAssetsAndScene()
    {
        Directory.CreateDirectory(MaterialRoot);
        Directory.CreateDirectory(PrefabRoot);
        softParticle = CreateSoftParticleTexture();
        HashSet<string> cues = CollectCues();
        foreach (string cue in cues.OrderBy(value => value, StringComparer.Ordinal))
            CreatePrefab(cue);
        CreateGalleryScene();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Generated {cues.Count} production VFX prefabs and the VFX gallery scene.");
    }

    /// <summary>
    /// Regenerates only the Heal delivery and target-result prefabs whose authored motion differs.
    /// </summary>
    [MenuItem("Tools/VFX/Regenerate Heal Delivery Prefabs")]
    public static void GenerateHealDeliveryPrefabs()
    {
        Directory.CreateDirectory(MaterialRoot);
        Directory.CreateDirectory(PrefabRoot);
        softParticle = CreateSoftParticleTexture();
        string[] cues =
        {
            "spell/heal/2-action-delivery",
            "spell/heal/3-action-emanation",
            "spell/heal/3-action-living",
            "spell/heal/3-action-undead-critical-success",
            "spell/heal/3-action-undead-success",
            "spell/heal/3-action-undead-failure",
            "spell/heal/3-action-undead-critical-failure",
        };
        foreach (string cue in cues)
            CreatePrefab(cue);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Generated {cues.Length} Heal delivery and target-result VFX prefabs.");
    }

    private static HashSet<string> CollectCues()
    {
        VfxCoverageManifest manifest = VfxCoverageManifest.Load();
        return manifest.entries.Select(entry => entry.cue).ToHashSet(StringComparer.Ordinal);
    }

    private static void CreatePrefab(string cue)
    {
        string assetPath = PrefabRoot + "/" + cue + ".prefab";
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath) ?? PrefabRoot);
        Color color = ColorFor(cue);
        Material material = RequireMaterial(cue, color);
        GameObject root = new("VFX " + cue.Replace('/', ' '));
        try
        {
            UnityVfxInstance behavior = root.AddComponent<UnityVfxInstance>();
            SerializedObject serialized = new(behavior);
            serialized.FindProperty("motion").enumValueIndex = (int)MotionFor(cue);
            serialized.FindProperty("duration").floatValue = DurationFor(cue);
            serialized.FindProperty("baseScale").floatValue = BaseScaleFor(cue);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            AddAuthoredGeometry(root.transform, cue, material);
            AddParticles(root, cue, color, material);
            AddTrail(root, cue, color, material);
            PrefabUtility.SaveAsPrefabAsset(root, assetPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void AddAuthoredGeometry(Transform parent, string cue, Material material)
    {
        PrimitiveType primitive =
            cue.Contains("slash", StringComparison.Ordinal) ? PrimitiveType.Cube
            : cue.Contains("bow", StringComparison.Ordinal)
            || cue.Contains("lance", StringComparison.Ordinal)
            || cue.Contains("piercing", StringComparison.Ordinal)
            || cue == "spell/heal/2-action-delivery"
                ? PrimitiveType.Capsule
            : cue.Contains("shield", StringComparison.Ordinal) ? PrimitiveType.Cylinder
            : cue.Contains("hymn", StringComparison.Ordinal)
            || cue.Contains("emanation", StringComparison.Ordinal)
                ? PrimitiveType.Quad
            : PrimitiveType.Sphere;
        GameObject shape = GameObject.CreatePrimitive(primitive);
        shape.name = "Authored " + primitive;
        shape.transform.SetParent(parent, false);
        Collider collider = shape.GetComponent<Collider>();
        if (collider != null)
            UnityEngine.Object.DestroyImmediate(collider);
        Renderer renderer = shape.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        if (primitive == PrimitiveType.Capsule)
        {
            shape.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shape.transform.localScale = new Vector3(0.12f, 0.55f, 0.12f);
        }
        else if (primitive == PrimitiveType.Cylinder)
        {
            shape.transform.localScale = cue.Contains("shield", StringComparison.Ordinal)
                ? new Vector3(0.65f, 0.04f, 0.65f)
                : new Vector3(0.5f, 0.035f, 0.5f);
            shape.transform.localRotation = Quaternion.Euler(
                90f,
                0f,
                cue.Contains("slash", StringComparison.Ordinal) ? 35f : 0f
            );
        }
        else if (primitive == PrimitiveType.Cube)
        {
            shape.transform.localScale = cue.Contains("heavy-slash", StringComparison.Ordinal)
                ? new Vector3(0.16f, 0.09f, 1.45f)
                : new Vector3(0.11f, 0.07f, 1.05f);
            shape.transform.localRotation = Quaternion.Euler(0f, 0f, 38f);
        }
        else if (primitive == PrimitiveType.Quad)
        {
            shape.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shape.transform.localScale = new Vector3(1.6f, 1.6f, 1f);
        }
        else
            shape.transform.localScale = Vector3.one * 0.42f;

        AddSignatureGeometry(parent, cue, material);

        if (IsPersistent(cue) || cue.Contains("critical", StringComparison.Ordinal))
        {
            for (int index = 0; index < 8; index++)
            {
                float angle = index * Mathf.PI * 0.25f;
                GameObject mote = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                mote.name = "Orbit Mote " + index;
                mote.transform.SetParent(parent, false);
                mote.transform.localPosition = new Vector3(
                    Mathf.Cos(angle) * 0.65f,
                    Mathf.Sin(index * 1.7f) * 0.12f,
                    Mathf.Sin(angle) * 0.65f
                );
                mote.transform.localScale = Vector3.one * 0.09f;
                UnityEngine.Object.DestroyImmediate(mote.GetComponent<Collider>());
                mote.GetComponent<Renderer>().sharedMaterial = material;
            }
        }
    }

    private static void AddSignatureGeometry(Transform parent, string cue, Material material)
    {
        if (cue.Contains("guidance", StringComparison.Ordinal))
        {
            for (int index = 0; index < 4; index++)
            {
                GameObject point = GameObject.CreatePrimitive(PrimitiveType.Cube);
                point.name = "Compass Point " + index;
                point.transform.SetParent(parent, false);
                point.transform.localRotation = Quaternion.Euler(0f, index * 90f, 0f);
                point.transform.localPosition = point.transform.forward * 0.38f;
                point.transform.localScale = new Vector3(0.08f, 0.035f, 0.55f);
                UnityEngine.Object.DestroyImmediate(point.GetComponent<Collider>());
                point.GetComponent<Renderer>().sharedMaterial = material;
            }
        }
        if (cue.Contains("haunting-hymn", StringComparison.Ordinal))
        {
            int waveCount =
                cue.EndsWith("/critical-success", StringComparison.Ordinal) ? 1
                : cue.EndsWith("/success", StringComparison.Ordinal) ? 2
                : cue.EndsWith("/critical-failure", StringComparison.Ordinal) ? 4
                : 3;
            for (int index = 0; index < waveCount; index++)
            {
                GameObject wave = GameObject.CreatePrimitive(PrimitiveType.Quad);
                wave.name = "Sound Wave " + index;
                wave.transform.SetParent(parent, false);
                wave.transform.localPosition = new Vector3(0f, 0.05f, 0.28f + index * 0.34f);
                wave.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                wave.transform.localScale = Vector3.one * (0.45f + index * 0.35f);
                UnityEngine.Object.DestroyImmediate(wave.GetComponent<Collider>());
                wave.GetComponent<Renderer>().sharedMaterial = material;
            }
            if (cue.EndsWith("/critical-failure", StringComparison.Ordinal))
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    GameObject deafened = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    deafened.name = side < 0 ? "Deafened Left Ear" : "Deafened Right Ear";
                    deafened.transform.SetParent(parent, false);
                    deafened.transform.localPosition = new Vector3(side * 0.42f, 0.62f, 0f);
                    deafened.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    deafened.transform.localScale = Vector3.one * 0.22f;
                    UnityEngine.Object.DestroyImmediate(deafened.GetComponent<Collider>());
                    deafened.GetComponent<Renderer>().sharedMaterial = material;
                }
            }
        }
        if (cue.Contains("heal", StringComparison.Ordinal))
        {
            if (cue.Contains("-living", StringComparison.Ordinal))
            {
                GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pillar.name = "Living Restoration Pillar";
                pillar.transform.SetParent(parent, false);
                pillar.transform.localScale = new Vector3(0.16f, 0.9f, 0.16f);
                UnityEngine.Object.DestroyImmediate(pillar.GetComponent<Collider>());
                pillar.GetComponent<Renderer>().sharedMaterial = material;
            }
            else if (cue.Contains("-undead-", StringComparison.Ordinal))
            {
                for (int index = 0; index < 6; index++)
                {
                    float angle = index * Mathf.PI / 3f;
                    GameObject spike = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    spike.name = "Undead Vitality Break " + index;
                    spike.transform.SetParent(parent, false);
                    spike.transform.localPosition = new Vector3(
                        Mathf.Cos(angle) * 0.46f,
                        0.12f,
                        Mathf.Sin(angle) * 0.46f
                    );
                    spike.transform.localRotation = Quaternion.Euler(
                        25f,
                        -angle * Mathf.Rad2Deg,
                        35f
                    );
                    spike.transform.localScale = new Vector3(0.08f, 0.08f, 0.58f);
                    UnityEngine.Object.DestroyImmediate(spike.GetComponent<Collider>());
                    spike.GetComponent<Renderer>().sharedMaterial = material;
                }
            }
        }
        if (cue == "auxiliary/rotting-aura-active")
        {
            for (int index = 0; index < 12; index++)
            {
                float angle = index * Mathf.PI * 2f / 12f;
                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = "Aura Boundary " + index;
                marker.transform.SetParent(parent, false);
                marker.transform.localPosition = new Vector3(
                    Mathf.Cos(angle),
                    0f,
                    Mathf.Sin(angle)
                );
                marker.transform.localRotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 0f);
                marker.transform.localScale = new Vector3(0.08f, 0.05f, 0.28f);
                UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
                marker.GetComponent<Renderer>().sharedMaterial = material;
            }
        }
    }

    private static void AddParticles(GameObject root, string cue, Color color, Material material)
    {
        ParticleSystem particles = root.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.playOnAwake = false;
        main.loop = IsPersistent(cue);
        main.duration = IsPersistent(cue) ? 2f : DurationFor(cue);
        main.startLifetime = IsPersistent(cue) ? 0.9f : 0.42f;
        main.startSpeed = cue.Contains("hymn", StringComparison.Ordinal) ? 1.4f : 0.62f;
        main.startSize = cue.Contains("critical", StringComparison.Ordinal) ? 0.16f : 0.1f;
        main.startColor = color;
        main.maxParticles = 96;
        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = IsPersistent(cue) ? 12f : 0f;
        if (!IsPersistent(cue))
            emission.SetBursts(
                new[]
                {
                    new ParticleSystem.Burst(
                        0f,
                        (short)(cue.Contains("critical", StringComparison.Ordinal) ? 20 : 12)
                    ),
                }
            );
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType =
            cue.Contains("hymn", StringComparison.Ordinal) ? ParticleSystemShapeType.Cone
            : cue.Contains("bless", StringComparison.Ordinal)
            || cue == "spell/heal/3-action-emanation"
                ? ParticleSystemShapeType.Donut
            : ParticleSystemShapeType.Sphere;
        shape.radius = 0.45f;
        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(
            new Gradient
            {
                colorKeys = new[]
                {
                    new GradientColorKey(color, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                alphaKeys = new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) },
            }
        );
        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
    }

    private static void AddTrail(GameObject root, string cue, Color color, Material material)
    {
        if (MotionFor(cue) is not (VfxMotionKind.Projectile or VfxMotionKind.Beam))
            return;
        TrailRenderer trail = root.AddComponent<TrailRenderer>();
        trail.sharedMaterial = material;
        trail.time = 0.32f;
        trail.minVertexDistance = 0.025f;
        trail.widthCurve = AnimationCurve.EaseInOut(0f, 0.24f, 1f, 0f);
        trail.colorGradient = new Gradient
        {
            colorKeys = new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(color, 1f),
            },
            alphaKeys = new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) },
        };
    }

    private static Material RequireMaterial(string cue, Color color)
    {
        string family = MaterialFamilyFor(cue);
        string path = MaterialRoot + "/" + family + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Unlit/Color");
            material = new Material(shader) { name = "VFX " + family };
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = color;
        if (material.HasProperty("_MainTex") && softParticle != null)
            material.SetTexture("_MainTex", softParticle);
        if (material.HasProperty("_Mode"))
        {
            material.SetFloat("_Mode", 2f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void CreateGalleryScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "VfxGallery";
        RenderSettings.ambientLight = new Color(0.1f, 0.12f, 0.18f);
        RenderSettings.fog = true;
        RenderSettings.fogColor = new Color(0.025f, 0.035f, 0.055f);
        RenderSettings.fogDensity = 0.018f;

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Dungeon Gallery Floor";
        floor.transform.localScale = new Vector3(2.5f, 1f, 2f);
        floor.GetComponent<Renderer>().sharedMaterial = RequireMaterial(
            "gallery/stone",
            new Color(0.16f, 0.18f, 0.22f)
        );
        Material gridMaterial = RequireMaterial(
            "gallery/grid-lines",
            new Color(0.24f, 0.38f, 0.48f, 0.48f)
        );
        for (int index = -7; index <= 7; index++)
        {
            GameObject lineX = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lineX.name = "Grid Line X " + index;
            lineX.transform.position = new Vector3(0f, 0.012f, index);
            lineX.transform.localScale = new Vector3(14f, 0.018f, 0.025f);
            lineX.GetComponent<Renderer>().sharedMaterial = gridMaterial;
            UnityEngine.Object.DestroyImmediate(lineX.GetComponent<Collider>());
            GameObject lineZ = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lineZ.name = "Grid Line Z " + index;
            lineZ.transform.position = new Vector3(index, 0.012f, 0f);
            lineZ.transform.localScale = new Vector3(0.025f, 0.018f, 14f);
            lineZ.GetComponent<Renderer>().sharedMaterial = gridMaterial;
            UnityEngine.Object.DestroyImmediate(lineZ.GetComponent<Collider>());
        }
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Dungeon Wall";
            wall.transform.position = new Vector3(0f, 1.5f, side * 7.5f);
            wall.transform.localScale = new Vector3(14f, 3f, 0.4f);
            wall.GetComponent<Renderer>().sharedMaterial = floor
                .GetComponent<Renderer>()
                .sharedMaterial;
        }

        CreateModel(
            "Gallery Source",
            new Vector3(-2.2f, 1f, 0f),
            "Assets/KayKit/Prefabs/Animated/MageStaffAnimated.prefab"
        );
        CreateModel(
            "Gallery Target",
            new Vector3(2.2f, 1f, 0f),
            "Assets/KayKit/Prefabs/Animated/BarbarianAnimated.prefab"
        );
        GameObject cameraObject = new("Main Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 7.5f, -10f);
        cameraObject.transform.LookAt(new Vector3(0f, 1f, 0f));
        camera.fieldOfView = 48f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.025f, 0.035f, 0.055f);

        GameObject key = new("Dungeon Key Light");
        Light light = key.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(0.72f, 0.78f, 1f);
        light.intensity = 0.85f;
        light.shadows = LightShadows.Soft;
        key.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
        GameObject warm = new("Dungeon Torch Light");
        Light warmLight = warm.AddComponent<Light>();
        warmLight.type = LightType.Point;
        warmLight.color = new Color(1f, 0.48f, 0.18f);
        warmLight.range = 9f;
        warmLight.intensity = 3f;
        warm.transform.position = new Vector3(0f, 3f, 2.5f);

        new GameObject("VFX Gallery Controller").AddComponent<VfxGalleryController>();
        EditorSceneManager.SaveScene(scene, GalleryScene);
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.All(value => !string.Equals(value.path, GalleryScene, StringComparison.Ordinal)))
            scenes.Add(new EditorBuildSettingsScene(GalleryScene, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void CreateModel(string name, Vector3 position, string modelPath)
    {
        GameObject model = new(name);
        model.transform.position = position;
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (modelAsset == null)
            throw new InvalidOperationException($"Gallery model '{modelPath}' is unavailable.");
        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        visual.name = name + " Model";
        visual.transform.SetParent(model.transform, false);
        visual.transform.localPosition = Vector3.down;
        visual.transform.localRotation = Quaternion.Euler(
            0f,
            name.Contains("Source") ? 90f : -90f,
            0f
        );
        CreatureAnimationController animation =
            visual.GetComponentInChildren<CreatureAnimationController>();
        if (animation == null)
            throw new InvalidOperationException(
                $"Gallery model '{modelPath}' requires production animation presentation."
            );
        CreaturePresentation presentation = model.AddComponent<CreaturePresentation>();
        presentation.Bind(animation, visual.GetComponentInChildren<CreatureEquipmentVisuals>());
        GameObject baseRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseRing.name = name + " Grid Base";
        baseRing.transform.SetParent(model.transform, false);
        baseRing.transform.localPosition = new Vector3(0f, -1f, 0f);
        baseRing.transform.localScale = new Vector3(0.85f, 0.05f, 0.85f);
        baseRing.GetComponent<Renderer>().sharedMaterial = RequireMaterial(
            "gallery/grid-base",
            new Color(0.08f, 0.1f, 0.13f)
        );
    }

    private static Texture2D CreateSoftParticleTexture()
    {
        string directory = Path.GetDirectoryName(SoftParticlePath) ?? "Assets/Textures";
        Directory.CreateDirectory(directory);
        const int size = 64;
        Texture2D texture = new(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float normalizedX = (x + 0.5f) / size * 2f - 1f;
                float normalizedY = (y + 0.5f) / size * 2f - 1f;
                float distance = Mathf.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY);
                float alpha = Mathf.Clamp01(1f - distance);
                alpha *= alpha * (3f - 2f * alpha);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        File.WriteAllBytes(SoftParticlePath, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(SoftParticlePath, ImportAssetOptions.ForceSynchronousImport);
        if (AssetImporter.GetAtPath(SoftParticlePath) is TextureImporter importer)
        {
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(SoftParticlePath);
    }

    private static bool IsPersistent(string cue) =>
        cue.Contains("persistent", StringComparison.Ordinal)
        || cue
            is "auxiliary/rage"
                or "auxiliary/rage-quick-tempered"
                or "auxiliary/rotting-aura-active";

    private static VfxMotionKind MotionFor(string cue)
    {
        if (IsPersistent(cue))
            return VfxMotionKind.Persistent;
        if (
            cue.Contains("projectile", StringComparison.Ordinal)
            || cue == "spell/heal/2-action-delivery"
            || cue.Contains("bow/", StringComparison.Ordinal)
            || cue.Contains("sling/", StringComparison.Ordinal)
        )
            return VfxMotionKind.Projectile;
        if (cue == "strike/piercing/travel")
            return VfxMotionKind.Beam;
        if (
            cue.Contains("bless", StringComparison.Ordinal)
            || cue == "spell/heal/3-action-emanation"
            || cue.Contains("rotting-aura", StringComparison.Ordinal)
        )
            return VfxMotionKind.Emanation;
        if (
            cue.Contains("infuse-vitality/", StringComparison.Ordinal)
            && cue.Contains("-action", StringComparison.Ordinal)
        )
            return VfxMotionKind.Beam;
        return VfxMotionKind.Burst;
    }

    private static float DurationFor(string cue) =>
        cue.Contains("projectile", StringComparison.Ordinal)
        || cue == "spell/heal/2-action-delivery"
        || cue.EndsWith("/travel", StringComparison.Ordinal)
            ? 0.65f
            : 0.8f;

    private static float BaseScaleFor(string cue)
    {
        if (cue == "auxiliary/rotting-aura-active")
            return 2.2f;
        if (cue.EndsWith("/critical-failure", StringComparison.Ordinal))
            return 1.35f;
        if (cue.EndsWith("/failure", StringComparison.Ordinal))
            return 1.12f;
        if (cue.EndsWith("/critical-success", StringComparison.Ordinal))
            return 0.72f;
        if (cue.EndsWith("/success", StringComparison.Ordinal))
            return 0.9f;
        return cue.Contains("critical", StringComparison.Ordinal) ? 1.35f : 1f;
    }

    private static string MaterialFamilyFor(string cue)
    {
        if (cue.Contains("heal/", StringComparison.Ordinal))
            return cue.Contains("-undead-", StringComparison.Ordinal)
                ? "spell-heal-undead"
                : "spell-heal";
        if (
            cue.StartsWith("spell/haunting-hymn/", StringComparison.Ordinal)
            && !cue.EndsWith("/cast", StringComparison.Ordinal)
        )
            return "spell-haunting-hymn-" + cue.Split('/').Last();
        return cue.Split('/')[0] + "-" + cue.Split('/')[1];
    }

    private static Color ColorFor(string cue)
    {
        if (cue.Contains("heal/", StringComparison.Ordinal))
            return cue.Contains("-undead-", StringComparison.Ordinal)
                ? new Color(0.32f, 0.92f, 1f, 0.82f)
                : new Color(0.5f, 1f, 0.56f, 0.78f);
        if (cue.StartsWith("spell/haunting-hymn/", StringComparison.Ordinal))
        {
            if (cue.EndsWith("/critical-success", StringComparison.Ordinal))
                return new Color(0.46f, 0.42f, 0.58f, 0.58f);
            if (cue.EndsWith("/success", StringComparison.Ordinal))
                return new Color(0.55f, 0.42f, 0.78f, 0.68f);
            if (cue.EndsWith("/critical-failure", StringComparison.Ordinal))
                return new Color(0.95f, 0.18f, 0.72f, 0.9f);
            return new Color(0.68f, 0.26f, 0.94f, 0.8f);
        }
        if (
            cue.Contains("haunting-hymn", StringComparison.Ordinal)
            || cue.Contains("sneak-attack", StringComparison.Ordinal)
        )
            return new Color(0.58f, 0.25f, 0.95f, 0.78f);
        if (cue.Contains("infuse-vitality", StringComparison.Ordinal))
            return new Color(0.5f, 1f, 0.56f, 0.78f);
        if (
            cue.Contains("light", StringComparison.Ordinal)
            || cue.Contains("bless", StringComparison.Ordinal)
            || cue.Contains("divine-lance", StringComparison.Ordinal)
            || cue.Contains("guidance", StringComparison.Ordinal)
        )
            return cue.Contains("divine-lance", StringComparison.Ordinal)
                ? new Color(1f, 0.9f, 0.52f, 0.86f)
                : new Color(1f, 0.78f, 0.25f, 0.82f);
        if (
            cue.Contains("shield", StringComparison.Ordinal)
            || cue.Contains("bow", StringComparison.Ordinal)
        )
            return new Color(0.35f, 0.78f, 1f, 0.76f);
        if (
            cue.Contains("rotting-aura", StringComparison.Ordinal)
            || cue.Contains("rage", StringComparison.Ordinal)
        )
            return new Color(0.75f, 0.12f, 0.18f, 0.76f);
        return new Color(0.95f, 0.82f, 0.62f, 0.78f);
    }
}

internal static class SpellAttackVfxEditorEntrySafety
{
    internal static bool TryRun(
        bool isBatchMode,
        Func<bool> saveCurrentModifiedScenesIfUserWantsTo,
        Action sceneReplacingWork
    )
    {
        if (!isBatchMode && !saveCurrentModifiedScenesIfUserWantsTo())
            return false;

        sceneReplacingWork();
        return true;
    }
}
#endif
