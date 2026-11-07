using System;
using MWP.Core;
using UnityEngine;

namespace MWP.Gameplay.Logging
{
    /// <summary>
    /// Writes MWP diagnostics into the Unity player log, the only log the game exposes.
    ///
    /// Every write is guarded: a logging failure must never escape into the simulation
    /// (REQ-SAFE-02). Throttling is not done here — <see cref="ThrottledLog"/> wraps this sink so
    /// the per-key 30 s window is shared by the whole mod (REQ-OPS-03).
    /// </summary>
    public sealed class UnityMwpLog : IMwpLog
    {
        /// <inheritdoc />
        public void Info(string key, string message) => Write(Level.Info, key, message);

        /// <inheritdoc />
        public void Warn(string key, string message) => Write(Level.Warn, key, message);

        /// <inheritdoc />
        public void Error(string key, string message) => Write(Level.Error, key, message);

        /// <inheritdoc />
        public void Compat(string key, string message) => Write(Level.Compat, key, message);

        private static void Write(Level level, string key, string message)
        {
            try
            {
                string line = "[MWP/" + LevelName(level) + "][" + (key ?? string.Empty) + "] "
                    + (message ?? string.Empty);
                switch (level)
                {
                    case Level.Warn:
                        Debug.LogWarning(line);
                        break;
                    case Level.Error:
                        Debug.LogError(line);
                        break;
                    default:
                        Debug.Log(line);
                        break;
                }
            }
            catch (Exception)
            {
                // Diagnostics are best effort only (REQ-SAFE-01).
            }
        }

        private static string LevelName(Level level)
        {
            switch (level)
            {
                case Level.Warn:
                    return "Warn";
                case Level.Error:
                    return "Error";
                case Level.Compat:
                    return "Compat";
                default:
                    return "Info";
            }
        }

        private enum Level
        {
            Info,
            Warn,
            Error,
            Compat
        }
    }
}
