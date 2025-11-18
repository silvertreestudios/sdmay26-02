using NUnit.Framework;

namespace Tests
{
    public class SampleTests
    {
        [Test]
        public void TestAddition()
        {
            // Arrange
            int a = 2;
            int b = 3;
            
            // Act
            int result = a + b;
            
            // Assert
            Assert.AreEqual(5, result);
        }
        
        [Test]
        public void TestStringConcatenation()
        {
            // Arrange
            string str1 = "Hello";
            string str2 = "World";
            
            // Act
            string result = str1 + " " + str2;
            
            // Assert
            Assert.AreEqual("Hello World", result);
        }
        
        [Test]
        public void TestBooleanLogic()
        {
            // Arrange
            bool isTrue = true;
            bool isFalse = false;
            
            // Act & Assert
            Assert.IsTrue(isTrue);
            Assert.IsFalse(isFalse);
            Assert.IsTrue(isTrue && !isFalse);
        }
    }
}
