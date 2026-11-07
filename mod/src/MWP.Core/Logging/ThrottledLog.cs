using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MWP.Core
{
    /// <summary>
    /// Per-key log throttle (REQ-OPS-03, REQ-NFR-08): each stable <see cref="MwpLogKeys"/> key may
    /// emit at most one message per window, so a broken tick loop cannot flood the player log.
    /// The clock is injectable for deterministic tests.
    /// </summary>
    public sealed class ThrottledLog
    {
        private readonly IMwpLog _inner;
        private readonly double _throttleSeconds;
        private readonly Func<double> _clock;
        private readonly Dictionary<string, double> _lastLogged = new Dictionary<string, double>(StringComparer.Ordinal);

        /// <summary>Creates a throttled wrapper around a real logger using a monotonic clock.</summary>
        /// <param name="inner">Logger that performs the actual output.</param>
        /// <param name="throttleSeconds">Minimum seconds between two messages of the same key; &lt;= 0 disables throttling.</param>
        public ThrottledLog(IMwpLog inner, double throttleSeconds)
            : this(inner, throttleSeconds, MonotonicSeconds)
        {
        }

        /// <summary>Creates a throttled wrapper with an explicit clock (unit tests).</summary>
        /// <param name="inner">Logger that performs the actual output.</param>
        /// <param name="throttleSeconds">Minimum seconds between two messages of the same key; &lt;= 0 disables throttling.</param>
        /// <param name="clock">Returns the current time in seconds; must be non-decreasing.</param>
        public ThrottledLog(IMwpLog inner, double throttleSeconds, Func<double> clock)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _throttleSeconds = double.IsNaN(throttleSeconds) || throttleSeconds < 0.0 ? 0.0 : throttleSeconds;
        }

        /// <summary>Emits an informational message unless the key is still inside its window.</summary>
        /// <param name="key">Stable throttle key.</param>
        /// <param name="message">Message text.</param>
        public void Info(string key, string message) => Emit(_inner.Info, key, message);

        /// <summary>Emits a warning unless the key is still inside its window.</summary>
        /// <param name="key">Stable throttle key.</param>
        /// <param name="message">Message text.</param>
        public void Warn(string key, string message) => Emit(_inner.Warn, key, message);

        /// <summary>Emits an error unless the key is still inside its window.</summary>
        /// <param name="key">Stable throttle key.</param>
        /// <param name="message">Message text.</param>
        public void Error(string key, string message) => Emit(_inner.Error, key, message);

        /// <summary>Emits a compatibility message unless the key is still inside its window.</summary>
        /// <param name="key">Stable throttle key.</param>
        /// <param name="message">Message text.</param>
        public void Compat(string key, string message) => Emit(_inner.Compat, key, message);

        /// <summary>
        /// Test hook: reports whether a message for <paramref name="key"/> would be emitted at
        /// <paramref name="nowSeconds"/>, and records that time when it returns true.
        /// </summary>
        /// <param name="key">Stable throttle key; null is treated as the empty key.</param>
        /// <param name="nowSeconds">Current time in seconds.</param>
        /// <returns>True when the caller should log now.</returns>
        public bool ShouldLog(string key, double nowSeconds)
        {
            string effectiveKey = key ?? string.Empty;
            if (double.IsNaN(nowSeconds) || double.IsPositiveInfinity(nowSeconds))
            {
                // A broken clock must not silence diagnostics permanently.
                return true;
            }

            if (_throttleSeconds <= 0.0)
            {
                return true;
            }

            if (_lastLogged.TryGetValue(effectiveKey, out double last))
            {
                double elapsed = nowSeconds - last;
                if (elapsed >= _throttleSeconds || elapsed < 0.0)
                {
                    // A negative delta means the clock was reset/wrapped; log instead of stalling.
                    _lastLogged[effectiveKey] = nowSeconds;
                    return true;
                }

                return false;
            }

            _lastLogged[effectiveKey] = nowSeconds;
            return true;
        }

        private void Emit(Action<string, string> sink, string key, string message)
        {
            if (ShouldLog(key, _clock()))
            {
                sink(key, message);
            }
        }

        private static double MonotonicSeconds()
        {
            return Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        }
    }
}
