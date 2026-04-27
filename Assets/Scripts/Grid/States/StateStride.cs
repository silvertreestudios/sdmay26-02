using UnityEngine;
using System.Collections.Generic;
using Game.Creature;
using GridPublic;
using System.Collections;

namespace GridPrivate
{
    public class StateStride : GridFSMState
    {
        protected GridFSM Fsm;
        // target character
        protected GameObject Character;
        protected GridAPIPrivate GridAPI = (GridAPIPrivate)GridPublic.GridAPI.GetInstance();
        protected IPathfinder Pathfinder;
        protected Tile[,] Tiles;
        protected Vector3Int StartPosition;
        protected Vector3Int CurrentPosition;
        protected List<PathNode> Path;
        protected ITokenMovement Movement;
        protected float MaxMoveDist;

        // compact constructor
        public StateStride(GameObject character, GridFSM fsm)
        {
            this.Character = character;
            Fsm = fsm;
        }
        //return false if enter was unsuccessfull, true otherwise
        public override void Enter(FiniteStateMachine<GridFSMState> fsm)
        {
            base.Enter(fsm);
            canCancel = true;
            Tiles = GridAPI.GetTiles();
            StartPosition = Vector3Int.RoundToInt(Character.transform.position);
            Pathfinder = GridAPI.GetPathfinder();
            Pathfinder.Search(Character, StartPosition);
            MaxMoveDist = 0.2f * Character.GetComponent<CreatureComponent>()?.speed ?? 0;

            AIActionController ai = Character.GetComponent<AIActionController>();
            if (ai != null)
            {
                // grab best path from the AI's controller, this should be set during its decision making process
                if (ai.BestPath == null || ai.BestPath.Count == 0)
                {
                    Debug.LogWarning("AI has no path to target, skipping movement");
                    this.fsm.ChangeState(this.fsm.IdleState);
                }
                else
                {
                    Path = ai.BestPath;
                    Leftclick();
                }
            }
            else
            {
                //highlight tiles
                List<Vector3Int> toHighlight = Pathfinder.InRange(Character, StartPosition, MaxMoveDist);
                toHighlight.Remove(Vector3Int.RoundToInt(Character.transform.position));
                OnHighlightRange.Invoke(toHighlight);
                OnHover.AddListener(HighlightPath);
                OnHoverEnd.AddListener(HideHighlightPath);
            }
        }

        //called by FSM machine once a state change is triggered
        public override void Exit()
        {
            OnHighlightRangeEnd.Invoke();
            OnHover.RemoveListener(HighlightPath);
            OnHoverEnd.RemoveListener(HideHighlightPath);
            OnActionComplete.Invoke();
        }
        
        // highlight the path just by hovering over in range tiles, clear if not hovering over valid tiles
        public override void Leftclick()
        {
            if (Path != null && canCancel)
            {
                OnActionConfirm.Invoke();
                canCancel = false;
                // Remove Path highlight changes
                OnHover.RemoveListener(HighlightPath);
                OnHoverEnd.RemoveListener(HideHighlightPath);
                OnHighlightRangeEnd.Invoke();
                HideHighlightPath();
                // Execute the path
                CoroutineRunner.Run(ExecutePlayerMovement(Path));
            }
        }

        public override void Rightclick()
        {
            if (!canCancel) return;
            UniversalEvents.OnCancel.Invoke();
        }

        protected void HighlightPath(List<Vector3Int> hover)
        {
            this.Path = null;
            if (hover == null) return;
            if (hover.Count == 0)
            {
                // Previewing an empty path clears it
                OnPreviewPath.Invoke(new());
                return;
            }
            Vector3Int end = hover[0];

            // Abandon path if target is occupied
            Tile tile = Tiles[end.x, end.z];
            if (tile != null && tile.Occupants.Count > 0)
            {
                Path = null;
                OnPreviewPath.Invoke(new());
                return;
            }
            // Assemble the paths into just Vector3Int
            Path = Pathfinder.Find(end);
            List<Vector3Int> visits = new();
            foreach (PathNode node in Path)
            {
                if (node.Dist > MaxMoveDist)
                    break;
                visits.Add(node.Location);
            }
            OnPreviewPath.Invoke(visits);
        }

        protected void HideHighlightPath()
        {
            // Previewing an empty path effectively hides it
            OnPreviewPath.Invoke(new());
        }

        protected IEnumerator ExecutePlayerMovement(List<PathNode> path)
        {
            TokenMovement movement = TokenMovement.GetInstance();

            int i = 1;
            PathNode step;
            while (i < Path.Count && (step = path[i++]) != null && step.Dist < MaxMoveDist)
            {
                // Remove from tile
                CurrentPosition = Vector3Int.RoundToInt(Character.transform.position);
                Tile tile = Tiles[CurrentPosition.x, CurrentPosition.z];
                Ref<bool> prevented = new(false);
                yield return tile.RemoveToken(Character, prevented);
                if (prevented.Value)
                    break;

                // Move to new tile
                yield return movement.Hop(Character.transform, step.Location);

                // Add to tile
                tile = Tiles[step.Location.x, step.Location.z];
                yield return tile.PlaceToken(Character);
            }
            canCancel = true;
            Fsm.ChangeState(new StateIdle());
        }
    }
}
