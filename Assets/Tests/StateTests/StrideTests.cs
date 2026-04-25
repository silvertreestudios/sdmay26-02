using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
//using GridPrivate;

namespace TestsState
{
    public class StrideTests
    {
        //get UI document to press buttons
        private UIDocument GetUIDocument()
        {
            return Object.FindFirstObjectByType<UIDocument>();
        }

        [UnitySetUp]
        public IEnumerator Setup()
        {
            // Load the MainMenu scene - add it to Build Settings first!
            SceneManager.LoadScene("UnitTestingScene");
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "UnitTestingScene");
        }
        
        //TODO test that moving works
        // [UnityTest]
        // public IEnumerator StrideMoveTest()
        // {
        //     //get active player object, click move, select tile that is pos.x, pos.y, pos.z + 1, check that player is now at that position

        //     var doc = GetUIDocument();
        //     var ui = doc.rootVisualElement;
        //     Button moveButton = ui.Q<Button>("MoveButton");

        //     // Simulate button click
        //     using (var evt = NavigationSubmitEvent.GetPooled())
        //     {
        //         evt.target = moveButton;
        //         moveButton.SendEvent(evt);
        //     }
            
        //     // Wait for highlights to load
        //     yield return new WaitForSeconds(0.1f);
            
        //     // hijack the stride state and initiate a left click on the tile directly above the player
        //     //THIS USES REFLECTION, WATCH A TUTORIAL ON THIS SO YOU UNDERSTAND IT
        //     // 1. Obtain your active StateStride instance (assuming you have access to the FSM or create it in the test)
        //     StateStride strideState = /* your test's StateStride instance */;

        //     // 2. Use Reflection to get the protected 'Character' field to calculate its position
        //     FieldInfo charField = typeof(StateStride).GetField("Character", BindingFlags.NonPublic | BindingFlags.Instance);
        //     GameObject character = (GameObject)charField.GetValue(strideState);

        //     // Calculate relative position (e.g., 1 tile forward in X)
        //     Vector3Int startPos = Vector3Int.RoundToInt(character.transform.position);
        //     Vector3Int targetTile = startPos + new Vector3Int(1, 0, 0); 

        //     // 3. Use Reflection to access and invoke the protected 'HighlightPath' method
        //     MethodInfo highlightMethod = typeof(StateStride).GetMethod("HighlightPath", BindingFlags.NonPublic | BindingFlags.Instance);

        //     // HighlightPath expects a List<Vector3Int> containing the hovered tile
        //     List<Vector3Int> hoverTarget = new List<Vector3Int> { targetTile };
        //     highlightMethod.Invoke(strideState, new object[] { hoverTarget });

        //     // 4. Now that 'Path' is populated internally, call Leftclick directly to trigger movement!
        //     strideState.Leftclick();


        //     yield return null;
        // }
        //TODO test that you cannot move to invalid tiles (void, wall, enemy, teammate, self)
        //TODO test that cancelling and going into strike state does not break stride functionality
        //TODO test that the max range works in at least one cardinal direction
        //TODO test that the player pathfinds around enemies and through teammates
    }
}