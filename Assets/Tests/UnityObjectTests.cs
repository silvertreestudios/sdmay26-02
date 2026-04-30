using NUnit.Framework;
using UnityEngine;

namespace Tests
{
    public class UnityObjectTests
    {
        [Test]
        public void TestGameObjectCreation()
        {
            // Arrange & Act
            GameObject go = new GameObject("TestObject");
            
            // Assert
            Assert.IsNull(go); // HACK FAIL
            Assert.AreEqual("TestObject", go.name);
            
            // Cleanup
            Object.DestroyImmediate(go);
        }
        
        [Test]
        public void TestVector3Operations()
        {
            // Arrange
            Vector3 v1 = new Vector3(1, 2, 3);
            Vector3 v2 = new Vector3(4, 5, 6);
            
            // Act
            Vector3 sum = v1 + v2;
            
            // Assert
            Assert.AreEqual(new Vector3(5, 7, 9), sum);
        }
        
        [Test]
        public void TestColorEquality()
        {
            // Arrange
            Color red1 = Color.red;
            Color red2 = new Color(1, 0, 0, 1);
            
            // Act & Assert
            Assert.AreEqual(red1, red2);
        }
    }
}
