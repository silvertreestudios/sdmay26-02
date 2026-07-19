using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Game.KayKit
{
    public sealed class CreatureAnimationController : MonoBehaviour
    {
        public const string IdleParameter = "Moving";
        public const string SpeedParameter = "MoveSpeed";
        public const string ActionLoopParameter = "ActionLoop";
        public const string ActionTrigger = "Action";
        public const string DeathTrigger = "Death";

        public const string IdlePlaceholderClipName = "Idle_A";
        public const string WalkPlaceholderClipName = "Walking_A";
        public const string ActionPlaceholderClipName = "Melee_Unarmed_Attack_Punch_A";
        public const string DeathPlaceholderClipName = "Death_A";

        private const string AdventurerIdleId = "animation/general/idle_a";
        private const string AdventurerWalkId = "animation/movementbasic/walking_a";
        private const string AdventurerHitId = "animation/general/hit_a";
        private const string AdventurerDeathId = "animation/general/death_a";
        private const string SkeletonIdleId = "animation/special/skeletons_idle";
        private const string SkeletonWalkId = "animation/special/skeletons_walking";
        private const string SkeletonDeathId = "animation/special/skeletons_death";

        private static readonly IReadOnlyDictionary<AnimationStyle, string> AttackClipIds =
            new Dictionary<AnimationStyle, string>
            {
                { AnimationStyle.Unarmed, "animation/combatmelee/melee_unarmed_attack_punch_a" },
                {
                    AnimationStyle.OneHandMelee,
                    "animation/combatmelee/melee_1h_attack_slice_horizontal"
                },
                { AnimationStyle.TwoHandMelee, "animation/combatmelee/melee_2h_attack_chop" },
                { AnimationStyle.Bow, "animation/combatranged/ranged_bow_release" },
                { AnimationStyle.OneHandRanged, "animation/combatranged/ranged_1h_shoot" },
                { AnimationStyle.TwoHandRanged, "animation/combatranged/ranged_2h_shoot" },
                { AnimationStyle.Magic, "animation/combatranged/ranged_magic_shoot" },
                { AnimationStyle.Tool, "animation/tools/chop" },
            };

        [SerializeField]
        private Animator animator;

        [SerializeField]
        private RuntimeAnimatorController sharedController;

        [SerializeField]
        private KayKitAnimationLibrary animationLibrary;

        [SerializeField]
        private KayKitRigProfile rigProfile;

        private readonly HashSet<string> warnedUnknownIds = new(StringComparer.OrdinalIgnoreCase);
        private AnimatorOverrideController overrideController;
        private Coroutine actionRoutine;
        private bool moving;
        private bool deathPlaying;
        private int playbackVersion;

        public Animator Animator => animator;
        public KayKitAnimationLibrary AnimationLibrary => animationLibrary;
        public KayKitRigProfile RigProfile => rigProfile;
        public bool IsMoving => moving;
        public bool IsActionPlaying { get; private set; }
        public bool IsDeathPlaying => deathPlaying;
        public string CurrentClipId { get; private set; }
        public bool HasDeathClip => TryGetClip(GetDeathClipId(), out _);

        private void Awake()
        {
            Initialize();
        }

        private void OnDisable()
        {
            moving = false;
            if (
                animator != null
                && animator.isInitialized
                && animator.runtimeAnimatorController != null
            )
                animator.SetBool(IdleParameter, false);
        }

        private void OnDestroy()
        {
            DisposeOverrideController();
        }

        public void Configure(
            Animator targetAnimator,
            RuntimeAnimatorController controller,
            KayKitAnimationLibrary library,
            KayKitRigProfile profile
        )
        {
            animator = targetAnimator;
            sharedController = controller;
            animationLibrary = library;
            rigProfile = profile;
            if (animator != null)
            {
                animator.applyRootMotion = false;
                if (Application.isPlaying)
                    Initialize();
                else
                    animator.runtimeAnimatorController = controller;
            }
        }

        public void SetMoving(bool isMoving, float speed)
        {
            moving = isMoving;
            if (
                animator == null
                || !animator.isInitialized
                || animator.runtimeAnimatorController == null
            )
                return;

            animator.applyRootMotion = false;
            animator.SetBool(IdleParameter, isMoving);
            animator.SetFloat(SpeedParameter, Mathf.Clamp(speed / 25.0f, 0.1f, 3.0f));
        }

        public void PlayAttack(AnimationStyle style)
        {
            if (AttackClipIds.TryGetValue(style, out string clipId))
                PlayClip(clipId);
        }

        public void PlayHit()
        {
            PlayClip(AdventurerHitId);
        }

        public void PlayDeath(Action completed)
        {
            string deathClipId = GetDeathClipId();
            if (!TryGetClip(deathClipId, out KayKitAnimationEntry entry) || !EnsureInitialized())
            {
                completed?.Invoke();
                return;
            }

            playbackVersion++;
            if (actionRoutine != null)
                StopCoroutine(actionRoutine);
            actionRoutine = null;
            deathPlaying = true;
            IsActionPlaying = false;
            CurrentClipId = deathClipId;
            ApplyOverride(DeathPlaceholderClipName, entry.Clip);
            animator.ResetTrigger(ActionTrigger);
            animator.SetTrigger(DeathTrigger);
            float delay = Mathf.Min(Mathf.Max(0.0f, entry.Duration) + 0.25f, 5.0f);
            actionRoutine = StartCoroutine(CompleteDeathAfter(delay, completed, playbackVersion));
        }

        public bool PlayClip(string clipId)
        {
            if (!TryGetClip(clipId, out KayKitAnimationEntry entry))
            {
                WarnUnknownClipOnce(clipId);
                return false;
            }
            if (!EnsureInitialized())
                return false;

            playbackVersion++;
            if (actionRoutine != null)
                StopCoroutine(actionRoutine);
            actionRoutine = null;
            deathPlaying = false;
            IsActionPlaying = true;
            CurrentClipId = entry.Id;
            ApplyOverride(ActionPlaceholderClipName, entry.Clip);
            animator.SetBool(ActionLoopParameter, entry.Loop);
            animator.ResetTrigger(DeathTrigger);
            animator.SetTrigger(ActionTrigger);

            if (!entry.Loop)
                actionRoutine = StartCoroutine(StopActionAfter(entry.Duration, playbackVersion));
            return true;
        }

        public void StopAction()
        {
            playbackVersion++;
            if (actionRoutine != null)
                StopCoroutine(actionRoutine);
            actionRoutine = null;
            IsActionPlaying = false;
            deathPlaying = false;
            CurrentClipId = null;

            if (animator == null)
                return;
            animator.ResetTrigger(ActionTrigger);
            animator.SetBool(ActionLoopParameter, false);
            string state = moving ? "Base Layer.Walk" : "Base Layer.Idle";
            animator.CrossFade(state, 0.05f);
        }

        public static IReadOnlyCollection<string> RequiredDefaultClipIds()
        {
            List<string> ids = new()
            {
                AdventurerIdleId,
                AdventurerWalkId,
                AdventurerHitId,
                AdventurerDeathId,
                SkeletonIdleId,
                SkeletonWalkId,
                SkeletonDeathId,
            };
            ids.AddRange(AttackClipIds.Values);
            return ids;
        }

        private void Initialize()
        {
            if (animator == null || sharedController == null || animationLibrary == null)
                return;

            animator.applyRootMotion = false;
            DisposeOverrideController();
            overrideController = new AnimatorOverrideController(sharedController);
            animator.runtimeAnimatorController = overrideController;
            ApplyLocomotionOverrides();
            animator.SetBool(IdleParameter, moving);
            animator.SetFloat(SpeedParameter, 1.0f);
        }

        private bool EnsureInitialized()
        {
            if (overrideController == null)
                Initialize();
            return animator != null && overrideController != null && animationLibrary != null;
        }

        private void ApplyLocomotionOverrides()
        {
            string idleId =
                rigProfile == KayKitRigProfile.Skeleton ? SkeletonIdleId : AdventurerIdleId;
            string walkId =
                rigProfile == KayKitRigProfile.Skeleton ? SkeletonWalkId : AdventurerWalkId;
            if (TryGetClip(idleId, out KayKitAnimationEntry idle))
                ApplyOverride(IdlePlaceholderClipName, idle.Clip);
            if (TryGetClip(walkId, out KayKitAnimationEntry walk))
                ApplyOverride(WalkPlaceholderClipName, walk.Clip);
        }

        private void ApplyOverride(string placeholderName, AnimationClip clip)
        {
            if (overrideController != null && clip != null)
                overrideController[placeholderName] = clip;
        }

        private bool TryGetClip(string clipId, out KayKitAnimationEntry entry)
        {
            if (animationLibrary != null)
                return animationLibrary.TryGet(clipId, out entry);
            entry = null;
            return false;
        }

        private string GetDeathClipId()
        {
            return rigProfile == KayKitRigProfile.Skeleton ? SkeletonDeathId : AdventurerDeathId;
        }

        private IEnumerator StopActionAfter(float duration, int version)
        {
            yield return new WaitForSeconds(Mathf.Max(0.0f, duration));
            if (version == playbackVersion && !deathPlaying)
                StopAction();
        }

        private IEnumerator CompleteDeathAfter(float delay, Action completed, int version)
        {
            yield return new WaitForSeconds(delay);
            if (version == playbackVersion && deathPlaying)
            {
                actionRoutine = null;
                completed?.Invoke();
            }
        }

        private void WarnUnknownClipOnce(string clipId)
        {
            string warningKey = string.IsNullOrWhiteSpace(clipId) ? "<empty>" : clipId;
            if (!warnedUnknownIds.Add(warningKey))
                return;
            if (Application.isEditor || Debug.isDebugBuild)
                Debug.LogWarning(
                    $"Unknown KayKit animation clip ID '{warningKey}' on {name}.",
                    this
                );
        }

        private void DisposeOverrideController()
        {
            if (overrideController == null)
                return;
            if (animator != null && animator.runtimeAnimatorController == overrideController)
                animator.runtimeAnimatorController = sharedController;
            if (Application.isPlaying)
                Destroy(overrideController);
            else
                DestroyImmediate(overrideController);
            overrideController = null;
        }
    }
}
