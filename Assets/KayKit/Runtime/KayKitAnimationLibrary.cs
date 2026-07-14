using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.KayKit
{
    [Serializable]
    public sealed class KayKitAnimationEntry
    {
        [SerializeField] private string id;
        [SerializeField] private string sourceCategory;
        [SerializeField] private AnimationClip clip;
        [SerializeField] private bool loop;
        [SerializeField] private float duration;

        public string Id => id;
        public string SourceCategory => sourceCategory;
        public AnimationClip Clip => clip;
        public bool Loop => loop;
        public float Duration => duration;

        public KayKitAnimationEntry(
            string id,
            string sourceCategory,
            AnimationClip clip,
            bool loop,
            float duration)
        {
            this.id = id;
            this.sourceCategory = sourceCategory;
            this.clip = clip;
            this.loop = loop;
            this.duration = duration;
        }
    }

    [CreateAssetMenu(menuName = "KayKit/Animation Library", fileName = "KayKitAnimationLibrary")]
    public sealed class KayKitAnimationLibrary : ScriptableObject
    {
        [SerializeField] private List<KayKitAnimationEntry> entries = new();

        public IReadOnlyList<KayKitAnimationEntry> Entries => entries;

#if UNITY_EDITOR
        public void ReplaceEntries(IEnumerable<KayKitAnimationEntry> replacement)
        {
            entries = new List<KayKitAnimationEntry>(replacement);
        }
#endif
    }
}
