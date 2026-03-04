using Xunit;
using XESmartTarget.Core.Utils;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace XESmartTarget.Tests.Utils
{
    public class SmartFormatHelperTests
    {
        [Fact]
        public void Format_WithStringDictionary_ReturnsFormattedString()
        {
            // Arrange
            var format = "Hello {name}, welcome to {place}!";
            var args = new Dictionary<string, string>
            {
                ["name"] = "Alice",
                ["place"] = "Wonderland"
            };

            // Act
            var result = SmartFormatHelper.Format(format, args);

            // Assert
            Xunit.Assert.Equal("Hello Alice, welcome to Wonderland!", result);
        }

        [Fact]
        public void Format_WithObjectDictionary_ReturnsFormattedString()
        {
            // Arrange
            var format = "Count: {count}, Price: {price}";
            var args = new Dictionary<string, object>
            {
                ["count"] = 42,
                ["price"] = 99.99
            };

            // Act
            var result = SmartFormatHelper.Format(format, args);

            // Assert
            Xunit.Assert.Equal("Count: 42, Price: 99.99", result);
        }

        [Fact]
        public void Format_WithMissingPlaceholder_ReturnsOriginalFormat()
        {
            // Arrange
            var format = "Hello {name}";
            var args = new Dictionary<string, string>
            {
                ["other"] = "value"
            };

            // Act
            var result = SmartFormatHelper.Format(format, args);

            // Assert
            Xunit.Assert.Equal(format, result);
        }

        [Fact]
        public void Format_WithEmptyDictionary_HandlesGracefully()
        {
            // Arrange
            var format = "No placeholders here";
            var args = new Dictionary<string, string>();

            // Act
            var result = SmartFormatHelper.Format(format, args);

            // Assert
            Xunit.Assert.Equal("No placeholders here", result);
        }

        [Fact]
        public void Format_WithNumberFormatting_WorksCorrectly()
        {
            // Arrange
            var format = "{count:0000}";
            var args = new Dictionary<string, object>
            {
                ["count"] = 42
            };

            // Act
            var result = SmartFormatHelper.Format(format, args);

            // Assert
            Xunit.Assert.Contains("0042", result);
        }

        [Fact]
        public void Format_WithSpecialCharacters_HandlesCorrectly()
        {
            // Arrange
            var format = "Value: {value}";
            var args = new Dictionary<string, string>
            {
                ["value"] = "Test\\Value"
            };

            // Act
            var result = SmartFormatHelper.Format(format, args);

            // Assert
            Xunit.Assert.Equal("Value: Test\\Value", result);
        }

        [Fact]
        public void Format_WithNullValue_HandlesGracefully()
        {
            // Arrange
            var format = "Value: {value}";
            var args = new Dictionary<string, object>
            {
                ["value"] = null!
            };

            // Act
            var result = SmartFormatHelper.Format(format, args);

            // Assert
            Xunit.Assert.NotNull(result);
        }

        [Fact]
        public async Task Format_ConcurrentCalls_ProduceCorrectResults()
        {
            // Arrange
            var format = "Thread: {thread}, Value: {value}";
            var tasks = new List<Task<(int thread, string result)>>();

            // Act - simulate concurrent formatting from multiple threads
            for (int i = 0; i < 100; i++)
            {
                int threadId = i;
                var task = Task.Run(() =>
                {
                    var args = new Dictionary<string, object>
                    {
                        ["thread"] = threadId,
                        ["value"] = $"Value_{threadId}"
                    };
                    var result = SmartFormatHelper.Format(format, args);
                    return (threadId, result);
                });
                tasks.Add(task);
            }

            var results = await Task.WhenAll(tasks);

            // Assert - verify each result is exactly what we expect
            foreach (var (threadId, result) in results)
            {
                var expectedResult = $"Thread: {threadId}, Value: Value_{threadId}";
                Xunit.Assert.Equal(expectedResult, result);
            }
        }

        [Fact]
        public void Format_SequentialCallsSameThread_NoStateLeakage()
        {
            // Arrange & Act
            var result1 = SmartFormatHelper.Format("Hello {name}", new Dictionary<string, string> { ["name"] = "Alice" });
            var result2 = SmartFormatHelper.Format("Goodbye {name}", new Dictionary<string, string> { ["name"] = "Bob" });
            var result3 = SmartFormatHelper.Format("Hi {name}", new Dictionary<string, string> { ["name"] = "Charlie" });

            // Assert - each call should be completely independent
            Xunit.Assert.Equal("Hello Alice", result1);
            Xunit.Assert.Equal("Goodbye Bob", result2);
            Xunit.Assert.Equal("Hi Charlie", result3);
            
            // Verify no cross-contamination
            Xunit.Assert.DoesNotContain("Bob", result1);
            Xunit.Assert.DoesNotContain("Alice", result2);
            Xunit.Assert.DoesNotContain("Charlie", result1);
        }

        [Fact]
        public void Format_RapidSequentialCalls_NoMemoryLeak()
        {
            // Arrange & Act - call many times to check for memory issues
            for (int i = 0; i < 10000; i++)
            {
                var args = new Dictionary<string, object>
                {
                    ["iteration"] = i,
                    ["value"] = $"Value_{i}"
                };
                var result = SmartFormatHelper.Format("Iteration: {iteration}, {value}", args);
                
                // Assert each result is correct
                Xunit.Assert.Contains($"Iteration: {i}", result);
                Xunit.Assert.Contains($"Value_{i}", result);
            }
            
            // If we got here without OutOfMemoryException, the test passes
            Xunit.Assert.True(true);
        }

        [Fact]
        public void Format_WithCharacterLiterals_DoesNotConvert()
        {
            // Arrange - test that ConvertCharacterStringLiterals = false works
            var format = "Value: {value}\\n";
            var args = new Dictionary<string, string>
            {
                ["value"] = "test"
            };

            // Act
            var result = SmartFormatHelper.Format(format, args);

            // Assert - should keep \\n as literal, not convert to newline
            Xunit.Assert.Contains("\\n", result);
            Xunit.Assert.DoesNotContain("\n", result);
        }

        [Fact]
        public void Format_ExceptionInFormatting_ReturnsOriginalFormat()
        {
            // Arrange - intentionally malformed format
            var format = "Bad format: {unclosed";
            var args = new Dictionary<string, string>
            {
                ["test"] = "value"
            };

            // Act
            var result = SmartFormatHelper.Format(format, args);

            // Assert - should return original format on exception
            Xunit.Assert.Equal(format, result);
        }

        [Theory]
        [InlineData("", "")]
        [InlineData("No placeholders", "No placeholders")]
        [InlineData("{name}", "Alice")]
        [InlineData("{first} {last}", "Alice Smith")]
        public void Format_VariousInputs_ProducesExpectedOutput(string format, string expected)
        {
            // Arrange
            var args = new Dictionary<string, string>
            {
                ["name"] = "Alice",
                ["first"] = "Alice",
                ["last"] = "Smith"
            };

            // Act
            var result = SmartFormatHelper.Format(format, args);

            // Assert
            if (expected == "")
            {
                Xunit.Assert.Equal(format, result);
            }
            else
            {
                Xunit.Assert.Contains(expected, result);
            }
        }
    }
}