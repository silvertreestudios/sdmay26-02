using System;
using Game.Creature;
using UnityEngine;

namespace Game.KayKit
{
    public sealed class CreaturePresentation : MonoBehaviour
    {
        public CreatureAnimationController AnimationController { get; private set; }
        public CreatureEquipmentVisuals EquipmentVisuals { get; private set; }

        public void Bind(CreatureAnimationController animationController, CreatureEquipmentVisuals equipmentVisuals)
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

        public void PlayAttack(AnimationStyle style)
        {
            if (style == AnimationStyle.Unarmed)
                EquipmentVisuals?.SetActiveWeapon(null);
            AnimationController?.PlayAttack(style);
        }

        public void PlayAttack(EquipmentWeapon weapon)
        {
            EquipmentVisuals?.SetActiveWeapon(weapon);
            AnimationStyle style = EquipmentVisuals != null
                ? EquipmentVisuals.GetAnimationStyle(weapon)
                : AnimationStyle.Unarmed;
            AnimationController?.PlayAttack(style);
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
    }
}
