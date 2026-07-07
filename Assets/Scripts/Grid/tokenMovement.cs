using System.Collections;
using UnityEngine;

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
                CurrentTime += Time.deltaTime;
                float time = Mathf.Clamp01(CurrentTime / JumpTime);
                Vector3 position = Vector3.Lerp(StartPoint, EndPoint, PtLerp.Evaluate(time));
                position.y += StepHeight * YLerp.Evaluate(time);
                Token.position = position;
                // Tilt forward during jump
                Vector3 tiltEuler = new Vector3(
                    MaxRotation * YLerp.Evaluate(time),
                    0,
                    0
                );
                // Look towards movement direction.
                Quaternion lookRotation = Quaternion.LookRotation(Direction);
                Quaternion tiltRotation = Quaternion.Euler(tiltEuler);
                Quaternion finalRotation = lookRotation * tiltRotation;
                Token.rotation = Quaternion.Slerp(Token.rotation, finalRotation, Time.deltaTime * 20f);
                if (time >= 1.0f)
                {
                    OnStepEnd.Invoke(Token.position);
                    IsMoving = false;
                    Token = null;
                }
            }

            if (IsRotating)
            {
                CurrentTime += Time.deltaTime;
                Quaternion lookRotation = Quaternion.LookRotation(Direction);
                Token.rotation = Quaternion.Slerp(Token.rotation, lookRotation, Time.deltaTime * 20f);
                if (CurrentTime >= 1.0f)
                {
                    IsRotating = false;
                    Token = null;
                }
            }
        }
    }
}
