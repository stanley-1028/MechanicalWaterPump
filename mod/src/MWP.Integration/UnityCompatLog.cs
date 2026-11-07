using System;
using MWP.Core;
using UnityEngine;

namespace MWP.Integration
{
    /// <summary>
    /// Game-log sink for the compatibility assembly. MWP.Integration deliberately does not depend
    /// on MWP.Gameplay (both assemblies only reference MWP.Core), so it owns its own small
    /// <see cref="IMwpLog"/> instead of injecting the gameplay registrar; the messages still follow
    /// the same category convention (<c>MWP/Info</c>, <c>MWP/Warn</c>, <c>MWP/Error</c>,
    /// <c>MWP/Compat</c>, REQ-OPS-03).
    /// </summary>
    internal sealed class UnityCompatLog : IMwpLog
    {
        /// <inheritdoc />
        public void Info(string key, string message) => Write(LogLevel.Info, key, message);

        /// <inheritdoc />
        public void Warn(string key, string message) => Write(LogLevel.Warn, key, message);

        /// <inheritdoc />
        public void Error(string key, string message) => Write(LogLevel.Error, key, message);

        /// <inheritdoc />
        public void Compat(string key, string message) => Write(LogLevel.Compat, key, message);

        private static void Write(LogLevel level, string key, string message)
        {
            // A broken Unity logger must never escape into the mod (a mod log call is best effort).
            try
            {
                string line = "[MWP/" + LevelName(level) + "][" + (key ?? string.Empty) + "] " + (message ?? string.Empty);
                switch (level)
                {
                    case LogLevel.Warn:
                        Debug.LogWarning(line);
                        break;
                    case LogLevel.Error:
                        Debug.LogError(line);
                        break;
                    default:
                        Debug.Log(line);
                        break;
                }
            }
            catch
            {
                // Ignored on purpose: diagnostics are not allowed to break gameplay (REQ-SAFE-01).
            }
        }

        private static string LevelName(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Warn:
                    return "Warn";
                case LogLevel.Error:
                    return "Error";
                case LogLevel.Compat:
                    return "Compat";
                default:
                    return "Info";
            }
        }

        private enum LogLevel
        {
            Info,
            Warn,
            Error,
            Compat
        }
    }
}
