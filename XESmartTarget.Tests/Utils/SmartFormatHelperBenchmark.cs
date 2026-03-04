using Xunit;
using Xunit.Abstractions;
using XESmartTarget.Core.Utils;
using System.Collections.Generic;
using System.Diagnostics;

namespace XESmartTarget.Tests.Utils
{
    public class SmartFormatHelperBenchmark
    {
        private readonly ITestOutputHelper _output;

        public SmartFormatHelperBenchmark(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Benchmark_Format_Performance()
        {
            // Arrange
            var format = "Event: {name}, Time: {timestamp}, Duration: {duration}";
            var iterations = 100_000;

            // Warmup
            for (int i = 0; i < 1000; i++)
            {
                SmartFormatHelper.Format(format, new Dictionary<string, object>
                {
                    ["name"] = "test_event",
                    ["timestamp"] = DateTime.Now,
                    ["duration"] = 123
                });
            }

            // Benchmark
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                var args = new Dictionary<string, object>
                {
                    ["name"] = $"event_{i}",
                    ["timestamp"] = DateTime.Now,
                    ["duration"] = i
                };
                SmartFormatHelper.Format(format, args);
            }
            sw.Stop();

            // Report
            var totalMs = sw.ElapsedMilliseconds;
            var avgMicroseconds = (totalMs * 1000.0) / iterations;
            var throughput = iterations / (totalMs / 1000.0);

            _output.WriteLine($"Total time: {totalMs} ms");
            _output.WriteLine($"Iterations: {iterations:N0}");
            _output.WriteLine($"Average: {avgMicroseconds:F2} μs per call");
            _output.WriteLine($"Throughput: {throughput:N0} calls/second");

            // Assert reasonable performance
            // Should be able to do at least 10,000 formats per second
            Xunit.Assert.True(throughput > 10_000, $"Performance too slow: {throughput:N0} calls/sec");
        }
    }
}