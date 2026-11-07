using System;
using System.Collections.Generic;

namespace MWP.Core
{
    /// <summary>
    /// Minimal UTF-8-safe JSON reader API used instead of Newtonsoft (REQ-NFR-07: Core has zero
    /// package dependencies). Parsed values are <see cref="Dictionary{TKey,TValue}"/>,
    /// <see cref="List{T}"/>, <see cref="string"/>, <see cref="double"/>, <see cref="bool"/> and
    /// <c>null</c>; the typed getters clamp wrong/missing values to a caller-supplied fallback so
    /// config and save parsing never throws on bad data (REQ-SAFE-01).
    /// </summary>
    public static class MiniJson
    {
        /// <summary>
        /// Parses one JSON document. Throws <see cref="FormatException"/> on invalid input; callers
        /// that must never throw (config/save codecs) wrap this in try/catch.
        /// </summary>
        /// <param name="text">Complete JSON text.</param>
        /// <returns>The parsed value, null for the JSON literal <c>null</c>.</returns>
        public static object? Parse(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            return MiniJsonReader.Parse(text);
        }

        /// <summary>
        /// Serializes a supported value to compact JSON. Non-finite doubles are rejected because
        /// JSON cannot represent them (REQ-SAFE-01).
        /// </summary>
        /// <param name="value">Dictionary/list/string/number/bool/null tree.</param>
        /// <returns>Compact JSON text.</returns>
        public static string Write(object? value) => MiniJsonWriter.Write(value);

        /// <summary>Reads a number, accepting any JSON numeric type; a missing/wrong value yields the fallback.</summary>
        /// <param name="obj">Parsed object.</param>
        /// <param name="key">Property name, matched case-insensitively as a fallback.</param>
        /// <param name="fallback">Value returned when absent or not numeric.</param>
        public static double GetNumber(IDictionary<string, object?> obj, string key, double fallback)
        {
            if (!TryGet(obj, key, out object? value))
            {
                return fallback;
            }

            switch (value)
            {
                case double d: return double.IsNaN(d) ? fallback : d;
                case float f: return double.IsNaN(f) ? fallback : f;
                case int i: return i;
                case long l: return l;
                case short s: return s;
                case byte b: return b;
                case decimal m: return (double)m;
                default: return fallback;
            }
        }

        /// <summary>Reads a whole number, truncating fractional values; out-of-range gives the fallback.</summary>
        /// <param name="obj">Parsed object.</param>
        /// <param name="key">Property name, matched case-insensitively as a fallback.</param>
        /// <param name="fallback">Value returned when absent, non-numeric or out of range.</param>
        public static int GetInt(IDictionary<string, object?> obj, string key, int fallback)
        {
            double value = GetNumber(obj, key, double.NaN);
            if (double.IsNaN(value) || value < int.MinValue || value > int.MaxValue)
            {
                return fallback;
            }

            return (int)value;
        }

        /// <summary>Reads a boolean; a missing/wrong value yields the fallback.</summary>
        /// <param name="obj">Parsed object.</param>
        /// <param name="key">Property name, matched case-insensitively as a fallback.</param>
        /// <param name="fallback">Value returned when absent or not a boolean.</param>
        public static bool GetBool(IDictionary<string, object?> obj, string key, bool fallback)
        {
            return TryGet(obj, key, out object? value) && value is bool b ? b : fallback;
        }

        /// <summary>Reads a string; a missing/wrong value yields the fallback.</summary>
        /// <param name="obj">Parsed object.</param>
        /// <param name="key">Property name, matched case-insensitively as a fallback.</param>
        /// <param name="fallback">Value returned when absent or not a string.</param>
        public static string GetString(IDictionary<string, object?> obj, string key, string fallback)
        {
            return TryGet(obj, key, out object? value) && value is string s ? s : fallback;
        }

        /// <summary>Case-insensitive lookup so both "id" and "Id" style configs load (chapter 20.6).</summary>
        internal static bool TryGet(
            IDictionary<string, object?>? obj,
            string? key,
            out object? value)
        {
            value = null;
            if (obj == null || string.IsNullOrEmpty(key))
            {
                return false;
            }

            if (obj.TryGetValue(key, out value))
            {
                return true;
            }

            foreach (KeyValuePair<string, object?> pair in obj)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            return false;
        }
    }
}
