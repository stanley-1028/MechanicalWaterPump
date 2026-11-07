using System;
using System.Collections.Generic;

namespace MWP.Core
{
    /// <summary>
    /// Reads and writes <c>MWP_Config.json</c> (config schema 2, requirements 20.6). Parsing never
    /// throws and never trusts the file: invalid JSON, wrong types, NaN, infinities, negatives and
    /// out-of-range values degrade to defaults or are clamped, with one
    /// <see cref="MwpLogKeys.ConfigInvalid"/> warning per rejected value (REQ-SAFE-01).
    /// </summary>
    public static class MwpConfigCodec
    {
        /// <summary>Parses and validates a config document; any failure yields <see cref="MwpConfig.Default"/>.</summary>
        /// <param name="json">Raw JSON text; null/empty is treated as "use defaults".</param>
        /// <param name="log">Diagnostics sink; null is tolerated (tests).</param>
        /// <returns>A validated, immutable config.</returns>
        public static MwpConfig Parse(string json, IMwpLog log)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                MwpConfigValidation.Warn(log, "config", "empty document; using defaults");
                return MwpConfig.Default;
            }

            try
            {
                object? parsed = MiniJson.Parse(json);
                if (parsed is IDictionary<string, object?> root)
                {
                    return MwpConfigValidation.Parse(root, log);
                }

                MwpConfigValidation.Warn(log, "config", "root is not an object; using defaults");
                return MwpConfig.Default;
            }
            catch (Exception ex)
            {
                // Defensive: a malformed file must never break mod startup (REQ-SAFE-01).
                MwpConfigValidation.Warn(log, "config", "invalid JSON (" + ex.GetType().Name + "); using defaults");
                return MwpConfig.Default;
            }
        }

        /// <summary>Serializes a config to the canonical schema 2 document (stable key order).</summary>
        /// <param name="config">Validated config to write.</param>
        /// <returns>Compact JSON text.</returns>
        public static string Serialize(MwpConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            var root = new Dictionary<string, object?>
            {
                ["configSchemaVersion"] = config.ConfigSchemaVersion,
                ["features"] = new Dictionary<string, object?>
                {
                    ["pipeAdapter"] = config.PipeAdapterEnabled,
                    ["directPipeOutput"] = config.DirectPipeOutputEnabled,
                    ["uiDiagnostics"] = config.UiDiagnosticsEnabled
                },
                ["balance"] = SerializeBalance(config.Balance),
                ["maintenance"] = SerializeMaintenance(config),
                ["compat"] = new Dictionary<string, object?>
                {
                    ["timberPipesMinVersion"] = config.TimberPipesMinVersion,
                    ["logThrottleSeconds"] = config.LogThrottleSeconds
                }
            };

            return MiniJson.Write(root);
        }

        private static Dictionary<string, object?> SerializeBalance(BalanceSettings balance)
        {
            var tiers = new List<object?>(balance.Tiers.Count);
            for (int i = 0; i < balance.Tiers.Count; i++)
            {
                TierDefinition tier = balance.Tiers[i];
                tiers.Add(new Dictionary<string, object?>
                {
                    ["tier"] = tier.TierName,
                    ["waterPerHour"] = tier.WaterPerHour,
                    ["powerRequired"] = tier.PowerRequiredHp,
                    ["internalCapacity"] = tier.InternalCapacity,
                    ["maxDepth"] = tier.MaxDepth
                });
            }

            return new Dictionary<string, object?>
            {
                ["useLegacyRates"] = balance.UseLegacyRates,
                ["badwaterPenalty"] = balance.BadwaterPenalty,
                ["minWaterQuality"] = balance.MinWaterQuality,
                ["cubicMetersPerWater"] = balance.CubicMetersPerWater,
                ["tickIntervalSeconds"] = balance.TickIntervalSeconds,
                ["tiers"] = tiers
            };
        }

        private static Dictionary<string, object?> SerializeMaintenance(MwpConfig config)
        {
            var intervals = new List<object?>(config.MaintenanceIntervalsDays.Count);
            for (int i = 0; i < config.MaintenanceIntervalsDays.Count; i++)
            {
                intervals.Add(config.MaintenanceIntervalsDays[i]);
            }

            var materialSets = new List<object?>(config.MaintenanceMaterials.Count);
            for (int i = 0; i < config.MaintenanceMaterials.Count; i++)
            {
                IReadOnlyList<GoodStack> stacks = config.MaintenanceMaterials[i];
                var stackList = new List<object?>(stacks.Count);
                for (int j = 0; j < stacks.Count; j++)
                {
                    stackList.Add(new Dictionary<string, object?>
                    {
                        ["id"] = stacks[j].GoodId,
                        ["amount"] = stacks[j].Amount
                    });
                }

                materialSets.Add(stackList);
            }

            return new Dictionary<string, object?>
            {
                ["enabled"] = config.MaintenanceEnabled,
                ["auto"] = config.MaintenanceAuto,
                ["autoThreshold"] = config.MaintenanceAutoThreshold,
                ["durationHours"] = config.MaintenanceDurationHours,
                ["intervalsDays"] = intervals,
                ["materials"] = materialSets
            };
        }
    }
}
