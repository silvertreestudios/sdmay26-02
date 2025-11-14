using NUnit.Framework;
using UnityEngine;

namespace Tests
{
    public class MathExampleTests
    {
        [Test]
        public void TestMultiplication()
        {
            // Arrange
            int a = 7;
            int b = 6;
            
            // Act
            int result = a * b;
            
            // Assert
            Assert.AreEqual(42, result);
        }
        
        [Test]
        public void TestDivision()
        {
            // Arrange
            float numerator = 10f;
            float denominator = 4f;
            
            // Act
            float result = numerator / denominator;
            
            // Assert
            Assert.AreEqual(2.5f, result);
        }
        
        [Test]
        public void TestMathfClamp()
        {
            // Arrange
            float value = 15f;
            float min = 0f;
            float max = 10f;
            
            // Act
            float clamped = Mathf.Clamp(value, min, max);
            
            // Assert
            Assert.AreEqual(10f, clamped);
        }
        
        [Test]
        public void TestMathfLerp()
        {
            // Arrange
            float start = 0f;
            float end = 100f;
            float t = 0.5f;
            
            // Act
            float interpolated = Mathf.Lerp(start, end, t);
            
            // Assert
            Assert.AreEqual(50f, interpolated);
        }
        
        [Test]
        public void TestSquareRoot()
        {
            // Arrange
            float value = 16f;
            
            // Act
            float result = Mathf.Sqrt(value);
            
            // Assert
            Assert.AreEqual(4f, result);
        }
        
        [Test]
        public void TestAbsoluteValue()
        {
            // Arrange
            int negativeValue = -42;
            
            // Act
            int result = Mathf.Abs(negativeValue);
            
            // Assert
            Assert.AreEqual(42, result);
        }
    }
}
