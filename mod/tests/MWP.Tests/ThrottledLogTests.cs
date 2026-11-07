using System;
using MWP.Core;

namespace MWP.Tests
{
    /// <summary>Unit tests for <see cref="ThrottledLog"/>: 30-second per-key throttling with an injected clock.</summary>
    internal static class ThrottledLogTests
    {
        internal static void Run(TestRunner t)
        {
            WindowTests(t);
            LevelTests(t);
            ShouldLogTests(t);
            ClockTests(t);
            ConstructorTests(t);

        }
        /// <summary>Test group: WindowTests.</summary>
        private static void WindowTests(TestRunner t)
        {
            t.Run("First message logs and repeats inside the window are suppressed", () =>
            {
                var log = new RecordingLog();
                double now = 100.0;
                var throttled = new ThrottledLog(log, 30.0, () => now);
                throttled.Warn("MWP/Warn/Test", "one");
                throttled.Warn("MWP/Warn/Test", "two");
                Assert.Equal(1, log.Entries.Count, "only one entry");
                now += 29.9;
                throttled.Warn("MWP/Warn/Test", "three");
                Assert.Equal(1, log.Entries.Count, "suppressed inside window");
                now += 0.1;
                throttled.Warn("MWP/Warn/Test", "four");
                Assert.Equal(2, log.Entries.Count, "logged at the window edge");
                Assert.Contains(log.Entries[1], "four", "second message content");
            });

            t.Run("Different keys are throttled independently", () =>
            {
                var log = new RecordingLog();
                double now = 0.0;
                var throttled = new ThrottledLog(log, 30.0, () => now);
                throttled.Info("A", "a");
                throttled.Info("B", "b");
                Assert.Equal(2, log.Entries.Count, "both keys logged");
            });
        }

        /// <summary>Test group: LevelTests.</summary>
        private static void LevelTests(TestRunner t)
        {
            t.Run("All four levels are forwarded", () =>
            {
                var log = new RecordingLog();
                var throttled = new ThrottledLog(log, 30.0, () => 0.0);
                throttled.Info("I", "info");
                throttled.Warn("W", "warn");
                throttled.Error("E", "error");
                throttled.Compat("C", "compat");
                Assert.Equal(4, log.Entries.Count, "all forwarded");
                Assert.Contains(log.Entries[0], "Info|I|info", "info level");
                Assert.Contains(log.Entries[1], "Warn|W|warn", "warn level");
                Assert.Contains(log.Entries[2], "Error|E|error", "error level");
                Assert.Contains(log.Entries[3], "Compat|C|compat", "compat level");
            });
        }

        /// <summary>Test group: ShouldLogTests.</summary>
        private static void ShouldLogTests(TestRunner t)
        {
            t.Run("ShouldLog permits one message per window", () =>
            {
                var throttled = new ThrottledLog(new RecordingLog(), 30.0, () => 0.0);
                Assert.True(throttled.ShouldLog("k", 0.0), "t=0");
                Assert.False(throttled.ShouldLog("k", 10.0), "t=10");
                Assert.False(throttled.ShouldLog("k", 29.9), "t=29.9");
                Assert.True(throttled.ShouldLog("k", 30.0), "t=30");
                Assert.False(throttled.ShouldLog("k", 45.0), "t=45");
                Assert.True(throttled.ShouldLog("k", 60.0), "t=60");
            });

            t.Run("Zero throttle always logs", () =>
            {
                var log = new RecordingLog();
                var throttled = new ThrottledLog(log, 0.0, () => 0.0);
                throttled.Info("k", "one");
                throttled.Info("k", "two");
                Assert.Equal(2, log.Entries.Count, "no throttling");
            });

            t.Run("Negative throttle behaves like disabled", () =>
            {
                var log = new RecordingLog();
                var throttled = new ThrottledLog(log, -5.0, () => 0.0);
                throttled.Info("k", "one");
                throttled.Info("k", "two");
                Assert.Equal(2, log.Entries.Count, "no throttling");
            });

            t.Run("NaN throttle behaves like disabled", () =>
            {
                var log = new RecordingLog();
                var throttled = new ThrottledLog(log, double.NaN, () => 0.0);
                throttled.Info("k", "one");
                throttled.Info("k", "two");
                Assert.Equal(2, log.Entries.Count, "no throttling");
            });
        }

        /// <summary>Test group: ClockTests.</summary>
        private static void ClockTests(TestRunner t)
        {
            t.Run("A clock that goes backwards logs again", () =>
            {
                var log = new RecordingLog();
                double now = 100.0;
                var throttled = new ThrottledLog(log, 30.0, () => now);
                throttled.Warn("k", "one");
                now = 90.0;
                throttled.Warn("k", "two");
                Assert.Equal(2, log.Entries.Count, "reset clock logs");
            });

            t.Run("A NaN clock timestamp does not silence diagnostics", () =>
            {
                var throttled = new ThrottledLog(new RecordingLog(), 30.0, () => 0.0);
                Assert.True(throttled.ShouldLog("k", double.NaN), "NaN timestamp logs");
                Assert.True(throttled.ShouldLog("k", 0.0), "still unseeded after NaN");
                Assert.False(throttled.ShouldLog("k", 1.0), "window now active");
            });

            t.Run("Default constructor uses a monotonic clock", () =>
            {
                var log = new RecordingLog();
                var throttled = new ThrottledLog(log, 30.0);
                throttled.Info("k", "one");
                throttled.Info("k", "two");
                Assert.Equal(1, log.Entries.Count, "throttled immediately");
            });
        }

        /// <summary>Test group: ConstructorTests.</summary>
        private static void ConstructorTests(TestRunner t)
        {
            t.Run("Null logger is rejected", () =>
                Assert.Throws<ArgumentNullException>(
                    () => new ThrottledLog(null!, 30.0),
                    "null logger"));
            t.Run("Null clock is rejected", () =>
                Assert.Throws<ArgumentNullException>(
                    () => new ThrottledLog(new RecordingLog(), 30.0, null!),
                    "null clock"));
        }
    }
}
