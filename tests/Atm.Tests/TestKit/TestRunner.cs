// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// TestRunner.cs
//
// What this file does: it is the entry point of the test project. It finds every
// method marked [Fact], runs each one, prints a green or red line for it, and exits
// with code 1 if anything was red. That exit code is what makes ./scripts/test.sh
// usable as a gate - a script or a build step can act on it without reading text.
//
// Why we run tests in a fixed order: test order must not change between two runs of
// the same code. If it did, a test that only fails when it runs after another test
// would appear and disappear at random, and this project has a rule against unstable
// tests: a test must not be flaky. Sorting by type name, then method name,
// removes the question.
//
// Why each test gets a fresh instance of its class: so that one test cannot leave
// state behind that changes the outcome of the next one. A test that passes only
// because an earlier test ran is not evidence of anything.
//
// Careful: this runner deliberately does NOT run tests in parallel. The failure
// scenarios in this project involve a virtual clock and a simulated network, and two
// tests sharing those at the same time would produce results nobody could reproduce.

using System.Diagnostics;
using System.Reflection;
using Xunit;

namespace Atm.Tests;

/// <summary>Discovers and runs every [Fact] in this assembly.</summary>
public static class TestRunner
{
    public static int Main()
    {
        var tests = Discover();
        var stopwatch = Stopwatch.StartNew();
        int passed = 0, failed = 0, skipped = 0;

        Console.WriteLine($"ATMSIM testleri — {tests.Count} test bulundu.");
        Console.WriteLine(new string('-', 60));

        foreach (var (type, method, skip) in tests)
        {
            var name = $"{type.Name}.{method.Name}";

            if (skip is not null)
            {
                skipped++;
                Console.WriteLine($"ATLANDI  {name} — {skip}");
                continue;
            }

            try
            {
                var instance = method.IsStatic ? null : Activator.CreateInstance(type);
                var result = method.Invoke(instance, null);

                // A test that returns a Task has not finished when Invoke returns. If
                // the runner walked away here, an assertion that failed a millisecond
                // later would be lost and the test would report green. Waiting is not
                // a nicety: a silently green test is worse than a missing one.
                if (result is Task task)
                {
                    task.GetAwaiter().GetResult();
                }
                passed++;
                Console.WriteLine($"GEÇTİ    {name}");
            }
            catch (TargetInvocationException ex)
            {
                // Reflection wraps whatever the test threw. The inner exception is the
                // one the reader cares about; the wrapper says nothing useful.
                failed++;
                Report(name, ex.InnerException ?? ex);
            }
            catch (Exception ex)
            {
                // The test could not even be started - usually a missing parameterless
                // constructor. That is a broken test, and it counts as red.
                failed++;
                Report(name, ex);
            }
        }

        stopwatch.Stop();
        Console.WriteLine(new string('-', 60));
        Console.WriteLine(
            $"Geçti: {passed}  Kaldı: {failed}  Atlandı: {skipped}  " +
            $"({stopwatch.ElapsedMilliseconds} ms)");

        return failed == 0 ? 0 : 1;
    }

    /// <summary>
    /// Every [Fact] method in this assembly, in a fixed order. Methods that take
    /// parameters are rejected loudly rather than skipped: a test the runner quietly
    /// ignores is a test everybody believes is running.
    /// </summary>
    private static List<(Type Type, MethodInfo Method, string? Skip)> Discover()
    {
        var found = new List<(Type, MethodInfo, string?)>();

        foreach (var type in Assembly.GetExecutingAssembly().GetTypes().OrderBy(t => t.FullName))
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                                       .OrderBy(m => m.Name))
            {
                var fact = method.GetCustomAttribute<FactAttribute>();
                if (fact is null)
                {
                    continue;
                }

                if (method.GetParameters().Length != 0)
                {
                    throw new InvalidOperationException(
                        $"{type.Name}.{method.Name} parametre alıyor. [Fact] parametresiz olmalı.");
                }

                found.Add((type, method, fact.Skip));
            }
        }

        return found;
    }

    private static void Report(string name, Exception ex)
    {
        Console.WriteLine($"KALDI    {name}");
        foreach (var line in (ex.Message ?? string.Empty).Split(Environment.NewLine))
        {
            Console.WriteLine($"         {line}");
        }

        if (ex is not AssertionFailedException)
        {
            // An unexpected exception (not a failed check) - the stack trace is the
            // only thing that says where it came from.
            Console.WriteLine($"         {ex.GetType().Name}");
            Console.WriteLine($"         {ex.StackTrace?.Split(Environment.NewLine).FirstOrDefault()?.Trim()}");
        }
    }
}
