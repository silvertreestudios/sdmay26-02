using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using GridPrivate;

namespace TestsState
{
    public class StrideTests : PlayModeBase
    {
        /// <summary>
        /// Resets the scene and then presses the Stride button before every test, waits for the state machine to transition into the stride state
        /// </summary>
        [UnitySetUp]
        public override IEnumerator Setup()
        {           
            yield return base.Setup();
            
            // Wait for the Stride button to appear in the UI
            Button moveButton = null;
            yield return WaitUntilWithTimeout(timeout, () => {
                moveButton = root.Q<Button>("StrideButton");
                return root.Q<Button>("StrideButton") != null;
            });

            Assert.IsNotNull(moveButton, "Timed out waiting for the Stride button to appear in the UI.");
            // Simulate button click
            PushButton(moveButton);

            // wait for the state to change to stride
            GridBase gridBase = Object.FindFirstObjectByType<GridBase>();
            yield return WaitUntilWithTimeout(timeout, () => gridBase.Fsm.CurrentState is StateStride);

            Assert.IsTrue(gridBase.Fsm.CurrentState is StateStride, "Timed out waiting for the FSM to transition to StateStride after clicking the Stride button.");

            // Disable GridInput to prevent real mouse movements from overriding our injected hover events
            GridInput gridInput = Object.FindFirstObjectByType<GridInput>();
            if (gridInput != null)
            {
                gridInput.enabled = false;
            }
        }

        /// <summary>
        /// Tests that the player can move to the right 3 tiles around an enemy player. Visually inpect this test for now to ensure that team pathfinding rules are enforced
        /// </summary>
        [UnityTest]
        public IEnumerator StrideMoveTest()
        {
            //get active player object, click move, select tile that is pos.x, pos.y, pos.z + 1, check that player is now at that position
            GameObject player = CombatManagerInterface.GetInstance().WhosTurn();
            Vector3 startPos = player.transform.position;
            Vector3Int targetPos = new Vector3Int(Mathf.RoundToInt(startPos.x) + 3, Mathf.RoundToInt(startPos.y), Mathf.RoundToInt(startPos.z));

            // Invoke OnHover for the target tile to preview path
            OnHover.Invoke(new System.Collections.Generic.List<Vector3Int> { targetPos });
            
            // Wait a frame for events to process
            yield return null;

            // Get FSM and simulate left click
            GridBase gridBase = Object.FindFirstObjectByType<GridBase>();
            gridBase.Fsm.CurrentState.Leftclick();

            // Wait for movement to finish (FSM returns to idle)
            yield return WaitUntilWithTimeout(timeout, () => gridBase.Fsm.CurrentState is StateIdle);
            
            Assert.IsTrue(gridBase.Fsm.CurrentState is StateIdle, "FSM did not return to StateIdle after movement.");
            Vector3 endPos = player.transform.position;
            Assert.AreEqual(targetPos, Vector3Int.RoundToInt(endPos), "Player did not move to the specified target position.");
        }

        /// <summary>
        /// Tests that a player cannot execute a stride action on an invalid tile (occupied tiles, null tiles, walls ect)
        /// </summary>
        [UnityTest]
        public IEnumerator StrideInvalidMoveTest()
        {
            GameObject player = CombatManagerInterface.GetInstance().WhosTurn();
            Vector3 startPos = player.transform.position;
            // hover over enemy tile and try to move there, check that player did not move
            Vector3Int targetPos = new Vector3Int(Mathf.RoundToInt(startPos.x) + 1, Mathf.RoundToInt(startPos.y), Mathf.RoundToInt(startPos.z));
            OnHover.Invoke(new System.Collections.Generic.List<Vector3Int> { targetPos });

            GridBase gridBase = Object.FindFirstObjectByType<GridBase>();
            gridBase.Fsm.CurrentState.Leftclick();

            // Wait a frame for events to process
            yield return null;

            targetPos = new Vector3Int(0, 0, 0); // null tile

            OnHover.Invoke(new System.Collections.Generic.List<Vector3Int> { targetPos });
            gridBase.Fsm.CurrentState.Leftclick();

            // Wait a frame for events to process
            yield return null;

            

            // check that player did not move and that we are still in stride state
            Assert.IsTrue(gridBase.Fsm.CurrentState is StateStride, "FSM should still be in StateStride after attempting to move to an invalid tile.");
            Assert.AreEqual(startPos, player.transform.position, "Player should not have moved when attempting to move to an invalid tile.");

        }
        //TODO test that cancelling and going into strike state does not break stride functionality
        //TODO test that the max range works in at least one cardinal direction
        //TODO test that the player pathfinds around enemies and through teammates
    }
}