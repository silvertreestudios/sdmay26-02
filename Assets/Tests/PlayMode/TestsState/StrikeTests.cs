using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using GridPrivate;


namespace TestsState
{
    public class StrikeTests : PlayModeBase
    {

        /// <summary>
        /// Resets the scene and then presses the Strike button before every test, waits for the state machine to transition into the strike state
        /// </summary>
        [UnitySetUp]
        public override IEnumerator Setup()
        {            
            yield return base.Setup();
            
            // Wait for the Stride button to appear in the UI
            Button strikeButton = null;
            yield return WaitUntilWithTimeout(timeout, () => {
                strikeButton = root.Q<Button>("UnarmedStrikeButton");
                return strikeButton != null;
            });

            Assert.IsNotNull(strikeButton, "Timed out waiting for the Strike button to appear in the UI.");
            
            // Simulate button click
            PushButton(strikeButton);

            // wait for the state to change to strike
            GridBase gridBase = Object.FindFirstObjectByType<GridBase>();

            yield return WaitUntilWithTimeout(timeout, () => gridBase.Fsm.CurrentState is StateStrike);

            Assert.IsTrue(gridBase.Fsm.CurrentState is StateStrike, "Timed out waiting for the FSM to transition to StateStrike after clicking the Strike button.");

            // Disable GridInput to prevent real mouse movements from overriding our injected hover events
            GridInput gridInput = Object.FindFirstObjectByType<GridInput>();
            if (gridInput != null)
            {
                gridInput.enabled = false;
            }
        }
        //TODO test that selecting a target returns the expected information
        [UnityTest]
        public IEnumerator StrikeEmptyInRange()
        {
            GameObject player = CombatManagerInterface.GetInstance().WhosTurn();
            Vector3 playerPos = player.transform.position;
            Vector3Int targetPos = new Vector3Int(Mathf.RoundToInt(playerPos.x), 0, Mathf.RoundToInt(playerPos.z + 1)); // empty tile within range
            OnHover.Invoke(new System.Collections.Generic.List<Vector3Int> { targetPos });

            GridBase gridBase = Object.FindFirstObjectByType<GridBase>();
            gridBase.Fsm.CurrentState.Leftclick();
            // Wait a frame for events to process
            yield return null;



            // check that we have transitioned to idle after an execution of strike
            Assert.IsTrue(gridBase.Fsm.CurrentState is StateIdle, "FSM should be in StateIdle after attempting to select any target");
            // check that the player still has 3 AP
            Assert.IsTrue(player.GetComponent<PlayerActionController>().ActionPoints == 3, "Player should still have 3 AP after attempting to strike an invalid target");
        }

        [UnityTest]
        public IEnumerator StrikeEmptyOutsideRange()
        {
            GameObject player = CombatManagerInterface.GetInstance().WhosTurn();
            Vector3 playerPos = player.transform.position;
            Vector3Int targetPos = new Vector3Int(Mathf.RoundToInt(playerPos.x), 0, Mathf.RoundToInt(playerPos.z + 2)); // empty tile outside range
            OnHover.Invoke(new System.Collections.Generic.List<Vector3Int> { targetPos });

            GridBase gridBase = Object.FindFirstObjectByType<GridBase>();
            gridBase.Fsm.CurrentState.Leftclick();
            // Wait a frame for events to process
            yield return null;



            // check that we have transitioned to idle after an execution of strike
            Assert.IsTrue(gridBase.Fsm.CurrentState is StateStrike, "FSM should be in StateStrike after attempting to select any target outside of range");
            // check that the player still has 3 AP
            Assert.IsTrue(player.GetComponent<PlayerActionController>().ActionPoints == 3, "Player should still have 3 AP after attempting to strike an invalid target");
        }

        [UnityTest]
        public IEnumerator StrikeValidInRange()
        {
            GameObject player = CombatManagerInterface.GetInstance().WhosTurn();
            Vector3 playerPos = player.transform.position;
            Vector3Int targetPos = new Vector3Int(Mathf.RoundToInt(playerPos.x + 1), 0, Mathf.RoundToInt(playerPos.z)); // enemy within range
            OnHover.Invoke(new System.Collections.Generic.List<Vector3Int> { targetPos });

            GridBase gridBase = Object.FindFirstObjectByType<GridBase>();
            gridBase.Fsm.CurrentState.Leftclick();
            // Wait a frame for events to process
            yield return null;



            // check that we have transitioned to idle after an execution of strike
            Assert.IsTrue(gridBase.Fsm.CurrentState is StateIdle, "FSM should be in StateIdle after attempting to select any target");
            // check that the player still has 3 AP
            Assert.IsTrue(player.GetComponent<PlayerActionController>().ActionPoints == 2, "Player should have 2 AP after attempting to strike a valid target");
        }

        [UnityTest]
        public IEnumerator StrikeValidOutsideRange()
        {
            GameObject player = CombatManagerInterface.GetInstance().WhosTurn();
            Vector3Int targetPos = new Vector3Int(Mathf.RoundToInt(18), 0, Mathf.RoundToInt(5)); // enemy outside range
            OnHover.Invoke(new System.Collections.Generic.List<Vector3Int> { targetPos });

            GridBase gridBase = Object.FindFirstObjectByType<GridBase>();
            gridBase.Fsm.CurrentState.Leftclick();
            // Wait a frame for events to process
            yield return null;



            // check that we have transitioned to idle after an execution of strike
            Assert.IsTrue(gridBase.Fsm.CurrentState is StateStrike, "FSM should be in StateStrike after attempting to select an enemy outside of range");
            // check that the player still has 3 AP
            Assert.IsTrue(player.GetComponent<PlayerActionController>().ActionPoints == 3, "Player should still have 3 AP after attempting to strike an enemy outside of range");
        }

         
        //TODO test that cancelling and going into stride does not break strike functionality
        //TODO test that the max range works in at least one cardinal direction
        //TODO ***STRETCH GOAL*** test emination ranges
        //TODO ***STRETCH GOAL*** test line of sight rules
    }
}