using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace TestsGrid
{
    public class GridClassTests
    {
        [Test]
        public void LevelLoadsSuccessfullyInEditor()
        {
            // Find the scene path by name
            string[] guids = AssetDatabase.FindAssets("t:Scene UnitTestingScene");
            Assert.IsTrue(guids.Length > 0, "Could not find UnitTestingScene in the project.");
            string scenePath = AssetDatabase.GUIDToAssetPath(guids[0]);

            // Open the scene in Edit Mode
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            // Find the Map component responsible for generating the level
            Map map = Object.FindFirstObjectByType<Map>();

            // Assert that the Map component was found in the scene
            Assert.IsNotNull(
                map,
                "Map component was not found. Ensure 'UnitTestingScene' has a GameObject with the Map component."
            );

            // Force generation directly since we are in EditMode
            map.Generate();

            // Assert that the Map initialized its data correctly
            Assert.IsNotNull(map.GetMapData(), "Map Data should be populated upon generation.");

            // Since Map.cs generates tiles as children for the level, assert children exist
            Assert.Greater(
                map.transform.childCount,
                0,
                "Map should generate child tile objects to display the level."
            );
        }
    }
}
