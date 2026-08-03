using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GridPrivate
{
    public class TokenMovement : SingletonMonoBehaviour<TokenMovement>
    {
        private readonly Dictionary<Transform, ExplorationMovementChannel> explorationChannels =
            new();
        private readonly List<Transform> completedExplorationChannels = new();

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

        internal ExplorationMovementOperation QueueExplorationWalk(Transform token, Vector3 next) =>
            QueueExplorationMovement(token, next, false);

        internal ExplorationMovementOperation QueueExplorationHop(Transform token, Vector3 next) =>
            QueueExplorationMovement(token, next, true);

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
                ApplyMovementPresentation(
                    Token,
                    StartPoint,
                    EndPoint,
                    Direction,
                    PtLerp.Evaluate(time),
                    time,
                    UseHop,
                    StepHeight,
                    MaxRotation,
                    YLerp,
                    Time.deltaTime
                );
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
                Token.rotation = Quaternion.Slerp(
                    Token.rotation,
                    lookRotation,
                    Time.deltaTime * 20f
                );
                if (CurrentTime >= 1.0f)
                {
                    IsRotating = false;
                    Token = null;
                }
            }

            AdvanceExplorationMovements(Time.deltaTime);
        }

        private static void ApplyMovementPresentation(
            Transform token,
            Vector3 start,
            Vector3 destination,
            Vector3 direction,
            float horizontalProgress,
            float verticalProgress,
            bool useHop,
            float stepHeight,
            float maxRotation,
            AnimationCurve verticalCurve,
            float deltaTime
        )
        {
            Vector3 position = Vector3.Lerp(start, destination, horizontalProgress);
            if (useHop)
                position.y += stepHeight * verticalCurve.Evaluate(verticalProgress);
            token.position = position;

            if (direction.sqrMagnitude <= Mathf.Epsilon)
                return;
            Quaternion rotation = Quaternion.LookRotation(direction);
            if (useHop)
            {
                rotation *= Quaternion.Euler(
                    new Vector3(maxRotation * verticalCurve.Evaluate(verticalProgress), 0, 0)
                );
            }
            token.rotation = Quaternion.Slerp(token.rotation, rotation, deltaTime * 20.0f);
        }

        /// <summary>
        /// Advances every independently queued exploration channel by a deterministic interval.
        /// </summary>
        /// <param name="deltaTime">The non-negative presentation interval in seconds.</param>
        protected void AdvanceExplorationMovements(float deltaTime)
        {
            completedExplorationChannels.Clear();
            if (explorationChannels.Count == 0)
                return;

            foreach (
                KeyValuePair<Transform, ExplorationMovementChannel> entry in explorationChannels
            )
            {
                ExplorationMovementChannel channel = entry.Value;
                if (channel.Token == null)
                {
                    channel.CancelAll();
                    completedExplorationChannels.Add(entry.Key);
                    continue;
                }

                channel.Advance(
                    Mathf.Max(0.0f, deltaTime),
                    JumpTime,
                    StepHeight,
                    MaxRotation,
                    YLerp
                );
                if (channel.IsEmpty)
                    completedExplorationChannels.Add(entry.Key);
            }

            foreach (Transform token in completedExplorationChannels)
                explorationChannels.Remove(token);
            completedExplorationChannels.Clear();
        }

        private ExplorationMovementOperation QueueExplorationMovement(
            Transform token,
            Vector3 destination,
            bool useHop
        )
        {
            if (token == null)
                return ExplorationMovementOperation.Completed;

            if (!explorationChannels.TryGetValue(token, out ExplorationMovementChannel channel))
            {
                channel = new ExplorationMovementChannel(token);
                explorationChannels.Add(token, channel);
            }

            ExplorationMovementOperation operation = new();
            channel.Enqueue(new ExplorationMovementSegment(destination, useHop, operation));
            return operation;
        }

        internal sealed class ExplorationMovementOperation : CustomYieldInstruction
        {
            internal static ExplorationMovementOperation Completed
            {
                get
                {
                    ExplorationMovementOperation operation = new();
                    operation.Complete();
                    return operation;
                }
            }

            internal bool IsCompleted { get; private set; }

            public override bool keepWaiting => !IsCompleted;

            internal void Complete() => IsCompleted = true;
        }

        private sealed class ExplorationMovementChannel
        {
            private readonly Queue<ExplorationMovementSegment> queued = new();
            private ExplorationMovementSegment active;
            private Vector3 startPoint;
            private Vector3 direction;
            private float currentTime;

            internal ExplorationMovementChannel(Transform token) => Token = token;

            internal Transform Token { get; }

            internal bool IsEmpty => active == null && queued.Count == 0;

            internal void Enqueue(ExplorationMovementSegment segment)
            {
                queued.Enqueue(segment);
                StartNextIfNeeded();
            }

            internal void Advance(
                float deltaTime,
                float duration,
                float stepHeight,
                float maxRotation,
                AnimationCurve verticalCurve
            )
            {
                StartNextIfNeeded();
                if (active == null)
                    return;

                currentTime += deltaTime;
                float time = duration <= 0.0f ? 1.0f : Mathf.Clamp01(currentTime / duration);
                ApplyMovementPresentation(
                    Token,
                    startPoint,
                    active.Destination,
                    direction,
                    time,
                    time,
                    active.UseHop,
                    stepHeight,
                    maxRotation,
                    verticalCurve,
                    deltaTime
                );

                if (time < 1.0f)
                    return;

                Token.position = active.Destination;
                OnStepEnd.Invoke(Token.position);
                active.Operation.Complete();
                active = null;
                StartNextIfNeeded();
            }

            internal void CancelAll()
            {
                active?.Operation.Complete();
                active = null;
                while (queued.Count > 0)
                    queued.Dequeue().Operation.Complete();
            }

            private void StartNextIfNeeded()
            {
                if (active != null || queued.Count == 0 || Token == null)
                    return;

                active = queued.Dequeue();
                startPoint = Token.position;
                direction = (active.Destination - startPoint).normalized;
                currentTime = 0.0f;
            }
        }

        private sealed class ExplorationMovementSegment
        {
            internal ExplorationMovementSegment(
                Vector3 destination,
                bool useHop,
                ExplorationMovementOperation operation
            )
            {
                Destination = destination;
                UseHop = useHop;
                Operation = operation;
            }

            internal Vector3 Destination { get; }

            internal bool UseHop { get; }

            internal ExplorationMovementOperation Operation { get; }
        }
    }
}
