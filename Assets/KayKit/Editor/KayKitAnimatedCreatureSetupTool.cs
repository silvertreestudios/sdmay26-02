using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.KayKit;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.KayKit.Editor
{
    public static class KayKitAnimatedCreatureSetupTool
    {
        public const string AnimatedPrefabRoot = KayKitSetupTool.PrefabRoot + "/Animated";
        public const string CreatureVisualCatalogPath = KayKitSetupTool.CatalogRoot + "/CreatureVisualCatalog.asset";
        public const string EquipmentVisualCatalogPath = KayKitSetupTool.CatalogRoot + "/EquipmentVisualCatalog.asset";
        public const string AnimatorControllerPath = KayKitSetupTool.ProjectRoot + "/Animation/KayKitCreatureAnimator.controller";
        public const float CharacterPreviewVisualScale = 0.62f;

        private const string AdventurerModelRoot = "Assets/ThirdParty/KayKit/Adventurers_2.0/Characters/fbx";
        private const string AdventurerAccessoryRoot = "Assets/ThirdParty/KayKit/Adventurers_2.0/Assets/fbx(unity)";
        private const string SkeletonModelRoot = "Assets/ThirdParty/KayKit/Skeletons_1.1/characters/fbx";
        private const string SkeletonAccessoryRoot = "Assets/ThirdParty/KayKit/Skeletons_1.1/assets/fbx(unity)";
        private const string RangerMaterialPath = KayKitSetupTool.MaterialRoot + "/KayKitAdventurers_ranger_texture.mat";
        private const string SkeletonMaterialPath = KayKitSetupTool.MaterialRoot + "/KayKitSkeletons_skeleton_texture.mat";

        private static readonly VisualDefinition[] VisualDefinitions =
        {
            new("RangerAnimated", AdventurerModelRoot + "/Ranger.fbx", KayKitSetupTool.MaterialRoot + "/KayKitAdventurers_ranger_texture.mat", KayKitRigProfile.Adventurer, "adventurer", "dogslicer"),
            new("BarbarianAnimated", AdventurerModelRoot + "/Barbarian.fbx", KayKitSetupTool.MaterialRoot + "/KayKitAdventurers_barbarian_texture.mat", KayKitRigProfile.Adventurer, "adventurer", "greataxe"),
            new("KnightAnimated", AdventurerModelRoot + "/Knight.fbx", KayKitSetupTool.MaterialRoot + "/KayKitAdventurers_knight_texture.mat", KayKitRigProfile.Adventurer, "adventurer", "longsword"),
            new("MageStaffAnimated", AdventurerModelRoot + "/Mage.fbx", KayKitSetupTool.MaterialRoot + "/KayKitAdventurers_mage_texture.mat", KayKitRigProfile.Adventurer, "adventurer", "staff"),
            new("MageWandAnimated", AdventurerModelRoot + "/Mage.fbx", KayKitSetupTool.MaterialRoot + "/KayKitAdventurers_mage_texture.mat", KayKitRigProfile.Adventurer, "adventurer", "wand"),
            new("RogueHoodedAnimated", AdventurerModelRoot + "/Rogue_Hooded.fbx", KayKitSetupTool.MaterialRoot + "/KayKitAdventurers_rogue_texture.mat", KayKitRigProfile.Adventurer, "adventurer", "dagger"),
            new("SkeletonMinionAnimated", SkeletonModelRoot + "/Skeleton_Minion.fbx", SkeletonMaterialPath, KayKitRigProfile.Skeleton, "skeleton", "unarmed"),
            new("SkeletonWarriorAnimated", SkeletonModelRoot + "/Skeleton_Warrior.fbx", SkeletonMaterialPath, KayKitRigProfile.Skeleton, "skeleton", "scimitar")
        };

        [MenuItem("Tools/KayKit/Regenerate Animated Creatures")]
        public static void RegenerateAnimatedCreatureAssets()
        {
            try
            {
                EnsureFolder(AnimatedPrefabRoot);
                EnsureFolder(Path.GetDirectoryName(AnimatorControllerPath)?.Replace((char)92, '/'));

                KayKitAnimationLibrary library = RequireAsset<KayKitAnimationLibrary>(KayKitSetupTool.AnimationLibraryPath);
                ValidateRequiredClips(library);
                AnimatorController controller = GenerateAnimatorController(library);
                EquipmentVisualCatalog equipmentCatalog = GenerateEquipmentCatalog();
                Dictionary<string, GameObject> prefabs = GenerateVisualPrefabs(controller, library, equipmentCatalog);
                CreatureVisualCatalog visualCatalog = GenerateCreatureVisualCatalog(prefabs);
                PatchGameplayPrefabs(visualCatalog);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                IReadOnlyList<string> errors = ValidateAnimatedAssets();
                if (errors.Count > 0)
                    throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
                Debug.Log("KayKit animated creature setup complete.");
            }
            catch (Exception exception)
            {
                Debug.LogError("KayKit animated creature setup failed: " + exception.Message);
                throw;
            }
        }

        public static IReadOnlyList<string> ValidateAnimatedAssets()
        {
            List<string> errors = new();
            KayKitAnimationLibrary library = AssetDatabase.LoadAssetAtPath<KayKitAnimationLibrary>(KayKitSetupTool.AnimationLibraryPath);
            if (library == null)
                errors.Add("Missing KayKit animation library.");
            else
            {
                foreach (string id in CreatureAnimationController.RequiredDefaultClipIds())
                    if (!library.TryGet(id, out KayKitAnimationEntry entry) || entry.Clip == null)
                        errors.Add("Missing required default animation: " + id);
            }

            CreatureVisualCatalog visualCatalog = AssetDatabase.LoadAssetAtPath<CreatureVisualCatalog>(CreatureVisualCatalogPath);
            Dictionary<string, string> expectedMappings = new(StringComparer.OrdinalIgnoreCase)
            {
                { "Lena", "adventurers/ranger" },
                { "Torgrim", "adventurers/barbarian" },
                { "Zombie Shambler", "skeletons/skeleton_minion" },
                { "Zombie Shambler (Rotting Aura)", "skeletons/skeleton_minion" },
                { "Skeleton Guard", "skeletons/skeleton_warrior" },
                { "Fighter", "adventurers/knight" },
                { "Cleric", "adventurers/mage" },
                { "Rogue", "adventurers/rogue_hooded" },
                { "Sorcerer", "adventurers/mage" },
                { "Barbarian", "adventurers/barbarian" }
            };
            foreach ((string key, string visualId) in expectedMappings)
            {
                if (visualCatalog == null || !visualCatalog.TryResolve(key, out CreatureVisualCatalogEntry entry) ||
                    !string.Equals(entry.VisualId, visualId, StringComparison.Ordinal))
                    errors.Add($"Missing creature visual mapping: {key} -> {visualId}");
            }

            GameObject controllerAssetOwner = null;
            foreach (VisualDefinition definition in VisualDefinitions)
            {
                string path = AnimatedPrefabRoot + "/" + definition.PrefabName + ".prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    errors.Add("Missing animated visual prefab: " + path);
                    continue;
                }
                Animator animator = prefab.GetComponentInChildren<Animator>(true);
                if (animator == null || animator.applyRootMotion)
                    errors.Add("Animated visual must have root motion disabled: " + path);
                if (prefab.GetComponent<CreatureAnimationController>() == null ||
                    prefab.GetComponent<CreatureEquipmentVisuals>() == null)
                    errors.Add("Animated visual is missing presentation components: " + path);
                controllerAssetOwner = prefab;
            }
            _ = controllerAssetOwner;

            ValidatePatchedPrefab("Assets/Prefabs/Creatures/Lena.prefab", visualCatalog, errors);
            ValidatePatchedPrefab("Assets/Prefabs/Creatures/Torgrim.prefab", visualCatalog, errors);
            ValidatePatchedPrefab("Assets/Prefabs/Creatures/goblin-warrior.prefab", visualCatalog, errors);
            ValidatePatchedPrefab("Assets/Prefabs/Creatures/kobold-warrior.prefab", visualCatalog, errors);
            ValidatePatchedPrefab("Assets/Prefabs/UI/ViewModel.prefab", visualCatalog, errors, false);
            return errors;
        }

        private static AnimatorController GenerateAnimatorController(KayKitAnimationLibrary library)
        {
            AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorControllerPath);
            if (existing != null)
                return existing;

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(AnimatorControllerPath);
            controller.AddParameter(CreatureAnimationController.IdleParameter, AnimatorControllerParameterType.Bool);
            controller.AddParameter(CreatureAnimationController.SpeedParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(CreatureAnimationController.ActionLoopParameter, AnimatorControllerParameterType.Bool);
            controller.AddParameter(CreatureAnimationController.ActionTrigger, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(CreatureAnimationController.DeathTrigger, AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState idle = machine.AddState("Idle", new Vector3(100, 50));
            AnimatorState walk = machine.AddState("Walk", new Vector3(100, 150));
            AnimatorState action = machine.AddState("Action", new Vector3(350, 50));
            AnimatorState death = machine.AddState("Death", new Vector3(350, 150));
            idle.motion = GetRequiredClip(library, "animation/general/idle_a");
            walk.motion = GetRequiredClip(library, "animation/movementbasic/walking_a");
            action.motion = GetRequiredClip(library, "animation/combatmelee/melee_unarmed_attack_punch_a");
            death.motion = GetRequiredClip(library, "animation/general/death_a");
            walk.speedParameterActive = true;
            walk.speedParameter = CreatureAnimationController.SpeedParameter;
            machine.defaultState = idle;

            AddBoolTransition(idle, walk, CreatureAnimationController.IdleParameter, true);
            AddBoolTransition(walk, idle, CreatureAnimationController.IdleParameter, false);
            AddActionExit(action, idle, false);
            AddActionExit(action, walk, true);

            AnimatorStateTransition actionTransition = machine.AddAnyStateTransition(action);
            actionTransition.duration = 0.05f;
            actionTransition.canTransitionToSelf = false;
            actionTransition.AddCondition(AnimatorConditionMode.If, 0, CreatureAnimationController.ActionTrigger);
            AnimatorStateTransition deathTransition = machine.AddAnyStateTransition(death);
            deathTransition.duration = 0.05f;
            deathTransition.canTransitionToSelf = false;
            deathTransition.AddCondition(AnimatorConditionMode.If, 0, CreatureAnimationController.DeathTrigger);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static EquipmentVisualCatalog GenerateEquipmentCatalog()
        {
            EquipmentVisualCatalog catalog = GetOrCreate<EquipmentVisualCatalog>(EquipmentVisualCatalogPath);
            Material ranger = RequireAsset<Material>(RangerMaterialPath);
            Material skeleton = RequireAsset<Material>(SkeletonMaterialPath);
            List<EquipmentVisualCatalogEntry> entries = new()
            {
                Entry("unarmed", "unarmed", "", "", -1, -1, AnimationStyle.Unarmed),
                Entry("dogslicer", "dogslicer", "", "", -1, -1, AnimationStyle.OneHandMelee, A("dagger.fbx", ranger, EquipmentSocket.RightHand)),
                Entry("dagger", "dagger", "", "", -1, -1, AnimationStyle.OneHandMelee, A("dagger.fbx", ranger, EquipmentSocket.RightHand)),
                Entry("longsword", "longsword", "", "", -1, -1, AnimationStyle.OneHandMelee, A("sword_1handed.fbx", ranger, EquipmentSocket.RightHand)),
                Entry("scimitar", "scimitar", "", "", -1, -1, AnimationStyle.OneHandMelee, A("sword_1handed.fbx", ranger, EquipmentSocket.RightHand)),
                Entry("skeleton-longsword", "longsword", "skeleton", "", -1, -1, AnimationStyle.OneHandMelee, S("Skeleton_Blade.fbx", skeleton, EquipmentSocket.RightHand)),
                Entry("skeleton-scimitar", "scimitar", "skeleton", "", -1, -1, AnimationStyle.OneHandMelee, S("Skeleton_Blade.fbx", skeleton, EquipmentSocket.RightHand)),
                Entry("greataxe", "greataxe", "", "", -1, -1, AnimationStyle.TwoHandMelee, A("axe_2handed.fbx", ranger, EquipmentSocket.RightHand)),
                Entry("skeleton-greataxe", "greataxe", "skeleton", "", -1, -1, AnimationStyle.TwoHandMelee, S("Skeleton_Axe.fbx", skeleton, EquipmentSocket.RightHand)),
                Entry("shortbow", "shortbow", "", "", -1, -1, AnimationStyle.Bow, A("bow_withString.fbx", ranger, EquipmentSocket.RightHand), A("quiver.fbx", ranger, EquipmentSocket.Quiver)),
                Entry("skeleton-shortbow", "shortbow", "skeleton", "", -1, -1, AnimationStyle.Bow, A("bow_withString.fbx", ranger, EquipmentSocket.RightHand), S("Skeleton_Quiver.fbx", skeleton, EquipmentSocket.Quiver)),
                Entry("halberd", "halberd", "", "", -1, -1, AnimationStyle.TwoHandMelee, A("staff.fbx", ranger, EquipmentSocket.RightHand)),
                Entry("spear", "spear", "", "", -1, -1, AnimationStyle.TwoHandMelee, A("staff.fbx", ranger, EquipmentSocket.RightHand)),
                Entry("skeleton-halberd", "halberd", "skeleton", "", -1, -1, AnimationStyle.TwoHandMelee, S("Skeleton_Staff.fbx", skeleton, EquipmentSocket.RightHand)),
                Entry("skeleton-spear", "spear", "skeleton", "", -1, -1, AnimationStyle.TwoHandMelee, S("Skeleton_Staff.fbx", skeleton, EquipmentSocket.RightHand)),
                Entry("sling", "sling", "", "", -1, -1, AnimationStyle.OneHandRanged, A("smokebomb.fbx", ranger, EquipmentSocket.RightHand)),
                Entry("staff", "staff", "", "", -1, -1, AnimationStyle.Magic, A("staff.fbx", ranger, EquipmentSocket.RightHand)),
                Entry("wand", "wand", "", "", -1, -1, AnimationStyle.Magic, A("wand.fbx", ranger, EquipmentSocket.RightHand)),
                Entry("fallback-skeleton-sword", "", "skeleton", "sword", 1, 0, AnimationStyle.OneHandMelee, S("Skeleton_Blade.fbx", skeleton, EquipmentSocket.RightHand)),
                Entry("fallback-skeleton-axe", "", "skeleton", "axe", 2, 0, AnimationStyle.TwoHandMelee, S("Skeleton_Axe.fbx", skeleton, EquipmentSocket.RightHand)),
                Entry("fallback-sword", "", "", "sword", 1, 0, AnimationStyle.OneHandMelee, A("sword_1handed.fbx", ranger, EquipmentSocket.RightHand)),
                Entry("fallback-axe", "", "", "axe", 2, 0, AnimationStyle.TwoHandMelee, A("axe_2handed.fbx", ranger, EquipmentSocket.RightHand)),
                Entry("fallback-bow", "", "", "bow", 2, 1, AnimationStyle.Bow, A("bow_withString.fbx", ranger, EquipmentSocket.RightHand), A("quiver.fbx", ranger, EquipmentSocket.Quiver)),
                Entry("fallback-ranged-one-hand", "", "", "", 1, 1, AnimationStyle.OneHandRanged, A("smokebomb.fbx", ranger, EquipmentSocket.RightHand)),
                Entry("fallback-melee-two-hand", "", "", "", 2, 0, AnimationStyle.TwoHandMelee, A("staff.fbx", ranger, EquipmentSocket.RightHand)),
                Entry("fallback-melee-one-hand", "", "", "", 1, 0, AnimationStyle.OneHandMelee, A("sword_1handed.fbx", ranger, EquipmentSocket.RightHand))
            };
            catalog.ReplaceEntries(entries);
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        private static Dictionary<string, GameObject> GenerateVisualPrefabs(
            RuntimeAnimatorController controller,
            KayKitAnimationLibrary library,
            EquipmentVisualCatalog equipmentCatalog)
        {
            Dictionary<string, GameObject> prefabs = new(StringComparer.Ordinal);
            foreach (VisualDefinition definition in VisualDefinitions)
            {
                GameObject model = RequireAsset<GameObject>(definition.ModelPath);
                Material material = RequireAsset<Material>(definition.MaterialPath);
                GameObject root = new(definition.PrefabName);
                try
                {
                    GameObject modelInstance = PrefabUtility.InstantiatePrefab(model) as GameObject;
                    if (modelInstance == null)
                        throw new InvalidOperationException("Could not instantiate character model: " + definition.ModelPath);
                    modelInstance.name = "Model";
                    modelInstance.transform.SetParent(root.transform, false);
                    Animator animator = modelInstance.GetComponent<Animator>();
                    if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                        throw new InvalidOperationException("Character model is not a valid Humanoid: " + definition.ModelPath);
                    animator.applyRootMotion = false;
                    animator.runtimeAnimatorController = controller;
                    foreach (Renderer renderer in modelInstance.GetComponentsInChildren<Renderer>(true))
                        renderer.sharedMaterial = material;

                    Transform torso = animator.GetBoneTransform(HumanBodyBones.UpperChest) ??
                        animator.GetBoneTransform(HumanBodyBones.Chest) ??
                        animator.GetBoneTransform(HumanBodyBones.Spine);
                    if (torso == null)
                        throw new InvalidOperationException("Could not resolve a Humanoid torso bone: " + definition.ModelPath);
                    Transform backSocket = CreateSocket("BackSocket", torso, new Vector3(0, 0.05f, -0.12f));
                    Transform quiverSocket = CreateSocket("QuiverSocket", torso, new Vector3(0.12f, 0.02f, -0.12f));

                    CreatureAnimationController animation = root.AddComponent<CreatureAnimationController>();
                    animation.Configure(animator, controller, library, definition.RigProfile);
                    CreatureEquipmentVisuals equipment = root.AddComponent<CreatureEquipmentVisuals>();
                    equipment.Configure(
                        animator,
                        equipmentCatalog,
                        definition.Species,
                        definition.DefaultWeaponSlug,
                        backSocket,
                        quiverSocket);

                    string path = AnimatedPrefabRoot + "/" + definition.PrefabName + ".prefab";
                    GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                    if (saved == null)
                        throw new InvalidOperationException("Could not save animated visual prefab: " + path);
                    prefabs[definition.PrefabName] = saved;
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }
            return prefabs;
        }

        private static CreatureVisualCatalog GenerateCreatureVisualCatalog(
            IReadOnlyDictionary<string, GameObject> prefabs)
        {
            CreatureVisualCatalog catalog = GetOrCreate<CreatureVisualCatalog>(CreatureVisualCatalogPath);
            GameObject ranger = prefabs["RangerAnimated"];
            GameObject barbarian = prefabs["BarbarianAnimated"];
            GameObject knight = prefabs["KnightAnimated"];
            GameObject mageStaff = prefabs["MageStaffAnimated"];
            GameObject mageWand = prefabs["MageWandAnimated"];
            GameObject rogue = prefabs["RogueHoodedAnimated"];
            GameObject skeletonMinion = prefabs["SkeletonMinionAnimated"];
            GameObject skeletonWarrior = prefabs["SkeletonWarriorAnimated"];
            catalog.ReplaceEntries(new[]
            {
                new CreatureVisualCatalogEntry("Lena", "adventurers/ranger", "adventurer", ranger),
                new CreatureVisualCatalogEntry("Torgrim", "adventurers/barbarian", "adventurer", barbarian),
                new CreatureVisualCatalogEntry("Zombie Shambler", "skeletons/skeleton_minion", "skeleton", skeletonMinion),
                new CreatureVisualCatalogEntry("Zombie Shambler (Rotting Aura)", "skeletons/skeleton_minion", "skeleton", skeletonMinion),
                new CreatureVisualCatalogEntry("Skeleton Guard", "skeletons/skeleton_warrior", "skeleton", skeletonWarrior),
                new CreatureVisualCatalogEntry("Fighter", "adventurers/knight", "adventurer", knight),
                new CreatureVisualCatalogEntry("Cleric", "adventurers/mage", "adventurer", mageStaff),
                new CreatureVisualCatalogEntry("Rogue", "adventurers/rogue_hooded", "adventurer", rogue),
                new CreatureVisualCatalogEntry("Sorcerer", "adventurers/mage", "adventurer", mageWand),
                new CreatureVisualCatalogEntry("Barbarian", "adventurers/barbarian", "adventurer", barbarian)
            });
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        private static void PatchGameplayPrefabs(CreatureVisualCatalog visualCatalog)
        {
            string[] creaturePrefabPaths = AssetDatabase.FindAssets(
                    "t:Prefab",
                    new[] { "Assets/Prefabs/Creatures" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            foreach (string path in creaturePrefabPaths)
                PatchPrefab(path, visualCatalog, true);
            PatchPrefab("Assets/Prefabs/UI/ViewModel.prefab", visualCatalog, false);
        }

        private static void PatchPrefab(string path, CreatureVisualCatalog visualCatalog, bool addPresentation)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                TokenMeshSelection selector = root.GetComponentInChildren<TokenMeshSelection>(true);
                if (selector == null)
                    return;
                Transform visualParent = addPresentation ? root.transform : selector.transform;
                Transform visualRoot = visualParent.Find("VisualRoot");
                if (!addPresentation && visualRoot == null)
                {
                    Transform oldRoot = root.transform.Find("VisualRoot");
                    if (oldRoot != null)
                    {
                        oldRoot.SetParent(visualParent, false);
                        visualRoot = oldRoot;
                    }
                }
                if (visualRoot == null)
                {
                    GameObject visualRootObject = new("VisualRoot");
                    visualRoot = visualRootObject.transform;
                    visualRoot.SetParent(visualParent, false);
                }
                if (!addPresentation)
                {
                    visualRoot.localPosition = Vector3.zero;
                    visualRoot.localRotation = Quaternion.identity;
                    visualRoot.localScale = Vector3.one * CharacterPreviewVisualScale;
                }
                selector.ConfigureAnimatedCatalog(visualCatalog, visualRoot);
                if (addPresentation && root.GetComponent<CreaturePresentation>() == null)
                    root.AddComponent<CreaturePresentation>();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ValidatePatchedPrefab(
            string path,
            CreatureVisualCatalog expectedCatalog,
            ICollection<string> errors,
            bool expectPresentation = true)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            TokenMeshSelection selector = prefab != null
                ? prefab.GetComponentInChildren<TokenMeshSelection>(true)
                : null;
            Transform expectedRoot = selector != null
                ? (expectPresentation ? prefab.transform.Find("VisualRoot") : selector.transform.Find("VisualRoot"))
                : null;
            if (selector == null || selector.VisualCatalog != expectedCatalog || expectedRoot == null)
                errors.Add("Prefab is not wired to the animated visual catalog: " + path);
            if (expectPresentation && prefab != null && prefab.GetComponent<CreaturePresentation>() == null)
                errors.Add("Creature prefab is missing the root presentation seam: " + path);
            if (!expectPresentation && expectedRoot != null &&
                Vector3.Distance(
                    expectedRoot.localScale,
                    Vector3.one * CharacterPreviewVisualScale) > 0.001f)
                errors.Add("Character preview VisualRoot scale is not configured for complete framing: " + path);
        }

        private static EquipmentVisualCatalogEntry Entry(
            string id,
            string itemSlug,
            string species,
            string fallbackGroup,
            int fallbackHands,
            int fallbackRange,
            AnimationStyle style,
            params EquipmentVisualAttachment[] attachments)
        {
            return new EquipmentVisualCatalogEntry(
                id,
                itemSlug,
                species,
                fallbackGroup,
                fallbackHands,
                fallbackRange,
                style,
                attachments);
        }

        private static EquipmentVisualAttachment A(
            string fileName,
            Material material,
            EquipmentSocket socket)
        {
            return Attachment(AdventurerAccessoryRoot + "/" + fileName, material, socket);
        }

        private static EquipmentVisualAttachment S(
            string fileName,
            Material material,
            EquipmentSocket socket)
        {
            return Attachment(SkeletonAccessoryRoot + "/" + fileName, material, socket);
        }

        private static EquipmentVisualAttachment Attachment(
            string path,
            Material material,
            EquipmentSocket socket)
        {
            return new EquipmentVisualAttachment(
                RequireAsset<GameObject>(path),
                material,
                socket,
                Vector3.zero,
                Vector3.zero,
                Vector3.one);
        }

        private static Transform CreateSocket(string name, Transform parent, Vector3 localPosition)
        {
            Transform socket = new GameObject(name).transform;
            socket.SetParent(parent, false);
            socket.localPosition = localPosition;
            socket.localRotation = Quaternion.identity;
            socket.localScale = Vector3.one;
            return socket;
        }

        private static void AddBoolTransition(
            AnimatorState source,
            AnimatorState destination,
            string parameter,
            bool value)
        {
            AnimatorStateTransition transition = source.AddTransition(destination);
            transition.hasExitTime = false;
            transition.duration = 0.1f;
            transition.AddCondition(
                value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                0,
                parameter);
        }

        private static void AddActionExit(AnimatorState action, AnimatorState destination, bool moving)
        {
            AnimatorStateTransition transition = action.AddTransition(destination);
            transition.hasExitTime = true;
            transition.exitTime = 1.0f;
            transition.duration = 0.05f;
            transition.AddCondition(AnimatorConditionMode.IfNot, 0, CreatureAnimationController.ActionLoopParameter);
            transition.AddCondition(
                moving ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                0,
                CreatureAnimationController.IdleParameter);
        }

        private static AnimationClip GetRequiredClip(KayKitAnimationLibrary library, string id)
        {
            if (!library.TryGet(id, out KayKitAnimationEntry entry) || entry.Clip == null)
                throw new InvalidOperationException("Required KayKit animation is missing: " + id);
            return entry.Clip;
        }

        private static void ValidateRequiredClips(KayKitAnimationLibrary library)
        {
            string[] missing = CreatureAnimationController.RequiredDefaultClipIds()
                .Where(id => !library.TryGet(id, out KayKitAnimationEntry entry) || entry.Clip == null)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    "The pinned KayKit package is missing required animated-creature defaults: " +
                    string.Join(", ", missing) +
                    ". Regenerate KayKitAnimationLibrary and verify the imported package version.");
            }
        }

        private static T GetOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;
            EnsureFolder(Path.GetDirectoryName(path)?.Replace((char)92, '/'));
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static T RequireAsset<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new InvalidOperationException("Required KayKit asset is missing: " + path);
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path)?.Replace((char)92, '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private sealed class VisualDefinition
        {
            public string PrefabName { get; }
            public string ModelPath { get; }
            public string MaterialPath { get; }
            public KayKitRigProfile RigProfile { get; }
            public string Species { get; }
            public string DefaultWeaponSlug { get; }

            public VisualDefinition(
                string prefabName,
                string modelPath,
                string materialPath,
                KayKitRigProfile rigProfile,
                string species,
                string defaultWeaponSlug)
            {
                PrefabName = prefabName;
                ModelPath = modelPath;
                MaterialPath = materialPath;
                RigProfile = rigProfile;
                Species = species;
                DefaultWeaponSlug = defaultWeaponSlug;
            }
        }
    }
}
