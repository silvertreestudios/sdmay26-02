using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class NewTestScript
{
    [UnitySetUp]
    public IEnumerator Setup()
    {
        string sceneName = "SampleScene";
        UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
        yield return new WaitUntil(() => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == sceneName);
        yield return new WaitForSeconds(1f); // Wait a frame to ensure scene is fully loaded
    }
    // A Test behaves as an ordinary method
    [Test]
    public void NewTestScriptSimplePasses()
    {
        // Use the Assert class to test conditions
    }
    // A UnityTest behaves like a coroutine in Play Mode. In Edit Mode you can use
    // `yield return null;` to skip a frame.
    [UnityTest]
    public IEnumerator PlayerExists()
    {
        // Use the Assert class to test conditions.
        // Use yield to skip a frame.
        // for now just checks for an object named "Token"
        GameObject player = GameObject.Find("Token");
        Assert.IsNotNull(player, "Player object 'Token' does not exist in the scene");

        yield return null;
    }
}
