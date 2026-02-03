// using System.Collections;
// using System.Collections.Generic;
// using UnityEngine;

// /// <summary>
// /// Handles grid-based action coroutines (Strike, Stride, etc.)
// /// Manages input handling and action execution flow
// /// </summary>
// public class GridActionHandler
// {
//     // Dependencies
//     private readonly GridCharacterController3D controller;
//     private readonly GridMemory gridMemory;
//     private readonly GridCoordinateConverter coordinateConverter;
//     private readonly GridPathfinder pathfinder;
//     private readonly MovementRange rangeHighlighter;
//     private readonly VisualIndicator visualIndicator;
//     private readonly Dictionary<GameObject, ITokenMovement> tokenMovements;

//     // Input state (accessed via properties from controller)
//     private System.Func<bool> getLeftClick;
//     private System.Func<bool> getRightClick;
//     private System.Func<bool> getIsDoubleClick;
//     private System.Func<bool> getCancel;
//     private System.Action<bool> setCancel;
//     private System.Action<bool> setLeftClick;
//     private System.Action<bool> setRightClick;
//     private System.Func<Vector3Int> getLastClickedCell;
//     private System.Action<Vector3Int> setLastClickedCell;
//     private System.Func<Camera> getCurrentCamera;
//     private System.Func<ITokenMovement> getCurrentMovement;

//     // State management
//     private System.Action<bool> setIsProcessingTurn;

//     /// <summary>
//     /// Creates a new GridActionHandler
//     /// </summary>
//     public GridActionHandler(
//         GridCharacterController3D controllerRef,
//         GridMemory gridMemoryRef,
//         GridCoordinateConverter coordinateConverterRef,
//         GridPathfinder pathfinderRef,
//         MovementRange rangeHighlighterRef,
//         VisualIndicator visualIndicatorRef,
//         Dictionary<GameObject, ITokenMovement> tokenMovementsRef)
//     {
//         controller = controllerRef;
//         gridMemory = gridMemoryRef;
//         coordinateConverter = coordinateConverterRef;
//         pathfinder = pathfinderRef;
//         rangeHighlighter = rangeHighlighterRef;
//         visualIndicator = visualIndicatorRef;
//         tokenMovements = tokenMovementsRef;
//     }

//     /// <summary>
//     /// Initializes input state accessors
//     /// </summary>
//     public void InitializeInputAccessors(
//         System.Func<bool> leftClickGetter,
//         System.Func<bool> rightClickGetter,
//         System.Func<bool> doubleClickGetter,
//         System.Func<bool> cancelGetter,
//         System.Action<bool> cancelSetter,
//         System.Action<bool> leftClickSetter,
//         System.Action<bool> rightClickSetter,
//         System.Func<Vector3Int> lastClickedCellGetter,
//         System.Action<Vector3Int> lastClickedCellSetter,
//         System.Func<Camera> currentCameraGetter,
//         System.Func<ITokenMovement> currentMovementGetter,
//         System.Action<bool> isProcessingTurnSetter)
//     {
//         getLeftClick = leftClickGetter;
//         getRightClick = rightClickGetter;
//         getIsDoubleClick = doubleClickGetter;
//         getCancel = cancelGetter;
//         setCancel = cancelSetter;
//         setLeftClick = leftClickSetter;
//         setRightClick = rightClickSetter;
//         getLastClickedCell = lastClickedCellGetter;
//         setLastClickedCell = lastClickedCellSetter;
//         getCurrentCamera = currentCameraGetter;
//         getCurrentMovement = currentMovementGetter;
//         setIsProcessingTurn = isProcessingTurnSetter;
//     }

//     /// <summary>
//     /// Executes strike action - selects a target within range
//     /// </summary>
//     // public IEnumerator ExecuteStrike(GameObject character, int range, CoroutineResult<GameObject> result)
//     // {
//     //     // Reset input state
//     //     yield return BeginAction(character);

//     //     List<GameObject> occupants = controller.GetOccupantsInArea(character, range);
//     //     result.Value = null;

//     //     while (true)
//     //     {
//     //         yield return new WaitUntil(() => getLeftClick() || getRightClick() || getCancel());

//     //         if (getCancel()) break;

//     //         if (getRightClick())
//     //         {
//     //             Debug.Log("[GridActionHandler] Strike cancelled");
//     //             setRightClick(false);
//     //             break;
//     //         }

//     //         setLeftClick(false);
//     //         Debug.Log("[GridActionHandler] Strike action processing...");

//     //         if (TryGetClickedCell(getCurrentCamera(), out Vector3Int targetCell))
//     //         {
//     //             List<GameObject> occupantsInCell = gridMemory.GetOccupantsInArea(new List<Vector3Int> { targetCell });

//     //             if (occupantsInCell.Count == 0)
//     //             {
//     //                 Debug.Log("[GridActionHandler] No occupants in the selected cell.");
//     //                 continue;
//     //             }

//     //             result.Value = occupantsInCell[0];

//     //             if (result.Value == null)
//     //                 continue;

//     //             // Check for double-click confirmation
//     //             if (getIsDoubleClick())
//     //             {
//     //                 if (occupants.Contains(result.Value))
//     //                 {
//     //                     Debug.Log($"[GridActionHandler] Target confirmed: {result.Value.name}");
//     //                     break;
//     //                 }
//     //                 else
//     //                 {
//     //                     Debug.Log("[GridActionHandler] Selected an invalid target.");
//     //                     continue;
//     //                 }
//     //             }
//     //             else
//     //             {
//     //                 // Single-click - preview target
//     //                 Debug.Log($"[GridActionHandler] Target preview: {result.Value.name}");
//     //             }
//     //         }
//     //     }

//     //     EndAction();
//     //     Debug.Log("[GridActionHandler] Strike action completed.");
//     //     yield return null;
//     // }

//     /// <summary>
//     /// Executes stride action - moves character along a path
//     /// </summary>
//     // public IEnumerator ExecuteStride(GameObject character, CoroutineResult<bool> canceled)
//     // {
//     //     // Reset input state
//     //     yield return BeginAction(character);

//     //     // Update highlights for movement range
//     //     Vector3Int startCell = coordinateConverter.GetCharacterCell(character);
//     //     rangeHighlighter.UpdateHighlights(startCell, controller.maxMovementDistance);

//     //     while (true)
//     //     {
//     //         yield return new WaitUntil(() => getLeftClick() || getRightClick() || getCancel());

//     //         if (getCancel()) break;

//     //         if (getRightClick())
//     //         {
//     //             if (visualIndicator.IsActive)
//     //             {
//     //                 visualIndicator.Clear();
//     //                 Debug.Log("[GridActionHandler] Visual indicator cancelled.");
//     //             }
//     //             setRightClick(false);
//     //             continue;
//     //         }

//     //         setLeftClick(false);

//     //         if (TryValidateAndGetPath(getCurrentCamera(), character, out List<Vector3Int> path))
//     //         {
//     //             if (getIsDoubleClick())
//     //             {
//     //                 // Double-click - confirm movement
//     //                 Debug.Log("[GridActionHandler] Double-click detected - confirming movement.");

//     //                 if (visualIndicator.IsActive && getLastClickedCell() == path[path.Count - 1])
//     //                 {
//     //                     setIsProcessingTurn(true);
//     //                     rangeHighlighter.ClearHighlights();
//     //                     visualIndicator.Clear();
//     //                     controller.StartCoroutine(ExecuteMovementInternal(character, getCurrentMovement(), path));
//     //                     break;
//     //                 }
//     //             }
//     //             else
//     //             {
//     //                 // Single-click - show path preview
//     //                 Debug.Log("[GridActionHandler] Single-click detected - showing visual indicator.");
//     //                 visualIndicator.ShowPath(path, false);
//     //                 setLastClickedCell(path[path.Count - 1]);
//     //             }
//     //         }
//     //     }

//     //     canceled.Value = getCancel();
//     //     EndAction();

//     //     if (visualIndicator.IsActive)
//     //     {
//     //         visualIndicator.Clear();
//     //     }

//     //     Debug.Log("[GridActionHandler] Stride action completed.");
//     // }

//     /// <summary>
//     /// Initializes action state (called at start of each action)
//     /// </summary>
//     private IEnumerator BeginAction(GameObject character)
//     {
//         setCancel(true);
//         yield return null;
//         setCancel(false);
//         controller.SetActivePlayer(character);
//     }

//     /// <summary>
//     /// Cleans up action state (called at end of each action)
//     /// </summary>
//     private void EndAction()
//     {
//         setCancel(false);
//         setLeftClick(false);
//         setRightClick(false);
//         rangeHighlighter.ClearHighlights();
//     }

//     /// <summary>
//     /// Validates clicked cell and calculates path
//     /// </summary>
//     private bool TryValidateAndGetPath(Camera cam, GameObject character, out List<Vector3Int> path)
//     {
//         path = null;

//         if (!TryGetClickedCell(cam, out Vector3Int targetCell))
//             return false;

//         if (!gridMemory.IsCellWalkable(targetCell))
//             return false;

//         if (!rangeHighlighter.IsCellReachable(targetCell))
//             return false;

//         Vector3Int startCell = coordinateConverter.GetCharacterCell(character);
//         var pathResult = pathfinder.FindPath(startCell, targetCell);

//         if (!pathResult.found || pathResult.path == null || pathResult.path.Count < 2)
//             return false;

//         if (controller.maxMovementDistance > 0)
//         {
//             int pathSteps = pathResult.path.Count - 1;
//             if (pathSteps > controller.maxMovementDistance)
//                 return false;
//         }

//         path = pathResult.path;
//         return true;
//     }

//     /// <summary>
//     /// Converts screen click to grid cell
//     /// </summary>
//     private bool TryGetClickedCell(Camera cam, out Vector3Int targetCell)
//     {
//         targetCell = Vector3Int.zero;

//         if (!coordinateConverter.ScreenToXZPlane(cam, InputCompat.MousePositionScreen(), gridMemory.GridY, out Vector3 hit))
//             return false;

//         return coordinateConverter.TryGridWorldToCell(hit, out targetCell);
//     }

//     /// <summary>
//     /// Internal movement execution coroutine
//     /// </summary>
//     private IEnumerator ExecuteMovementInternal(GameObject actor, ITokenMovement movement, List<Vector3Int> path)
//     {
//         movement.setPath(path);
//         yield return new WaitForSeconds(0.3f);
//         movement.start();

//         Vector3Int lastCell = coordinateConverter.GetCharacterCell(actor);

//         while (movement.IsMoving())
//         {
//             yield return movement.update();

//             Vector3Int currentCell = coordinateConverter.GetCharacterCell(actor);

//             if (currentCell != lastCell)
//             {
//                 gridMemory.MoveCreaturePosition(actor, currentCell, lastCell);
//                 lastCell = currentCell;
//             }
//         }

//         Vector3Int finalCell = coordinateConverter.GetCharacterCell(actor);
//         if (finalCell != lastCell)
//         {
//             gridMemory.MoveCreaturePosition(actor, finalCell, lastCell);
//         }

//         yield return new WaitForSeconds(0.5f);
//         setIsProcessingTurn(false);

//         Debug.Log("[GridActionHandler] Movement completed.");
//     }
// }