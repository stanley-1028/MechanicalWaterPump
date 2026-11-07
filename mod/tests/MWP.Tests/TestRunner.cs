using System;
using System.Collections.Generic;

namespace MWP.Tests
{
    /// <summary>
    /// Tiny built-in test runner (zero NuGet on purpose, REQ-NFR-07). Each test is a delegate; a
    /// thrown exception fails that test only. <see cref="Report"/> prints the final summary line
    /// and returns the process exit code.
    /// </summary>
    internal sealed class TestRunner
    {
        private int _passed;
        private int _failed;

        /// <summary>Number of passing tests.</summary>
        internal int Passed => _passed;

        /// <summary>Number of failing tests.</summary>
        internal int Failed => _failed;

        /// <summary>Total number of executed tests.</summary>
        internal int Total => _passed + _failed;

        /// <summary>Runs one test and records the outcome.</summary>
        /// <param name="name">Test name shown on failure.</param>
        /// <param name="test">Test body; throw to fail.</param>
        internal void Run(string name, Action test)
        {
            if (test == null)
            {
                throw new ArgumentNullException(nameof(test));
            }

            try
            {
                test();
                _passed++;
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("FAIL " + name);
                Console.WriteLine("     " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>Prints the summary and returns the exit code (0 = all passed).</summary>
        internal int Report()
        {
            int total = Total;
            if (_failed == 0)
            {
                Console.WriteLine("PASS " + _passed + "/" + total);
                return 0;
            }

            Console.WriteLine("FAIL " + _passed + "/" + total);
            return 1;
        }
    }

    /// <summary>Assertions that throw a descriptive exception on failure.</summary>
    internal static class Assert
    {
        /// <summary>Fails unless the condition holds.</summary>
        internal static void True(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception(message + " (expected true)");
            }
        }

        /// <summary>Fails unless the condition is false.</summary>
        internal static void False(bool condition, string message)
        {
            if (condition)
            {
                throw new Exception(message + " (expected false)");
            }
        }

        /// <summary>Fails unless both values are equal (exact comparison).</summary>
        internal static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception(message + " (expected " + expected + ", actual " + actual + ")");
            }
        }

        /// <summary>Fails unless the value is within tolerance of the expected value.</summary>
        internal static void Near(double expected, double actual, double tolerance, string message)
        {
            if (double.IsNaN(actual) || Math.Abs(expected - actual) > tolerance)
            {
                throw new Exception(
                    message + " (expected " + expected + " +/- " + tolerance + ", actual " + actual + ")");
            }
        }

        /// <summary>Fails unless the haystack contains the needle (ordinal comparison).</summary>
        internal static void Contains(string haystack, string needle, string message)
        {
            if (haystack == null || haystack.IndexOf(needle, StringComparison.Ordinal) < 0)
            {
                throw new Exception(message + " (missing '" + needle + "' in '" + haystack + "')");
            }
        }

        /// <summary>Fails when the value is null.</summary>
        internal static void NotNull(object? value, string message)
        {
            if (value == null)
            {
                throw new Exception(message + " (was null)");
            }
        }

        /// <summary>Fails unless <paramref name="action"/> throws <typeparamref name="TException"/>.</summary>
        internal static void Throws<TException>(Action action, string message)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new Exception(message + " (threw " + ex.GetType().Name + " instead of " + typeof(TException).Name + ")");
            }

            throw new Exception(message + " (did not throw " + typeof(TException).Name + ")");
        }
    }
}
