using System;
using System.Collections.Generic;

namespace MWP.Core
{
    /// <summary>Throttled logger abstraction so Core never references UnityEngine (REQ-OPS-03).</summary>
    public interface IMwpLog
    {
        /// <summary>Informational message.</summary>
        /// <param name="key">Stable throttle key, see <see cref="MwpLogKeys"/>.</param>
        /// <param name="message">Message text.</param>
        void Info(string key, string message);

        /// <summary>Warning that needs attention but is recoverable.</summary>
        /// <param name="key">Stable throttle key.</param>
        /// <param name="message">Message text.</param>
        void Warn(string key, string message);

        /// <summary>Error; the system degraded but kept running.</summary>
        /// <param name="key">Stable throttle key.</param>
        /// <param name="message">Message text.</param>
        void Error(string key, string message);

        /// <summary>Compatibility-detection message.</summary>
        /// <param name="key">Stable throttle key.</param>
        /// <param name="message">Message text.</param>
        void Compat(string key, string message);
    }

    /// <summary>Stable log keys (REQ-NFR-08). Do not build log strings inline.</summary>
    public static class MwpLogKeys
    {
        /// <summary>Mod startup completed.</summary>
        public const string Startup = "MWP/Info/Startup";

        /// <summary>Configuration loaded and validated.</summary>
        public const string ConfigLoaded = "MWP/Info/Config";

        /// <summary>Configuration value clamped or rejected.</summary>
        public const string ConfigInvalid = "MWP/Warn/Config";

        /// <summary>Save file schema migrated.</summary>
        public const string SaveMigrated = "MWP/Info/SaveMigrate";

        /// <summary>A single pump save was corrupt and was safely reset.</summary>
        public const string SaveCorrupt = "MWP/Warn/SaveCorrupt";

        /// <summary>Save write/read failure.</summary>
        public const string SaveError = "MWP/Error/Save";

        /// <summary>New pump entity initialised.</summary>
        public const string PumpCreated = "MWP/Info/PumpCreated";

        /// <summary>Pump entered maintenance.</summary>
        public const string MaintenanceStarted = "MWP/Info/Maintenance";

        /// <summary>Maintenance could not run (materials or path).</summary>
        public const string MaintenanceBlocked = "MWP/Warn/MaintenanceBlocked";

        /// <summary>Maintenance finished.</summary>
        public const string MaintenanceFinished = "MWP/Info/MaintenanceDone";

        /// <summary>Water body withdrawal failed repeatedly.</summary>
        public const string WaterWithdrawFailed = "MWP/Warn/WaterWithdraw";

        /// <summary>Compatibility scan result.</summary>
        public const string CompatScan = "MWP/Compat/Scan";

        /// <summary>Optional pipe adapter failed and degraded.</summary>
        public const string CompatAdapterError = "MWP/Compat/AdapterError";
    }

    /// <summary>In-memory logger used by unit tests.</summary>
    public sealed class RecordingLog : IMwpLog
    {
        private readonly List<string> _entries = new List<string>();

        /// <summary>Captured entries in "<level>|<key>|<message>" form.</summary>
        public IReadOnlyList<string> Entries => _entries;

        /// <inheritdoc />
        public void Info(string key, string message) => _entries.Add("Info|" + key + "|" + message);

        /// <inheritdoc />
        public void Warn(string key, string message) => _entries.Add("Warn|" + key + "|" + message);

        /// <inheritdoc />
        public void Error(string key, string message) => _entries.Add("Error|" + key + "|" + message);

        /// <inheritdoc />
        public void Compat(string key, string message) => _entries.Add("Compat|" + key + "|" + message);
    }
}
