using System;
using System.Collections.Generic;

namespace MWP.Core
{
    /// <summary>
    /// Typed field readers for the pump save JSON. A wrong JSON type (for example a string where a
    /// number belongs) marks the pump as corrupt so <see cref="MwpSaveCodec.Sanitize"/> can apply
    /// the safe reset required by REQ-SAVE-03; a missing field is not corruption and keeps the
    /// fallback (requirements 11.5).
    /// </summary>
    internal static class SaveFieldReader
    {
        /// <summary>Reads a finite number; wrong-typed or non-finite values flag the state as corrupt.</summary>
        internal static double ReadNumber(
            IDictionary<string, object?> root,
            string key,
            double fallback,
            ref bool corrupt)
        {
            if (!MiniJson.TryGet(root, key, out object? raw))
            {
                return fallback;
            }

            double value;
            switch (raw)
            {
                case double number: value = number; break;
                case float number: value = number; break;
                case int number: value = number; break;
                case long number: value = number; break;
                case decimal number: value = (double)number; break;
                default:
                    corrupt = true;
                    return fallback;
            }

            if (!IsFinite(value))
            {
                corrupt = true;
                return fallback;
            }

            return value;
        }

        /// <summary>Reads a whole number; out-of-range or wrong-typed values flag the state as corrupt.</summary>
        internal static int ReadInt(
            IDictionary<string, object?> root,
            string key,
            int fallback,
            ref bool corrupt)
        {
            double value = ReadNumber(root, key, fallback, ref corrupt);
            if (value < int.MinValue || value > int.MaxValue)
            {
                corrupt = true;
                return fallback;
            }

            return (int)value;
        }

        /// <summary>Reads a boolean; wrong-typed values optionally flag the state as corrupt.</summary>
        internal static bool ReadBool(
            IDictionary<string, object?> root,
            string key,
            bool fallback,
            ref bool corrupt,
            bool markCorrupt = true)
        {
            if (!MiniJson.TryGet(root, key, out object? raw))
            {
                return fallback;
            }

            if (raw is bool flag)
            {
                return flag;
            }

            if (markCorrupt)
            {
                corrupt = true;
            }

            return fallback;
        }

        /// <summary>Reads the maintenance phase name or number; invalid values flag corruption.</summary>
        internal static MaintenancePhase ReadPhase(IDictionary<string, object?> root, ref bool corrupt)
        {
            if (!MiniJson.TryGet(root, "maintenanceState", out object? raw))
            {
                return MaintenancePhase.None;
            }

            if (raw is string text
                && Enum.TryParse(text, true, out MaintenancePhase parsed)
                && Enum.IsDefined(typeof(MaintenancePhase), parsed))
            {
                return parsed;
            }

            if (raw is double number && Enum.IsDefined(typeof(MaintenancePhase), (int)number))
            {
                return (MaintenancePhase)(int)number;
            }

            corrupt = true;
            return MaintenancePhase.None;
        }

        /// <summary>Reads the last reported state; an invalid value falls back to Producing (recomputed each tick).</summary>
        internal static PumpState ReadState(IDictionary<string, object?> root)
        {
            if (!MiniJson.TryGet(root, "state", out object? raw))
            {
                return PumpState.Producing;
            }

            if (raw is string text
                && Enum.TryParse(text, true, out PumpState parsed)
                && Enum.IsDefined(typeof(PumpState), parsed))
            {
                return parsed;
            }

            if (raw is double number && Enum.IsDefined(typeof(PumpState), (int)number))
            {
                return (PumpState)(int)number;
            }

            return PumpState.Producing;
        }

        /// <summary>Clamps an integer into min..max.</summary>
        internal static int ClampInt(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        /// <summary>True when the value is neither NaN nor an infinity.</summary>
        internal static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
