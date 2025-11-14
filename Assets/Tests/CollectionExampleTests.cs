using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Tests
{
    public class CollectionExampleTests
    {
        [Test]
        public void TestListAddition()
        {
            // Arrange
            List<int> numbers = new List<int>();
            
            // Act
            numbers.Add(1);
            numbers.Add(2);
            numbers.Add(3);
            
            // Assert
            Assert.AreEqual(3, numbers.Count);
            Assert.AreEqual(2, numbers[1]);
        }
        
        [Test]
        public void TestListRemoval()
        {
            // Arrange
            List<string> fruits = new List<string> { "Apple", "Banana", "Cherry" };
            
            // Act
            fruits.Remove("Banana");
            
            // Assert
            Assert.AreEqual(2, fruits.Count);
            Assert.IsFalse(fruits.Contains("Banana"));
        }
        
        [Test]
        public void TestDictionaryOperations()
        {
            // Arrange
            Dictionary<string, int> scores = new Dictionary<string, int>();
            
            // Act
            scores.Add("Player1", 100);
            scores.Add("Player2", 200);
            scores["Player3"] = 150;
            
            // Assert
            Assert.AreEqual(3, scores.Count);
            Assert.AreEqual(200, scores["Player2"]);
            Assert.IsTrue(scores.ContainsKey("Player3"));
        }
        
        [Test]
        public void TestArrayOperations()
        {
            // Arrange
            int[] numbers = new int[5];
            
            // Act
            for (int i = 0; i < numbers.Length; i++)
            {
                numbers[i] = i * 10;
            }
            
            // Assert
            Assert.AreEqual(5, numbers.Length);
            Assert.AreEqual(30, numbers[3]);
        }
        
        [Test]
        public void TestLinqQuery()
        {
            // Arrange
            List<int> numbers = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            
            // Act
            var evenNumbers = numbers.Where(n => n % 2 == 0).ToList();
            
            // Assert
            Assert.AreEqual(5, evenNumbers.Count);
            Assert.IsTrue(evenNumbers.Contains(4));
            Assert.IsFalse(evenNumbers.Contains(5));
        }
        
        [Test]
        public void TestListSorting()
        {
            // Arrange
            List<int> numbers = new List<int> { 5, 2, 8, 1, 9 };
            
            // Act
            numbers.Sort();
            
            // Assert
            Assert.AreEqual(1, numbers[0]);
            Assert.AreEqual(9, numbers[numbers.Count - 1]);
        }
        
        [Test]
        public void TestQueueOperations()
        {
            // Arrange
            Queue<string> queue = new Queue<string>();
            
            // Act
            queue.Enqueue("First");
            queue.Enqueue("Second");
            queue.Enqueue("Third");
            
            string first = queue.Dequeue();
            
            // Assert
            Assert.AreEqual("First", first);
            Assert.AreEqual(2, queue.Count);
        }
        
        [Test]
        public void TestStackOperations()
        {
            // Arrange
            Stack<int> stack = new Stack<int>();
            
            // Act
            stack.Push(10);
            stack.Push(20);
            stack.Push(30);
            
            int top = stack.Pop();
            
            // Assert
            Assert.AreEqual(30, top);
            Assert.AreEqual(2, stack.Count);
        }
    }
}
