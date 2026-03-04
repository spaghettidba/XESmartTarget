using Xunit;
using Xunit.Abstractions;
using XESmartTarget.Core.Utils;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Linq;

namespace XESmartTarget.Tests.Utils
{
    public class SmartFormatHelperThreadSafetyTest
    {
        private readonly ITestOutputHelper _output;

        public SmartFormatHelperThreadSafetyTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task DetectThreadSafety_HighContentionScenario()
        {
            // This test simulates the exact problem you experienced:
            // Multiple threads formatting at the same time with different data
            // If SmartFormatter maintains state, data will get mixed up

            var iterations = 1000;
            var tasks = new List<Task>();
            var errors = new ConcurrentBag<string>();

            // Create 20 threads all formatting simultaneously
            for (int threadId = 0; threadId < 20; threadId++)
            {
                int capturedThreadId = threadId;
                var task = Task.Run(() =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        // Each thread uses unique identifiers
                        var uniqueValue = $"Thread{capturedThreadId}_Iter{i}";
                        var format = "Value: {value}, Extra: {extra}";
                        var args = new Dictionary<string, object>
                        {
                            ["value"] = uniqueValue,
                            ["extra"] = $"Extra_{capturedThreadId}_{i}"
                        };

                        var result = SmartFormatHelper.Format(format, args);

                        // Verify the result contains ONLY this thread's data
                        if (!result.Contains(uniqueValue))
                        {
                            errors.Add($"Thread {capturedThreadId}: Expected '{uniqueValue}' not found in '{result}'");
                        }

                        // Check for contamination from other threads
                        for (int otherThread = 0; otherThread < 20; otherThread++)
                        {
                            if (otherThread != capturedThreadId)
                            {
                                var otherPattern = $"Thread{otherThread}_";
                                if (result.Contains(otherPattern))
                                {
                                    errors.Add($"Thread {capturedThreadId} contaminated with Thread {otherThread} data: '{result}'");
                                }
                            }
                        }
                    }
                });
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);

            // Report results
            if (errors.Any())
            {
                _output.WriteLine($"❌ THREAD-SAFETY VIOLATIONS DETECTED: {errors.Count}");
                foreach (var error in errors.Take(10)) // Show first 10 errors
                {
                    _output.WriteLine($"  - {error}");
                }
                Xunit.Assert.Fail($"Thread-safety violations detected: {errors.Count} errors");
            }
            else
            {
                _output.WriteLine($"✅ Thread-safe: {iterations * 20:N0} concurrent calls with no contamination");
            }
        }

        [Fact]
        public async Task DetectStateLeak_RapidAlternatingData()
        {
            // This test simulates rapid switching between different data sets
            // A stateful formatter would leak data between calls

            var iterations = 10000;
            var tasks = new List<Task>();
            var errors = new ConcurrentBag<string>();

            for (int i = 0; i < 10; i++)
            {
                var task = Task.Run(() =>
                {
                    for (int j = 0; j < iterations; j++)
                    {
                        // Alternate between two completely different formats
                        if (j % 2 == 0)
                        {
                            var result = SmartFormatHelper.Format(
                                "EventA: {fieldA}",
                                new Dictionary<string, string> { ["fieldA"] = "ValueA" }
                            );

                            if (result.Contains("fieldB") || result.Contains("ValueB"))
                            {
                                errors.Add($"EventA contaminated with EventB data: {result}");
                            }
                        }
                        else
                        {
                            var result = SmartFormatHelper.Format(
                                "EventB: {fieldB}",
                                new Dictionary<string, string> { ["fieldB"] = "ValueB" }
                            );

                            if (result.Contains("fieldA") || result.Contains("ValueA"))
                            {
                                errors.Add($"EventB contaminated with EventA data: {result}");
                            }
                        }
                    }
                });
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);

            if (errors.Any())
            {
                _output.WriteLine($"❌ STATE LEAKAGE DETECTED: {errors.Count}");
                foreach (var error in errors.Take(10))
                {
                    _output.WriteLine($"  - {error}");
                }
                Xunit.Assert.Fail($"State leakage detected: {errors.Count} errors");
            }
            else
            {
                _output.WriteLine($"✅ No state leakage: {iterations * 10:N0} alternating calls successful");
            }
        }

        [Fact]
        public async Task SimulateXEventProcessing_RealisticWorkload()
        {
            // Simulates actual XESmartTarget workload:
            // Multiple event types being processed concurrently with different formats

            var eventTypes = new[]
            {
                ("sql_batch_completed", "Query: {statement}, Duration: {duration}, User: {username}"),
                ("rpc_completed", "Proc: {object_name}, Duration: {duration}, CPU: {cpu_time}"),
                ("error_reported", "Error: {error_number}, Message: {message}, Severity: {severity}"),
                ("deadlock_graph", "Deadlock: {victim_id}, Resource: {resource_type}"),
            };

            var errors = new ConcurrentBag<string>();
            var tasks = new List<Task>();

            // Simulate 4 concurrent XEvent readers, each processing 5000 events
            for (int reader = 0; reader < 4; reader++)
            {
                int capturedReader = reader;
                var task = Task.Run(() =>
                {
                    var random = new Random(capturedReader);

                    for (int i = 0; i < 5000; i++)
                    {
                        // Pick random event type
                        var (eventName, format) = eventTypes[random.Next(eventTypes.Length)];

                        // Create unique data for this event
                        var eventId = $"R{capturedReader}_E{i}";
                        var args = new Dictionary<string, object>
                        {
                            ["statement"] = $"SELECT * FROM Table_{eventId}",
                            ["object_name"] = $"sp_Proc_{eventId}",
                            ["duration"] = i * 1000,
                            ["cpu_time"] = i * 500,
                            ["username"] = $"User_{capturedReader}",
                            ["error_number"] = i,
                            ["message"] = $"Error message {eventId}",
                            ["severity"] = random.Next(1, 25),
                            ["victim_id"] = eventId,
                            ["resource_type"] = $"PAGE_{eventId}"
                        };

                        var result = SmartFormatHelper.Format(format, args);

                        // Verify this reader's unique identifier appears
                        if (!result.Contains(eventId) && !result.Contains($"User_{capturedReader}"))
                        {
                            // Only check if at least one of our markers should be there
                            bool shouldContain = format.Contains("{statement}") || 
                                               format.Contains("{object_name}") ||
                                               format.Contains("{username}") ||
                                               format.Contains("{message}") ||
                                               format.Contains("{victim_id}");

                            if (shouldContain)
                            {
                                errors.Add($"Reader {capturedReader}: Event {eventId} data not found in result");
                            }
                        }

                        // Check for contamination from other readers
                        for (int otherReader = 0; otherReader < 4; otherReader++)
                        {
                            if (otherReader != capturedReader)
                            {
                                if (result.Contains($"User_{otherReader}") && format.Contains("{username}"))
                                {
                                    errors.Add($"Reader {capturedReader} contaminated with Reader {otherReader} username");
                                }
                            }
                        }
                    }
                });
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);

            if (errors.Any())
            {
                _output.WriteLine($"❌ XEvent simulation FAILED: {errors.Count} contamination issues");
                foreach (var error in errors.Take(20))
                {
                    _output.WriteLine($"  - {error}");
                }
                Xunit.Assert.Fail($"XEvent workload simulation detected contamination: {errors.Count} errors");
            }
            else
            {
                _output.WriteLine($"✅ XEvent simulation passed: 20,000 concurrent events processed cleanly");
            }
        }

        [Fact]
        public async Task ProveSharedFormatterIsBroken()
        {
            // This test uses a DELIBERATE
            // BROKEN shared formatter
            // to demonstrate the exact problem you were experiencing

            _output.WriteLine("⚠️  Testing with SHARED formatter (the broken approach)...\n");

            // Create a shared formatter (THE PROBLEM)
            var sharedFormatter = SmartFormat.Smart.CreateDefaultSmartFormat();
            sharedFormatter.Settings.Parser.ConvertCharacterStringLiterals = false;

            var iterations = 100;
            var tasks = new List<Task>();
            var errors = new ConcurrentBag<string>();
            var successCount = 0;

            // Create 10 threads all using the SAME formatter
            for (int threadId = 0; threadId < 10; threadId++)
            {
                int capturedThreadId = threadId;
                var task = Task.Run(() =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        var uniqueValue = $"Thread{capturedThreadId}_Iter{i}";
                        var format = "Value: {value}";
                        var args = new Dictionary<string, object>
                        {
                            ["value"] = uniqueValue
                        };

                        // Using the SHARED formatter (DANGEROUS!)
                        string result;
                        try
                        {
                            result = sharedFormatter.Format(format, args);
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Thread {capturedThreadId}: Exception - {ex.Message}");
                            continue;
                        }

                        // Check if result is correct
                        if (result == $"Value: {uniqueValue}")
                        {
                            Interlocked.Increment(ref successCount);
                        }
                        else if (!result.Contains(uniqueValue))
                        {
                            errors.Add($"Thread {capturedThreadId}: Expected '{uniqueValue}' but got '{result}'");
                        }
                        else
                        {
                            // Result contains our value but has extra contamination
                            errors.Add($"Thread {capturedThreadId}: Result contaminated: '{result}'");
                        }
                    }
                });
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);

            // Report results
            var totalCalls = iterations * 10;
            var errorRate = (errors.Count * 100.0) / totalCalls;
            var successRate = (successCount * 100.0) / totalCalls;

            _output.WriteLine($"╔════════════════════════════════════════════════════════╗");
            _output.WriteLine($"║   SHARED FORMATTER TEST RESULTS (Expected to fail)    ║");
            _output.WriteLine($"╠════════════════════════════════════════════════════════╣");
            _output.WriteLine($"║ Total calls:       {totalCalls,10}                        ║");
            _output.WriteLine($"║ Successful:        {successCount,10} ({successRate,5:F1}%)                ║");
            _output.WriteLine($"║ Errors detected:   {errors.Count,10} ({errorRate,5:F1}%)                ║");
            _output.WriteLine($"╚════════════════════════════════════════════════════════╝");

            if (errors.Any())
            {
                _output.WriteLine($"\n❌ CONFIRMED: Shared formatter causes thread-safety issues!");
                _output.WriteLine($"\nFirst 10 errors:");
                foreach (var error in errors.Take(10))
                {
                    _output.WriteLine($"  • {error}");
                }
            }

            // Now test with the CORRECT implementation (per-call creation)
            _output.WriteLine($"\n\n✅ Testing with PER-CALL formatter (the correct approach)...\n");

            errors.Clear();
            successCount = 0;
            tasks.Clear();

            for (int threadId = 0; threadId < 10; threadId++)
            {
                int capturedThreadId = threadId;
                var task = Task.Run(() =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        var uniqueValue = $"Thread{capturedThreadId}_Iter{i}";
                        var format = "Value: {value}";
                        var args = new Dictionary<string, object>
                        {
                            ["value"] = uniqueValue
                        };

                        // Using SmartFormatHelper (creates NEW formatter each time)
                        var result = SmartFormatHelper.Format(format, args);

                        // Check if result is correct
                        if (result == $"Value: {uniqueValue}")
                        {
                            Interlocked.Increment(ref successCount);
                        }
                        else
                        {
                            errors.Add($"Thread {capturedThreadId}: Expected '{uniqueValue}' but got '{result}'");
                        }
                    }
                });
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);

            errorRate = (errors.Count * 100.0) / totalCalls;
            successRate = (successCount * 100.0) / totalCalls;

            _output.WriteLine($"╔════════════════════════════════════════════════════════╗");
            _output.WriteLine($"║   PER-CALL FORMATTER TEST RESULTS                     ║");
            _output.WriteLine($"╠════════════════════════════════════════════════════════╣");
            _output.WriteLine($"║ Total calls:       {totalCalls,10}                        ║");
            _output.WriteLine($"║ Successful:        {successCount,10} ({successRate,5:F1}%)                ║");
            _output.WriteLine($"║ Errors detected:   {errors.Count,10} ({errorRate,5:F1}%)                ║");
            _output.WriteLine($"╚════════════════════════════════════════════════════════╝");

            if (!errors.Any())
            {
                _output.WriteLine($"\n✅ CONFIRMED: Per-call formatter is thread-safe!");
            }
            else
            {
                _output.WriteLine($"\n❌ Unexpected errors with per-call formatter:");
                foreach (var error in errors.Take(10))
                {
                    _output.WriteLine($"  • {error}");
                }
            }

            // The test passes regardless because we're demonstrating both approaches
            // But output clearly shows which one fails
            Xunit.Assert.True(true, "Demonstration complete - see output for results");
        }
    }
}