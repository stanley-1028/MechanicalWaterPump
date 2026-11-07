using MWP.Core;

namespace MWP.Tests
{
    // REQ-NFR-03 note: the 40-line cap is a soft guideline for logic. The group methods in
    // this file are declarative lists of independent t.Run cases; splitting them further
    // would only scatter the cases, so the documented reason for exceeding it is recorded here.
    /// <summary>
    /// REQ-BAL-11 / T22: the 30-day average effective output, including maintenance downtime and
    /// the condition efficiency bands, must stay inside 90..105% of the rated value. The
    /// simulation drives the real state machine and maintenance ledger one game hour at a time.
    /// </summary>
    internal static class AverageYieldTests
    {
        /// <summary>30 game days, in game hours.</summary>
        private const int Hours30Days = 30 * 24;

        internal static void Run(TestRunner t)
        {
            TierAverageTests(t);
            DowntimeTests(t);
            MaintainingStateTests(t);
            LegacyYieldTests(t);

        }

        /// <summary>
        /// Runs the production loop for 30 game days and returns total output divided by the rated
        /// 30-day output. Materials are assumed available; only downtime and wear are modelled.
        /// </summary>
        private static double Simulate(TierDefinition tier, out double downtimeHours, out int maintainingHours)
        {
            var ledger = new MaintenanceLedger();
            MaintenanceRules rules = BuildRules(tier);
            var context = new MaintenanceContext(true, true, false);
            double total = 0.0;
            downtimeHours = 0.0;
            maintainingHours = 0;

            for (int hour = 0; hour < Hours30Days; hour++)
            {
                total += RunHour(tier, ledger, out bool maintaining);
                if (maintaining)
                {
                    downtimeHours += 1.0;
                    maintainingHours++;
                }

                ApplyDecision(ledger, rules, context);
            }

            return total / (tier.WaterPerHour * Hours30Days);
        }

        /// <summary>Builds the production/maintenance rules of one tier, materials assumed available.</summary>
        private static MaintenanceRules BuildRules(TierDefinition tier)
        {
            return new MaintenanceRules(true, true, 50.0, 2.0, tier.MaintenanceIntervalDays, tier.MaintenanceMaterials);
        }

        /// <summary>Advances one game hour: produces and wears, or counts downtime; returns the output.</summary>
        private static double RunHour(TierDefinition tier, MaintenanceLedger ledger, out bool maintaining)
        {
            var input = new PumpStatusInput(
                true,
                false,
                true,
                tier.PowerRequiredHp,
                tier.PowerRequiredHp,
                true,
                tier.InternalCapacity,
                true,
                ledger.Phase,
                ledger.Condition);
            PumpStatus status = PumpStateMachine.Evaluate(input, tier, 0.0, 1.0, 0.0);
            maintaining = status.State == PumpState.Maintaining;
            if (maintaining)
            {
                ledger.AdvanceMaintenance(1.0);
                return 0.0;
            }

            ledger.AccumulateWear(1.0, tier.MaintenanceIntervalDays);
            return status.EffectiveWaterPerHour;
        }

        /// <summary>Applies the ledger decision so the next hour starts from the new phase.</summary>
        private static void ApplyDecision(
            MaintenanceLedger ledger,
            MaintenanceRules rules,
            in MaintenanceContext context)
        {
            MaintenanceDecision decision = ledger.Decide(context, rules);
            if (decision.Action == MaintenanceAction.Start)
            {
                ledger.BeginMaintenance(rules);
            }
            else if (decision.Action == MaintenanceAction.Completed)
            {
                ledger.CompleteMaintenance();
            }
        }

        /// <summary>Test group: TierAverageTests.</summary>
        private static void TierAverageTests(TestRunner t)
        {
            t.Run("30-day average yield stays within 90-105% of rated for all tiers", () =>
            {
                for (int i = 0; i < TierCatalog.Default.Count; i++)
                {
                    TierDefinition tier = TierCatalog.Default[i];
                    double ratio = Simulate(tier, out double downtimeHours, out int maintainingHours);
                    string name = tier.TierName;

                    Assert.True(ratio >= 0.90, name + " average " + ratio + " is below 90%");
                    Assert.True(ratio <= 1.05, name + " average " + ratio + " is above 105%");
                    Assert.True(ratio < 1.0, name + " maintenance must cost some output, got " + ratio);
                    Assert.True(maintainingHours > 0, name + " must spend some time maintaining");
                    Assert.True(downtimeHours < 0.10 * Hours30Days, name + " downtime too large: " + downtimeHours);
                }
            });
        }

        /// <summary>Test group: DowntimeTests.</summary>
        private static void DowntimeTests(TestRunner t)
        {
            t.Run("Every tier's maintenance downtime is 2h per cycle", () =>
            {
                for (int i = 0; i < TierCatalog.Default.Count; i++)
                {
                    TierDefinition tier = TierCatalog.Default[i];
                    Simulate(tier, out double downtimeHours, out int maintainingHours);
                    Assert.True(maintainingHours % 2 == 0, tier.TierName + " downtime is not 2h cycles: " + maintainingHours);
                    Assert.Near(maintainingHours, downtimeHours, 1e-12, tier.TierName + " downtime counter mismatch");
                }
            });
        }

        /// <summary>Test group: MaintainingStateTests.</summary>
        private static void MaintainingStateTests(TestRunner t)
        {
            t.Run("Maintaining state reports zero effective output", () =>
            {
                TierDefinition tier = TierCatalog.Get(PumpTier.Basic);
                var ledger = new MaintenanceLedger();
                var rules = new MaintenanceRules(true, true, 50.0, 2.0, tier.MaintenanceIntervalDays, tier.MaintenanceMaterials);
                ledger.BeginMaintenance(rules);
                var input = new PumpStatusInput(
                    true, false, true, tier.PowerRequiredHp, tier.PowerRequiredHp,
                    true, tier.InternalCapacity, true, ledger.Phase, ledger.Condition);
                PumpStatus status = PumpStateMachine.Evaluate(input, tier, 0.0, 1.0, 0.0);
                Assert.Equal(PumpState.Maintaining, status.State, "maintaining state");
                Assert.Near(0.0, status.EffectiveWaterPerHour, 1e-12, "no water while maintaining");
                Assert.True(ledger.RemainingHours > 0.0, "downtime timer is running");
            });
        }

        /// <summary>Test group: LegacyYieldTests.</summary>
        private static void LegacyYieldTests(TestRunner t)
        {
            t.Run("Legacy rates keep the same maintenance-adjusted ratio", () =>
            {
                BalanceSettings legacy = BalanceSettings.WithLegacyRates();
                double ratio = Simulate(legacy.Tiers[0], out double downtime, out _);
                Assert.True(ratio >= 0.90, "legacy average below 90%: " + ratio);
                Assert.True(ratio <= 1.05, "legacy average above 105%: " + ratio);
                Assert.True(downtime > 0.0, "legacy downtime missing");
            });
        }
    }
}
