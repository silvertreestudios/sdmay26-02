using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Game.KayKit.Editor
{
    public enum KayKitClipSemantics
    {
        Ambiguous,
        Loop,
        OneShot,
        SetupPose,
    }

    public static class KayKitPathUtility
    {
        public const string VendorRoot = "Assets/ThirdParty/KayKit";
        public const string DungeonRoot = VendorRoot + "/DungeonRemastered_1.1";
        public const string AdventurersRoot = VendorRoot + "/Adventurers_2.0";
        public const string SkeletonsRoot = VendorRoot + "/Skeletons_1.1";
        public const string AnimationsRoot = VendorRoot + "/CharacterAnimations_1.1";

        public static readonly string[] PackRoots =
        {
            DungeonRoot,
            AdventurersRoot,
            SkeletonsRoot,
            AnimationsRoot,
        };

        private static readonly HashSet<string> LoopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "idle",
            "idling",
            "walk",
            "walking",
            "run",
            "running",
            "sprint",
            "sprinting",
            "crawl",
            "crawling",
            "sneak",
            "sneaking",
            "crouch_idle",
            "swim",
            "swimming",
            "block_idle",
            "aim",
            "aiming",
            "holding",
            "blocking",
            "shooting",
            "crouching",
            "push_ups",
            "inactive_floor_pose",
            "inactive_standing_pose",
            "chopping",
            "digging",
            "fishing_idle",
            "fishing_reeling",
            "fishing_struggling",
            "hammering",
            "lockpicking",
            "pickaxing",
            "sawing",
            "working",
        };

        private static readonly string[] OneShotWords =
        {
            "attack",
            "hit",
            "hurt",
            "damage",
            "death",
            "defeat",
            "die",
            "spawn",
            "cast",
            "spell",
            "shoot",
            "reload",
            "dodge",
            "roll",
            "dash",
            "jump",
            "hop",
            "land",
            "fall",
            "interact",
            "pickup",
            "pick_up",
            "throw",
            "wave",
            "cheer",
            "sit",
            "stand",
            "lie",
            "knock",
            "open",
            "close",
            "equip",
            "unequip",
            "use",
            "drink",
            "eat",
            "block",
            "draw",
            "release",
            "raise",
            "spellcasting",
            "summon",
            "cheering",
            "waving",
            "transform",
            "awaken",
            "taunt",
            "chop",
            "dig",
            "fishing_bite",
            "fishing_cast",
            "fishing_catch",
            "fishing_tug",
            "hammer",
            "lockpick",
            "pickaxe",
            "saw",
            "work",
        };

        public static string Normalize(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace((char)92, '/');
        }

        public static bool IsVendorAsset(string path)
        {
            string normalized = Normalize(path);
            return normalized.StartsWith(VendorRoot + "/", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsDungeonModel(string path)
        {
            return IsUnder(path, DungeonRoot)
                && string.Equals(
                    Path.GetExtension(path),
                    ".fbx",
                    StringComparison.OrdinalIgnoreCase
                );
        }

        public static bool IsAnimationSource(string path)
        {
            return IsUnder(path, AnimationsRoot)
                && string.Equals(
                    Path.GetExtension(path),
                    ".fbx",
                    StringComparison.OrdinalIgnoreCase
                );
        }

        public static bool IsCharacterModel(string path)
        {
            string normalized = Normalize(path);
            bool characterPack =
                IsUnder(normalized, AdventurersRoot) || IsUnder(normalized, SkeletonsRoot);
            return characterPack
                && string.Equals(
                    Path.GetExtension(normalized),
                    ".fbx",
                    StringComparison.OrdinalIgnoreCase
                )
                && normalized.IndexOf("/Characters/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string GetDungeonId(string path)
        {
            if (!IsDungeonModel(path))
                throw new ArgumentException($"Not a Dungeon Remastered FBX: {path}", nameof(path));

            return "dungeon/"
                + WithoutExtension(GetRelativePath(path, DungeonRoot)).ToLowerInvariant();
        }

        public static string GetAnimationCategory(string path)
        {
            if (!IsAnimationSource(path))
                throw new ArgumentException(
                    $"Not a Character Animations FBX: {path}",
                    nameof(path)
                );

            string fileName = Path.GetFileNameWithoutExtension(Normalize(path));
            const string prefix = "Rig_Medium_";
            if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return fileName.Substring(prefix.Length);

            string relative = GetRelativePath(path, AnimationsRoot);
            string directory = Path.GetDirectoryName(relative)?.Replace((char)92, '/');
            return string.IsNullOrEmpty(directory) ? fileName : directory.Split('/').Last();
        }

        public static string GetAnimationId(string sourcePath, string clipName)
        {
            string category = GetAnimationCategory(sourcePath);
            return $"animation/{Slug(category)}/{Slug(clipName)}";
        }

        public static string GetStableAssetId(string path)
        {
            string normalized = Normalize(path);
            string root = PackRoots.FirstOrDefault(candidate => IsUnder(normalized, candidate));
            if (root == null)
                throw new ArgumentException($"Not a known KayKit pack asset: {path}", nameof(path));

            string pack =
                root == DungeonRoot ? "dungeon"
                : root == AdventurersRoot ? "adventurers"
                : root == SkeletonsRoot ? "skeletons"
                : "animations";
            return pack + "/" + GetRelativePath(normalized, root).ToLowerInvariant();
        }

        public static string GetRelativePath(string path, string root)
        {
            string normalized = Normalize(path);
            string normalizedRoot = Normalize(root).TrimEnd('/');
            if (!IsUnder(normalized, normalizedRoot))
                throw new ArgumentException($"{path} is not beneath {root}.", nameof(path));

            return normalized.Substring(normalizedRoot.Length + 1);
        }

        public static KayKitClipSemantics ClassifyClip(string clipName)
        {
            string slug = Slug(clipName);
            if (slug is "t_pose" or "tpose" || slug.Contains("_t_pose") || slug.Contains("_tpose"))
                return KayKitClipSemantics.SetupPose;

            if (slug.Contains("_cycle") || LoopWords.Any(word => MatchesAction(slug, word)))
                return KayKitClipSemantics.Loop;

            if (OneShotWords.Any(word => MatchesAction(slug, word)))
                return KayKitClipSemantics.OneShot;

            return KayKitClipSemantics.Ambiguous;
        }

        public static string Slug(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            char[] normalized = value
                .Trim()
                .ToLowerInvariant()
                .Select(character => char.IsLetterOrDigit(character) ? character : '_')
                .ToArray();
            return string.Join(
                "_",
                new string(normalized).Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries)
            );
        }

        private static bool MatchesAction(string slug, string action)
        {
            return slug == action
                || slug.StartsWith(action + "_", StringComparison.Ordinal)
                || slug.EndsWith("_" + action, StringComparison.Ordinal)
                || slug.Contains("_" + action + "_");
        }

        private static bool IsUnder(string path, string root)
        {
            string normalized = Normalize(path);
            string normalizedRoot = Normalize(root).TrimEnd('/');
            return normalized.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string WithoutExtension(string path)
        {
            string normalized = Normalize(path);
            string extension = Path.GetExtension(normalized);
            return string.IsNullOrEmpty(extension)
                ? normalized
                : normalized.Substring(0, normalized.Length - extension.Length);
        }
    }
}
