using System;
using System.Collections.Generic;
using Game.Creature;
using UnityEngine;

namespace Game.KayKit
{
    public sealed class CreatureEquipmentVisuals : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private EquipmentVisualCatalog catalog;
        [SerializeField] private string species;
        [SerializeField] private string defaultWeaponSlug = EquipmentVisualCatalog.UnarmedSlug;
        [SerializeField] private Transform backSocket;
        [SerializeField] private Transform quiverSocket;

        private readonly List<GameObject> accessoryInstances = new();
        private readonly HashSet<string> warnedMappings = new(StringComparer.OrdinalIgnoreCase);
        private CreatureComponent creature;
        private EquipmentWeapon activeWeapon;
        private bool hasActiveWeaponOverride;

        public EquipmentWeapon ActiveWeapon => activeWeapon;
        public EquipmentVisualCatalogEntry CurrentEntry { get; private set; }
        public int AccessoryInstanceCount => accessoryInstances.Count;
        public string Species => species;

        private void OnEnable()
        {
            BindCreature(GetComponentInParent<CreatureComponent>());
            Refresh();
        }

        private void OnDisable()
        {
            BindCreature(null);
            ClearActiveWeaponOverride();
            ClearAccessories();
        }

        public void Configure(
            Animator targetAnimator,
            EquipmentVisualCatalog equipmentCatalog,
            string targetSpecies,
            string initialWeaponSlug,
            Transform targetBackSocket,
            Transform targetQuiverSocket)
        {
            animator = targetAnimator;
            catalog = equipmentCatalog;
            species = targetSpecies;
            defaultWeaponSlug = string.IsNullOrWhiteSpace(initialWeaponSlug)
                ? EquipmentVisualCatalog.UnarmedSlug
                : initialWeaponSlug;
            backSocket = targetBackSocket;
            quiverSocket = targetQuiverSocket;
            BindCreature(GetComponentInParent<CreatureComponent>());
            if (Application.isPlaying && isActiveAndEnabled)
                Refresh();
        }

        public void SetActiveWeapon(EquipmentWeapon weapon)
        {
            activeWeapon = weapon;
            hasActiveWeaponOverride = true;
            Refresh();
        }

        public AnimationStyle GetAnimationStyle(EquipmentWeapon weapon)
        {
            if (catalog != null && catalog.TryResolve(weapon, species, out EquipmentVisualCatalogEntry entry))
                return entry.AnimationStyle;

            return InferStyle(weapon);
        }

        public void Refresh()
        {
            ClearAccessories();
            CurrentEntry = null;
            if (catalog == null)
                return;

            EquipmentWeapon displayedWeapon = SelectDisplayedWeapon();
            EquipmentVisualCatalogEntry entry;
            bool resolved = hasActiveWeaponOverride && displayedWeapon == null
                ? catalog.TryResolveSlug(EquipmentVisualCatalog.UnarmedSlug, species, out entry)
                : displayedWeapon != null
                ? catalog.TryResolve(displayedWeapon, species, out entry)
                : catalog.TryResolveSlug(defaultWeaponSlug, species, out entry);

            if (!resolved || entry == null)
            {
                WarnUnresolvedOnce(displayedWeapon != null ? displayedWeapon.name : defaultWeaponSlug);
                return;
            }

            CurrentEntry = entry;
            foreach (EquipmentVisualAttachment attachment in entry.Attachments)
                CreateAttachment(attachment);
        }

        private void BindCreature(CreatureComponent target)
        {
            if (creature == target)
                return;
            if (creature != null)
                creature.EquipmentChanged -= HandleEquipmentChanged;
            creature = target;
            if (creature != null)
                creature.EquipmentChanged += HandleEquipmentChanged;
        }

        private void HandleEquipmentChanged()
        {
            ClearActiveWeaponOverride();
            Refresh();
        }

        private void ClearActiveWeaponOverride()
        {
            activeWeapon = null;
            hasActiveWeaponOverride = false;
        }

        private EquipmentWeapon SelectDisplayedWeapon()
        {
            if (hasActiveWeaponOverride)
                return activeWeapon;
            if (creature == null)
                return null;
            if (HasName(creature.equippedRightHand))
                return creature.equippedRightHand;
            if (HasName(creature.equippedLeftHand))
                return creature.equippedLeftHand;
            return null;
        }

        private void CreateAttachment(EquipmentVisualAttachment attachment)
        {
            if (attachment == null || attachment.AccessoryPrefab == null)
                return;

            Transform socket = ResolveSocket(attachment.Socket);
            if (socket == null)
                return;

            GameObject instance = Instantiate(attachment.AccessoryPrefab, socket, false);
            instance.name = attachment.AccessoryPrefab.name + " (Equipment)";
            instance.transform.localPosition = attachment.LocalPosition;
            instance.transform.localRotation = Quaternion.Euler(attachment.LocalEulerAngles);
            instance.transform.localScale = attachment.LocalScale;
            CreaturePresentation.SetLayerRecursively(instance, gameObject.layer);
            if (attachment.Material != null)
            {
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterial = attachment.Material;
            }
            accessoryInstances.Add(instance);
        }

        private Transform ResolveSocket(EquipmentSocket socket)
        {
            if (animator == null)
                return null;
            switch (socket)
            {
                case EquipmentSocket.RightHand:
                    return animator.GetBoneTransform(HumanBodyBones.RightHand);
                case EquipmentSocket.LeftHand:
                    return animator.GetBoneTransform(HumanBodyBones.LeftHand);
                case EquipmentSocket.Back:
                    return backSocket;
                case EquipmentSocket.Quiver:
                    return quiverSocket;
                case EquipmentSocket.None:
                    return transform;
                default:
                    return null;
            }
        }

        private void ClearAccessories()
        {
            foreach (GameObject instance in accessoryInstances)
            {
                if (instance == null)
                    continue;
                instance.SetActive(false);
                if (Application.isPlaying)
                    Destroy(instance);
                else
                    DestroyImmediate(instance);
            }
            accessoryInstances.Clear();
        }

        private void WarnUnresolvedOnce(string item)
        {
            string key = string.IsNullOrWhiteSpace(item) ? "<empty>" : item;
            if (!warnedMappings.Add(key))
                return;
            if (Application.isEditor || Debug.isDebugBuild)
                Debug.LogWarning($"No KayKit equipment visual mapping resolved for '{key}' on {name}.", this);
        }

        private static bool HasName(EquipmentWeapon weapon)
        {
            return weapon != null && !string.IsNullOrWhiteSpace(weapon.name);
        }

        private static AnimationStyle InferStyle(EquipmentWeapon weapon)
        {
            if (weapon == null)
                return AnimationStyle.Unarmed;
            if (string.Equals(weapon.group, "bow", StringComparison.OrdinalIgnoreCase))
                return AnimationStyle.Bow;
            if (weapon.range > 0)
                return weapon.hands == 1 ? AnimationStyle.OneHandRanged : AnimationStyle.TwoHandRanged;
            return weapon.hands >= 2 ? AnimationStyle.TwoHandMelee : AnimationStyle.OneHandMelee;
        }
    }
}
