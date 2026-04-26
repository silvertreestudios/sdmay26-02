using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.Animations;
using System;

namespace GridPrivate
{
    public class TokenMovement : SingletonMonoBehaviour<TokenMovement>
    {
        // Jump points for the piece to move between
        [SerializeField]
        protected float StepHeight;
        [SerializeField]
        protected float MaxRotation;
        [SerializeField]
        protected AnimationCurve PtLerp;
        [SerializeField]
        protected AnimationCurve YLerp;
        [SerializeField]
        protected float JumpTime;

        protected bool IsRotating;
        protected bool IsMoving;
        protected float CurrentTime;
        protected Transform Token;
        protected Vector3 StartPoint;
        protected Vector3 EndPoint;
        protected Vector3 Direction;

        public void LookAt(Vector3 target, Transform token)
        {
            IsRotating = true;
            Token = token;
            Direction = (target - token.position).normalized;
        }

        public IEnumerator Hop(Transform token, Vector3 next)
        {
            if (IsMoving)
                yield break;
            IsMoving = true;
            Token = token;
            CurrentTime = 0.0f;
            EndPoint = next;
            StartPoint = token.position;
            Direction = (EndPoint - StartPoint).normalized;
            yield return new WaitUntil(() => Token == null);
        }

        public void Update()
        {
            if (IsMoving)
            {
                // Update the current time
                CurrentTime += Time.deltaTime;
                float time = Mathf.Clamp01(CurrentTime / JumpTime);
                //-------------MOVEMENT CALCULATIONS----------------//
                // Calculate the new position using the animation curves
                Vector3 position = Vector3.Lerp(StartPoint, EndPoint, PtLerp.Evaluate(time));
                position.y += StepHeight * YLerp.Evaluate(time);
                // Apply the new position and rotation
                Token.position = position;
                //-------------ROTATION CALCULATIONS----------------//
                // Tilt forward during jump
                Vector3 tiltEuler = new Vector3(
                    MaxRotation * YLerp.Evaluate(time),
                    0,
                    0
                );
                //Look towards movement direction
                Quaternion lookRotation = Quaternion.LookRotation(Direction);
                //convert tilt euler to quaternion
                Quaternion tiltRotation = Quaternion.Euler(tiltEuler);
                //combine the two rotations
                Quaternion finalRotation = lookRotation * tiltRotation;
                //apply the rotation smoothly
                Token.rotation = Quaternion.Slerp(Token.rotation, finalRotation, Time.deltaTime * 20f);
                //--------------------------------------------------//
                // If the jump is complete
                if (time >= 1.0f)
                {
                    //trigger step audio
                    OnStepEnd.Invoke(Token.position);
                    // Cleanup
                    IsMoving = false;
                    Token = null;
                }
            }   
            if(IsRotating)
            {
                CurrentTime += Time.deltaTime;
                Quaternion lookRotation = Quaternion.LookRotation(Direction);
                Token.rotation = Quaternion.Slerp(Token.rotation, lookRotation, Time.deltaTime * 20f);
                if(CurrentTime >= 1.0f)
                {
                    IsRotating = false;
                    Token = null;
                }
            }
        }
    }
}
