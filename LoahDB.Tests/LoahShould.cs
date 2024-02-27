using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LoahDB.Tests
{
    public class LoahShould
    {
        private const string TestRoot = "TestRoot";
        private const string TestKey = "TestKey";

        [Fact]
        public void GetAndSetWithoutEncryption()
        {
            // Arrange
            var expectedObject = new TestModel { Name = "TestName", Age = 30 };
            var loah = new Loah<TestModel>(TestKey, TestRoot);

            // Act
            loah.Set(expectedObject);
            var actualObject = loah.Get();

            // Assert
            Assert.Equal(expectedObject.Name, actualObject.Name);
            Assert.Equal(expectedObject.Age, actualObject.Age);
        }

        [Fact]
        public void GetAndSetWithEncryption()
        {
            var key = "Hello world!!!!!!!!!!!";
            // Arrange
            var expectedObject = new TestModel { Name = "TestName", Age = 30 };
            var loah = new Loah<TestModel>(TestKey, TestRoot, key);

            // Act
            loah.Set(expectedObject);
            var actualObject = loah.Get();

            // Assert
            Assert.Equal(expectedObject.Name, actualObject.Name);
            Assert.Equal(expectedObject.Age, actualObject.Age);
        }

        [Fact]
        public void ExistsAfterSet()
        {
            // Arrange
            var loah = new Loah<TestModel>(TestKey, TestRoot);

            // Act
            loah.Set(new TestModel { Name = "TestName", Age = 30 });
            var exists = LoahDb.Exists(TestKey, TestRoot);

            // Assert
            Assert.True(exists);
        }

        [Fact]
        public void DeleteAfterSet()
        {
            // Arrange
            var loah = new Loah<TestModel>(TestKey, TestRoot);
            loah.Set(new TestModel { Name = "TestName", Age = 30 });

            // Act
            LoahDb.Delete(TestKey, TestRoot);
            var exists = LoahDb.Exists(TestKey, TestRoot);

            // Assert
            Assert.False(exists);
        }

        [Fact]
        public void HasKeyListContainingExistingFiles()
        {
            // Arrange
            var loah = new Loah<TestModel>(TestKey, TestRoot);
            loah.Set(new TestModel { Name = "TestName", Age = 30 });

            // Act
            var keyList = LoahDb.KeyList(TestRoot);

            // Assert
            Assert.Contains(TestKey, keyList);
        }
    }
}
