using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.KayKit
{
    [Serializable]
    public sealed class KayKitSourceManifestEntry
    {
        [SerializeField]
        private string sourceUrl;

        [SerializeField]
        private string pack;

        [SerializeField]
        private string version;

        [SerializeField]
        private string downloadDate;

        [SerializeField]
        private string license;

        [SerializeField]
        private string relativeSourcePath;

        [SerializeField]
        private string stableId;

        [SerializeField]
        private UnityEngine.Object asset;

        public string SourceUrl => sourceUrl;
        public string Pack => pack;
        public string Version => version;
        public string DownloadDate => downloadDate;
        public string License => license;
        public string RelativeSourcePath => relativeSourcePath;
        public string StableId => stableId;
        public UnityEngine.Object Asset => asset;

        public KayKitSourceManifestEntry(
            string sourceUrl,
            string pack,
            string version,
            string downloadDate,
            string license,
            string relativeSourcePath,
            string stableId,
            UnityEngine.Object asset
        )
        {
            this.sourceUrl = sourceUrl;
            this.pack = pack;
            this.version = version;
            this.downloadDate = downloadDate;
            this.license = license;
            this.relativeSourcePath = relativeSourcePath;
            this.stableId = stableId;
            this.asset = asset;
        }
    }

    [CreateAssetMenu(menuName = "KayKit/Source Manifest", fileName = "KayKitSourceManifest")]
    public sealed class KayKitSourceManifest : ScriptableObject
    {
        [SerializeField]
        private List<KayKitSourceManifestEntry> entries = new();

        public IReadOnlyList<KayKitSourceManifestEntry> Entries => entries;

#if UNITY_EDITOR
        public void ReplaceEntries(IEnumerable<KayKitSourceManifestEntry> replacement)
        {
            entries = new List<KayKitSourceManifestEntry>(replacement);
        }
#endif
    }
}
