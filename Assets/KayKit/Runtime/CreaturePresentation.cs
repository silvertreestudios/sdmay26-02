using System;
using Game.Creature;
using UnityEngine;

namespace Game.KayKit
{
    public sealed class CreaturePresentation : MonoBehaviour
    {
        public CreatureAnimationController AnimationController { get; private set; }
        public CreatureEquipmentVisuals EquipmentVisuals { get; private set; }

        public void Bind(
            CreatureAnimationController animationController,
            CreatureEquipmentVisuals equipmentVisuals
        )
        {
            if (AnimationController != null && AnimationController != animationController)
                AnimationController.SetMoving(false, 0.0f);
            AnimationController = animationController;
            EquipmentVisuals = equipmentVisuals;
        }

        public void SetMoving(bool moving, float speed)
        {
            AnimationController?.SetMoving(moving, speed);
        }

        /// <summary>Starts an attack animation and updates the visible weapon state.</summary>
        /// <param name="style">The equipped or unarmed animation style.</param>
        /// <returns><see langword="true"/> when a usable animation started.</returns>
        public bool PlayAttack(AnimationStyle style)
        {
            if (style == AnimationStyle.Unarmed)
                EquipmentVisuals?.SetActiveWeapon(null);
            return AnimationController != null && AnimationController.PlayAttack(style);
        }

        /// <summary>Starts the attack animation selected for an equipped weapon.</summary>
        /// <param name="weapon">The weapon whose presentation selects the animation style.</param>
        /// <returns><see langword="true"/> when a usable animation started.</returns>
        public bool PlayAttack(EquipmentWeapon weapon)
        {
            EquipmentVisuals?.SetActiveWeapon(weapon);
            AnimationStyle style =
                EquipmentVisuals != null
                    ? EquipmentVisuals.GetAnimationStyle(weapon)
                    : AnimationStyle.Unarmed;
            return AnimationController != null && AnimationController.PlayAttack(style);
        }

        /// <summary>Faces a target position and starts a styled attack animation.</summary>
        /// <param name="style">The equipped or unarmed animation style.</param>
        /// <param name="targetPosition">The world position the creature should face.</param>
        /// <returns><see langword="true"/> when a usable animation started.</returns>
        public bool PlayAttack(AnimationStyle style, Vector3 targetPosition)
        {
            FaceTowards(targetPosition);
            return PlayAttack(style);
        }

        /// <summary>Faces a target position and starts an equipped-weapon attack animation.</summary>
        /// <param name="weapon">The weapon whose presentation selects the animation style.</param>
        /// <param name="targetPosition">The world position the creature should face.</param>
        /// <returns><see langword="true"/> when a usable animation started.</returns>
        public bool PlayAttack(EquipmentWeapon weapon, Vector3 targetPosition)
        {
            FaceTowards(targetPosition);
            return PlayAttack(weapon);
        }

        public bool FaceTowards(Vector3 targetPosition)
        {
            Vector3 direction = targetPosition - transform.position;
            direction.y = 0.0f;
            if (direction.sqrMagnitude <= 0.0001f)
                return false;
            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            return true;
        }

        public void PlayHit()
        {
            AnimationController?.PlayHit();
        }

        public bool PlayDeath(Action completed)
        {
            if (AnimationController == null || !AnimationController.HasDeathClip)
                return false;
            AnimationController.PlayDeath(completed);
            return true;
        }

        private void OnDisable()
        {
            AnimationController?.SetMoving(false, 0.0f);
        }

        internal static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null)
                return;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = layer;
        }
    }
}
