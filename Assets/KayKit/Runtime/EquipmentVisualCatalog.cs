using System;
using System.Collections.Generic;
using Game.Creature;
using UnityEngine;

namespace Game.KayKit
{
    public enum EquipmentSocket
    {
        None,
        RightHand,
        LeftHand,
        Back,
        Quiver
    }

    [Serializable]
    public sealed class EquipmentVisualAttachment
    {
        [SerializeField] private GameObject accessoryPrefab;
        [SerializeField] private Material material;
        [SerializeField] private EquipmentSocket socket;
        [SerializeField] private Vector3 localPosition;
        [SerializeField] private Vector3 localEulerAngles;
        [SerializeField] private Vector3 localScale = Vector3.one;

        public GameObject AccessoryPrefab => accessoryPrefab;
        public Material Material => material;
        public EquipmentSocket Socket => socket;
        public Vector3 LocalPosition => localPosition;
        public Vector3 LocalEulerAngles => localEulerAngles;
        public Vector3 LocalScale => localScale;

        public EquipmentVisualAttachment(
            GameObject accessoryPrefab,
            Material material,
            EquipmentSocket socket,
            Vector3 localPosition,
            Vector3 localEulerAngles,
            Vector3 localScale)
        {
            this.accessoryPrefab = accessoryPrefab;
            this.material = material;
            this.socket = socket;
            this.localPosition = localPosition;
            this.localEulerAngles = localEulerAngles;
            this.localScale = localScale;
        }
    }

    [Serializable]
    public sealed class EquipmentVisualCatalogEntry
    {
        [SerializeField] private string id;
        [SerializeField] private string itemSlug;
        [SerializeField] private string species;
        [SerializeField] private string fallbackGroup;
        [SerializeField] private int fallbackHands = -1;
        [SerializeField] private int fallbackRange = -1;
        [SerializeField] private AnimationStyle animationStyle;
        [SerializeField] private List<EquipmentVisualAttachment> attachments = new();

        public string Id => id;
        public string ItemSlug => itemSlug;
        public string Species => species;
        public string FallbackGroup => fallbackGroup;
        public int FallbackHands => fallbackHands;
        public int FallbackRange => fallbackRange;
        public AnimationStyle AnimationStyle => animationStyle;
        public IReadOnlyList<EquipmentVisualAttachment> Attachments => attachments;

        public EquipmentVisualCatalogEntry(
            string id,
            string itemSlug,
            string species,
            string fallbackGroup,
            int fallbackHands,
            int fallbackRange,
            AnimationStyle animationStyle,
            IEnumerable<EquipmentVisualAttachment> attachments)
        {
            this.id = id;
            this.itemSlug = itemSlug;
            this.species = species;
            this.fallbackGroup = fallbackGroup;
            this.fallbackHands = fallbackHands;
            this.fallbackRange = fallbackRange;
            this.animationStyle = animationStyle;
            this.attachments = attachments == null
                ? new List<EquipmentVisualAttachment>()
                : new List<EquipmentVisualAttachment>(attachments);
        }
    }

    [CreateAssetMenu(menuName = "KayKit/Equipment Visual Catalog", fileName = "EquipmentVisualCatalog")]
    public sealed class EquipmentVisualCatalog : ScriptableObject
    {
        public const string UnarmedSlug = "unarmed";

        [SerializeField] private List<EquipmentVisualCatalogEntry> entries = new();

        public IReadOnlyList<EquipmentVisualCatalogEntry> Entries => entries;

        public bool TryResolve(
            EquipmentWeapon weapon,
            string species,
            out EquipmentVisualCatalogEntry entry)
        {
            if (weapon == null || string.IsNullOrWhiteSpace(weapon.name))
                return TryResolveSlug(UnarmedSlug, species, out entry);

            string slug = NormalizeSlug(weapon.name);
            string normalizedSpecies = NormalizeSlug(species);

            entry = FindExact(slug, normalizedSpecies) ?? FindExact(slug, string.Empty);
            if (entry != null)
                return true;

            entry = FindFallback(weapon, normalizedSpecies) ?? FindFallback(weapon, string.Empty);
            return entry != null;
        }

        public bool TryResolveSlug(
            string itemSlug,
            string species,
            out EquipmentVisualCatalogEntry entry)
        {
            string slug = NormalizeSlug(itemSlug);
            string normalizedSpecies = NormalizeSlug(species);
            entry = FindExact(slug, normalizedSpecies) ?? FindExact(slug, string.Empty);
            return entry != null;
        }

        public static string NormalizeSlug(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            char[] buffer = value.Trim().ToLowerInvariant().ToCharArray();
            for (int i = 0; i < buffer.Length; i++)
            {
                if (!char.IsLetterOrDigit(buffer[i]))
                    buffer[i] = '-';
            }

            return string.Join("-", new string(buffer)
                .Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private EquipmentVisualCatalogEntry FindExact(string slug, string species)
        {
            foreach (EquipmentVisualCatalogEntry candidate in entries)
            {
                if (candidate == null)
                    continue;
                if (NormalizeSlug(candidate.ItemSlug) == slug &&
                    NormalizeSlug(candidate.Species) == species)
                    return candidate;
            }

            return null;
        }

        private EquipmentVisualCatalogEntry FindFallback(EquipmentWeapon weapon, string species)
        {
            string group = NormalizeSlug(weapon.group);
            foreach (EquipmentVisualCatalogEntry candidate in entries)
            {
                if (candidate == null || !string.IsNullOrWhiteSpace(candidate.ItemSlug) ||
                    NormalizeSlug(candidate.Species) != species)
                    continue;
                if (!string.IsNullOrWhiteSpace(candidate.FallbackGroup) &&
                    NormalizeSlug(candidate.FallbackGroup) != group)
                    continue;
                if (candidate.FallbackHands >= 0 && candidate.FallbackHands != weapon.hands)
                    continue;
                bool ranged = weapon.range > 0;
                if (candidate.FallbackRange >= 0 && (candidate.FallbackRange > 0) != ranged)
                    continue;
                return candidate;
            }

            return null;
        }

#if UNITY_EDITOR
        public void ReplaceEntries(IEnumerable<EquipmentVisualCatalogEntry> replacement)
        {
            entries = replacement == null
                ? new List<EquipmentVisualCatalogEntry>()
                : new List<EquipmentVisualCatalogEntry>(replacement);
        }
#endif
    }
}
