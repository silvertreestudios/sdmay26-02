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
        protected bool UseHop;

        public void LookAt(Vector3 target, Transform token)
        {
            IsRotating = true;
            Token = token;
            Direction = (target - token.position).normalized;
        }

        public IEnumerator Hop(Transform token, Vector3 next)
        {
            yield return BeginMovement(token, next, true);
        }

        public IEnumerator Walk(Transform token, Vector3 next)
        {
            yield return BeginMovement(token, next, false);
        }

        private IEnumerator BeginMovement(Transform token, Vector3 next, bool useHop)
        {
            if (IsMoving)
                yield break;

            IsMoving = true;
            Token = token;
            CurrentTime = 0.0f;
            EndPoint = next;
            StartPoint = token.position;
            Direction = (EndPoint - StartPoint).normalized;
            UseHop = useHop;
            yield return new WaitUntil(() => Token == null);
        }

        public void Update()
        {
            if (IsMoving)
            {
                CurrentTime += Time.deltaTime;
                float time = Mathf.Clamp01(CurrentTime / JumpTime);
                Vector3 position = Vector3.Lerp(StartPoint, EndPoint, PtLerp.Evaluate(time));
                if (UseHop)
                    position.y += StepHeight * YLerp.Evaluate(time);
                Token.position = position;
                // Look towards movement direction.
                Quaternion lookRotation = Quaternion.LookRotation(Direction);
                Quaternion finalRotation = lookRotation;
                if (UseHop)
                {
                    Vector3 tiltEuler = new Vector3(
                        MaxRotation * YLerp.Evaluate(time),
                        0,
                        0
                    );
                    finalRotation *= Quaternion.Euler(tiltEuler);
                }
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
