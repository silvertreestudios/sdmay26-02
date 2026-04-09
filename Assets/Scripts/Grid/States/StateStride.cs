using UnityEngine;
using System.Collections.Generic;
using Game.Creature;
using GridPublic;

namespace GridPrivate
{
    public class StateStride : GridFSMState
    {
        // target character
        GameObject character;
        // reference to helper class
        // i dont think we need this anymore
        //GridCharacterController3D controller;
        protected GridAPIPrivate GridAPI = (GridAPIPrivate)GridPublic.GridAPI.GetInstance();
        protected IPathfinder IPathfinder;
        protected Tile[,] Tiles;
        protected Vector3Int StartPosition;
        protected Vector3Int CurrentPosition;
        protected List<PathNode> Path;
        protected ITokenMovement Movement;
        protected float MaxMoveDist;

        // compact constructor
        public StateStride(GameObject character)
        {
            this.character = character;
        }
        //return false if enter was unsuccessfull, true otherwise
        public override void Enter(FiniteStateMachine<GridFSMState> fsm)
        {
            base.Enter(fsm);
            canCancel = true;
            Tiles = GridAPI.GetTiles();
            StartPosition = Vector3Int.RoundToInt(character.transform.position);
            IPathfinder = GridAPI.GetPathfinder();
            IPathfinder.Search(character, StartPosition);
            MaxMoveDist = 0.2f * character.GetComponent<CreatureComponent>()?.speed ?? 0;

            AIActionController ai = character.GetComponent<AIActionController>();
            if (ai != null)
            {

                //controller.isProcessingTurn = true;
                // grab best path from the AI's controller, this should be set during its decision making process
                if (ai.bestPath == null || ai.bestPath.Count == 0)
                {
                    Debug.LogWarning("AI has no path to target, skipping movement");
                    this.fsm.ChangeState(this.fsm.idleState);
                }
                else
                {
                    Debug.Log("starting AI stride movement, path length: " + ai.bestPath.Count);
                    //TODO update mindless controller to utilize new pathfinding system
                    //CoroutineRunner.Run(ExecutePlayerMovement(ai.bestPath));

                    ////////////////////////////////////////////////////////////////////////////////////////
                }
            }
            else
            {
                //highlight tiles
                List<Vector3Int> toHighlight = IPathfinder.InRange(character, StartPosition, MaxMoveDist);
                OnHighlightRange.Invoke(toHighlight);
            }
        }

        //called by FSM machine once a state change is triggered
        public override void Exit()
        {
            OnHighlightRangeEnd.Invoke();
        }
        
        // highlight the path just by hovering over in range tiles, clear if not hovering over valid tiles
        public override void Leftclick()
        {
            if (Path != null && canCancel)
            {
                canCancel = false;

                int i = 0;
                while (Path[i++].Dist < MaxMoveDist)
                {
                CoroutineRunner.Run(ExecutePlayerMovement(Path));
                }
            }
        }

        public override void Rightclick()
        {
            if (!canCancel) return;
            UniversalEvents.OnCancel.Invoke();
        }

        public override void StateUpdate()
        {
            if(!canCancel)return;
            //TODO implement hover path highlights here
        }

        private System.Collections.IEnumerator ExecutePlayerMovement(List<PathNode> path)
        {
            TokenMovement movement = character.GetComponent<TokenMovement>();

            int i = 0;
            PathNode step = path[0];
            while ( step.Dist < MaxMoveDist)
            {
                // Remove from tile
                Tile tile = Tiles[step.Location.x, step.Location.z];
                Ref<bool> prevented = new(false);
                yield return tile.RemoveToken(character, prevented);
                if (prevented.Value)
                    break;

                // Move to new tile
                movement.setPoint(step.Location);
                movement.start();
                yield return Movement.update();

                // Add to tile
                CurrentPosition = Vector3Int.RoundToInt(character.transform.position);
                tile.PlaceToken(character);

                step = ++i < path.Count? path[i]: null;
            }
            Exit();
        }
    }
}
