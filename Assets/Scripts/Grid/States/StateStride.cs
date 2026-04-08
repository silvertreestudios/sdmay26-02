using UnityEngine;
using System.Collections.Generic;
using Unity.VisualScripting;
using Game.Creature;

/*
namespace GridPrivate
{
    public class StateStride : GridFSMState
    {
        // target character
        protected GameObject character;
        protected IPathfinder Pathfinder;

        protected Vector3Int startCell;
        protected List<Vector3Int> path;
        protected bool IsMoving = false;

        // compact constructor
        public StateStride(GameObject character, IPathfinder pathfinder)
        {
            this.character = character;
            Pathfinder = pathfinder;
        }
        public override void Enter(FiniteStateMachine<GridFSMState> fsm)
        {
            base.Enter(fsm);

            startCell = Vector3Int.RoundToInt(character.transform.position);

            AIActionController ai = character.GetComponent<AIActionController>();
            if (ai != null)
            {
                if (ai.bestPath == null || ai.bestPath.Count == 0)
                {
                    Debug.LogWarning("AI has no path to target, skipping movement");
                    this.fsm.ChangeState(this.fsm.idleState);
                }
                else
                {
                    Debug.Log("starting AI stride movement, path length: " + ai.bestPath.Count);
                    CoroutineRunner.Run(ExecutePlayerMovement(ai.bestPath));
                }
            }
            else
            {
                //Highlight possible stride locations
                int maxMoveDist = character.GetComponent<CreatureComponent>()?.speed/5 ?? 0;
                List<Vector3Int> tiles = Pathfinder.InRange(startCell, maxMoveDist);
                OnHighlightRange.Invoke(tiles);
                Pathfinder.Search(startCell);
            }
        }

        public override bool Exit()
        {
            return true;
        }

        public override void Leftclick()
        {
            if (IsMoving) return; // Cannot select path while moving

            var path = Pathfinder.Find()
            if (controller.TryValidateAndGetPath(controller.currentCamera, character, out List<Vector3Int> path))
            {
                this.path = path;
                controller.visualIndicator.ShowPath(path, false);
                controller.lastClickedCell = path[path.Count - 1];
            }
            else
            {
                // invalid cell, make it impossible to execute stride
                controller.lastClickedCell = Vector3Int.zero;
            }

        }

        public override void DoubleLeftclick()
        {
            if (controller.isProcessingTurn) return; // Cannot execute stride while moving

            // execute stride if a valid path is selected
            if (controller.visualIndicator.IsActive && controller.lastClickedCell == path[path.Count - 1])
            {
                this.canceled = false;
                controller.isProcessingTurn = true;
                controller.rangeHighlighter.ClearHighlights();
                controller.visualIndicator.Clear();

                // movement tracking is handled by character controller, not sure if this is the best design choice
                // could maybe cause issues if multiply actions try to read the movement information without cleaning up
                controller.StartCoroutine(ExecutePlayerMovement(path));
            }
        }

        private System.Collections.IEnumerator ExecutePlayerMovement(List<Vector3Int> path)
        {
            ITokenMovement movement = controller.GetMovementController(character);
            yield return controller.StartCoroutine(controller.ExecuteMovementInternal(character, movement, path));
            canceled = false;
            fsm.ChangeState(fsm.idleState);
        }

        public override void Rightclick()
        {
            if (controller.isProcessingTurn) return; // Cannot cancel while moving

            // cancel stride when right clicking
            // if you are reading this and want to make another action, try to keep right click consistent for cancelling
            // I like it because its how the UI in Rimworld works and I quite enjoy that game :)
            //Debug.Log("[State_Stride] Stride cancelled");
            canceled = true;
            fsm.ChangeState(fsm.idleState);

        }
    }
}

*/
using UnityEngine;
using System.Collections.Generic;
using Unity.VisualScripting;
using Game.Creature;
using GridPublic;
using System.Numerics;
using UnityEngine.Android;

namespace GridPrivate
{
    public class StateStride : GridFSMState
    {
        // target character
        GameObject character;
        // reference to helper class
        // i dont think we need this anymore
        //GridCharacterController3D controller;
        private GridAPIPrivate gridAPI = (GridAPIPrivate)GridAPI.GetInstance();
        private IPathfinder IPathfinder;
        public bool canceled { get; private set; } = false;
        private Tile[,] gridTiles;
        private Vector3Int startPosition;
        private Vector3Int currentPosition;
        private List<PathNode> path;
        private ITokenMovement movement;

        // compact constructor
        public StateStride(GameObject character, GridCharacterController3D controller)
        {
            this.character = character;
        }
        //return false if enter was unsuccessfull, true otherwise
        public override void Enter(FiniteStateMachine<GridFSMState> fsm)
        {
            base.Enter(fsm);
            gridTiles = gridAPI.GetTiles();
            startPosition = Vector3Int.RoundToInt(character.transform.position);
            IPathfinder.Search(startPosition);
            int maxMoveDist = character.GetComponent<CreatureComponent>()?.speed ?? 0;
            IPathfinder.InRange(startPosition, maxMoveDist/5);
            // Debug.Log("[State_Stride] Entered Stride State");
            canceled = true;
           
            //let the FSM handle this flag. if the state is not idle then we are processing a turn
            this.fsm.isProcessingTurn = false;

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
                    CoroutineRunner.Run(ExecutePlayerMovement(ai.bestPath));
                }
            }
            else
            {
                //highlight tiles
                controller.rangeHighlighter.UpdateHighlights(startTile, maxMoveDist / 5);
            }
        }

        //called by FSM machine once a state change is triggered
        public override bool Exit()
        {
            // Debug.Log("Canceled Stride");
            fsm.canceled = canceled;
            // Clear visual indicators
            controller.visualIndicator.Clear();
            controller.rangeHighlighter.ClearHighlights();
            //TODO add a exited state flag for the FSM to listen to so the state itself does not have to change states
            //This flag should return the cancel condition so we dont need to manually set the fsm cancel condition here
            //return true;
        }
        
        // hihligt the path just by hovering over in range tiles, clear if not hovering over valid tiles
        public override void Leftclick()
        {
            if (fsm.isProcessingTurn) return; 
            // execute stride if a valid path is selected
            //TODO figure out new click system
            path = IPathfinder.Find(clickedcell);
            if (path != null)
            {
                this.canceled = false;
                fsm.isProcessingTurn = true;

                CoroutineRunner.Run(ExecutePlayerMovement(path));
                //because ExecutePlayerMovement is no longer a co-rouitne we can wait for it to return before exiting the stride state
            }
        }

        public override void Rightclick()
        {
            if (fsm.isProcessingTurn) return; // Cannot cancel while moving

            // cancel stride when right clicking
            // if you are reading this and want to make another action, try to keep right click consistent for cancelling
            // I like it because its how the UI in Rimworld works and I quite enjoy that game :)
            //Debug.Log("[State_Stride] Stride cancelled");
            canceled = true;
            Exit();
        }

        public override void StateUpdate()
        {
            if(fsm.isProcessingTurn)return;
            //TODO implement hover path highlights here
        }

        private System.Collections.IEnumerator ExecutePlayerMovement(List<PathNode> path)
        {
            //TODO find new home for the movement controllers (ITokenMovment)
            movement = controller.GetMovementController(character);
            //TODO find a way to translate List<PathNode to a List<Vector3Int> so ryans code works
            movement.setPath(path);
            movement.start();

            Vector3Int lastPosition = Vector3Int.RoundToInt(character.transform.position);

            while (movement.IsMoving())
            {
                //character leaves tile to begin movement
                gridAPI.RemoveToken(character);
                yield return movement.update();

                currentPosition = Vector3Int.RoundToInt(character.transform.position);
                //if there is a character in the currecnt tile then we are simply passing through the token, dont add to tile.
                if(gridTiles[currentPosition.x, currentPosition.z].Occupant != null) continue;
                //we no longer need to check if each cell in the path is selectable since the find function handles that check for us
                gridAPI.PlaceToken(character);
            }
            fsm.isProcessingTurn = false;
            Exit();
        }
    }
}
