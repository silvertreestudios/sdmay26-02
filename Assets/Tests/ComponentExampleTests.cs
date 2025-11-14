using NUnit.Framework;
using UnityEngine;

namespace Tests
{
    public class ComponentExampleTests
    {
        [Test]
        public void TestTransformPosition()
        {
            // Arrange
            GameObject go = new GameObject("TestObject");
            Transform transform = go.transform;
            Vector3 newPosition = new Vector3(5, 10, 15);
            
            // Act
            transform.position = newPosition;
            
            // Assert
            Assert.AreEqual(newPosition, transform.position);
            
            // Cleanup
            Object.DestroyImmediate(go);
        }
        
        [Test]
        public void TestTransformRotation()
        {
            // Arrange
            GameObject go = new GameObject("TestObject");
            Transform transform = go.transform;
            Quaternion rotation = Quaternion.Euler(45, 90, 180);
            
            // Act
            transform.rotation = rotation;
            
            // Assert
            Assert.AreEqual(rotation, transform.rotation);
            
            // Cleanup
            Object.DestroyImmediate(go);
        }
        
        [Test]
        public void TestTransformScale()
        {
            // Arrange
            GameObject go = new GameObject("TestObject");
            Transform transform = go.transform;
            Vector3 scale = new Vector3(2, 3, 4);
            
            // Act
            transform.localScale = scale;
            
            // Assert
            Assert.AreEqual(scale, transform.localScale);
            
            // Cleanup
            Object.DestroyImmediate(go);
        }
        
        [Test]
        public void TestAddComponent()
        {
            // Arrange
            GameObject go = new GameObject("TestObject");
            
            // Act
            Rigidbody rb = go.AddComponent<Rigidbody>();
            
            // Assert
            Assert.IsNotNull(rb);
            Assert.IsNotNull(go.GetComponent<Rigidbody>());
            
            // Cleanup
            Object.DestroyImmediate(go);
        }
        
        [Test]
        public void TestGameObjectActivation()
        {
            // Arrange
            GameObject go = new GameObject("TestObject");
            
            // Act
            go.SetActive(false);
            bool isActiveAfterDeactivation = go.activeSelf;
            
            go.SetActive(true);
            bool isActiveAfterActivation = go.activeSelf;
            
            // Assert
            Assert.IsFalse(isActiveAfterDeactivation);
            Assert.IsTrue(isActiveAfterActivation);
            
            // Cleanup
            Object.DestroyImmediate(go);
        }
        
        [Test]
        public void TestGameObjectTag()
        {
            // Arrange
            GameObject go = new GameObject("TestObject");
            string expectedTag = "Player";
            
            // Act
            go.tag = expectedTag;
            
            // Assert
            Assert.AreEqual(expectedTag, go.tag);
            
            // Cleanup
            Object.DestroyImmediate(go);
        }
    }
}
