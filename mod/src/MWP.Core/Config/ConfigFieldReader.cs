using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MWP.Core
{
    /// <summary>
    /// Scalar/collection field readers for config schema 2. Split from
    /// <see cref="MwpConfigValidation"/> to honour the 300-line rule (REQ-NFR-03); all reads are
    /// defensive and report through <see cref="MwpConfigValidation.Warn"/>.
    /// </summary>
    internal static class ConfigFieldReader
    {
        /// <summary>Reads a strictly positive finite number, otherwise the fallback.</summary>
        internal static double ReadPositive(
            IDictionary<string, object?>? map,
            string key,
            double fallback,
            double max,
            string path,
            IMwpLog log)
        {
            if (map == null || !MiniJson.TryGet(map, key, out _))
            {
                return fallback;
            }

            double value = MiniJson.GetNumber(map, key, double.NaN);
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0.0)
            {
                MwpConfigValidation.Warn(log, path + "." + key, "expected a positive finite number");
                return fallback;
            }

            if (value > max)
            {
                MwpConfigValidation.Warn(log, path + "." + key, "clamped " + value + " to " + max);
                return max;
            }

            return value;
        }

        /// <summary>Reads a finite number clamped into min..max, otherwise the fallback.</summary>
        internal static double ReadNumber(
            IDictionary<string, object?>? map,
            string key,
            double fallback,
            double min,
            double max,
            string path,
            IMwpLog log)
        {
            if (map == null || !MiniJson.TryGet(map, key, out _))
            {
                return fallback;
            }

            double value = MiniJson.GetNumber(map, key, double.NaN);
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                MwpConfigValidation.Warn(log, path + "." + key, "expected a finite number");
                return fallback;
            }

            double clamped = BalanceFormulas.Sanitize(value, fallback, min, max);
            if (clamped != value)
            {
                MwpConfigValidation.Warn(log, path + "." + key, "clamped " + value + " to " + clamped);
            }

            return clamped;
        }

        /// <summary>Reads a whole number clamped into min..max, otherwise the fallback.</summary>
        internal static int ReadInt(
            IDictionary<string, object?>? map,
            string key,
            int fallback,
            int min,
            int max,
            string path,
            IMwpLog log)
        {
            double value = ReadNumber(map, key, fallback, min, max, path, log);
            if (value < min)
            {
                return min;
            }

            return value > max ? max : (int)value;
        }

        /// <summary>Reads a boolean, otherwise the fallback.</summary>
        internal static bool ReadBool(
            IDictionary<string, object?>? map,
            string key,
            bool fallback,
            string path,
            IMwpLog log)
        {
            if (map == null || !MiniJson.TryGet(map, key, out object? value))
            {
                return fallback;
            }

            if (value is bool flag)
            {
                return flag;
            }

            MwpConfigValidation.Warn(log, path + "." + key, "expected a boolean");
            return fallback;
        }

        /// <summary>Reads the per-tier maintenance interval array, filling gaps with tier defaults.</summary>
        internal static IReadOnlyList<double> ReadIntervals(IList<object?>? raw, IMwpLog log)
        {
            var intervals = new double[TierCatalog.Default.Count];
            for (int i = 0; i < intervals.Length; i++)
            {
                intervals[i] = TierCatalog.Default[i].MaintenanceIntervalDays;
            }

            if (raw != null)
            {
                for (int i = 0; i < raw.Count && i < intervals.Length; i++)
                {
                    string path = "maintenance.intervalsDays[" + i + "]";
                    if (raw[i] is double value && !double.IsNaN(value) && !double.IsInfinity(value))
                    {
                        double clamped = BalanceFormulas.Sanitize(value, intervals[i], MwpConfigValidation.MinIntervalDays, MwpConfigValidation.MaxIntervalDays);
                        if (clamped != value)
                        {
                            MwpConfigValidation.Warn(log, path, "clamped " + value + " to " + clamped);
                        }

                        intervals[i] = clamped;
                    }
                    else
                    {
                        MwpConfigValidation.Warn(log, path, "expected a finite number");
                    }
                }
            }

            return new ReadOnlyCollection<double>(intervals);
        }

        /// <summary>Reads the per-tier maintenance material sets, filling gaps with tier defaults.</summary>
        internal static IReadOnlyList<IReadOnlyList<GoodStack>> ReadMaterialSets(IList<object?>? raw, IMwpLog log)
        {
            var sets = new IReadOnlyList<GoodStack>[TierCatalog.Default.Count];
            for (int i = 0; i < sets.Length; i++)
            {
                sets[i] = TierCatalog.Default[i].MaintenanceMaterials;
            }

            if (raw == null)
            {
                return new ReadOnlyCollection<IReadOnlyList<GoodStack>>(sets);
            }

            for (int i = 0; i < raw.Count && i < sets.Length; i++)
            {
                string path = "maintenance.materials[" + i + "]";
                if (raw[i] is IList<object?> stacks)
                {
                    sets[i] = ReadMaterials(stacks, sets[i], path, log);
                }
                else
                {
                    MwpConfigValidation.Warn(log, path, "expected an array");
                }
            }

            return new ReadOnlyCollection<IReadOnlyList<GoodStack>>(sets);
        }

        /// <summary>Reads the Timber Pipes minimum version, falling back to the verified default.</summary>
        internal static string ReadTimberPipesVersion(IDictionary<string, object?>? map, IMwpLog log)
        {
            if (map == null)
            {
                return MwpConfig.DefaultTimberPipesMinVersion;
            }

            string value = MiniJson.GetString(map, "timberPipesMinVersion", MwpConfig.DefaultTimberPipesMinVersion);
            if (string.IsNullOrWhiteSpace(value))
            {
                MwpConfigValidation.Warn(log, "compat.timberPipesMinVersion", "empty value; using default");
                return MwpConfig.DefaultTimberPipesMinVersion;
            }

            return value.Trim();
        }

        private static IReadOnlyList<GoodStack> ReadMaterials(
            IList<object?> raw,
            IReadOnlyList<GoodStack> fallback,
            string path,
            IMwpLog log)
        {
            var stacks = new List<GoodStack>(raw.Count);
            foreach (object? entry in raw)
            {
                if (entry is not IDictionary<string, object?> map)
                {
                    MwpConfigValidation.Warn(log, path, "expected an object");
                    continue;
                }

                string goodId = MiniJson.GetString(map, "id", string.Empty);
                if (string.IsNullOrWhiteSpace(goodId))
                {
                    MwpConfigValidation.Warn(log, path, "missing good id");
                    continue;
                }

                int amount = ReadInt(map, "amount", 0, 0, MwpConfigValidation.MaxMaterialAmount, path + "." + goodId, log);
                stacks.Add(new GoodStack(goodId.Trim(), amount));
            }

            if (stacks.Count == 0 && raw.Count > 0)
            {
                MwpConfigValidation.Warn(log, path, "no valid stacks; using defaults");
                return fallback;
            }

            return new ReadOnlyCollection<GoodStack>(stacks);
        }
    }
}
