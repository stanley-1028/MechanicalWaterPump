using MWP.Core;

namespace MWP.Tests
{
    // REQ-NFR-03 note: the 40-line cap is a soft guideline for logic. The group methods in
    // this file are declarative lists of independent t.Run cases; splitting them further
    // would only scatter the cases, so the documented reason for exceeding it is recorded here.
    /// <summary>Unit tests for <see cref="MaintenanceLedger"/>: wear, automatic/manual flow, blocking and reset.</summary>
    internal static class MaintenanceTests
    {
        internal static void Run(TestRunner t)
        {
            WearTests(t);
            DecideTests(t);
            TimerTests(t);

        }

        private static MaintenanceRules Rules(
            bool enabled = true,
            bool auto = true,
            double threshold = 50.0,
            double duration = 2.0,
            double interval = 8.0)
        {
            return new MaintenanceRules(enabled, auto, threshold, duration, interval, GoodStacks.Freeze(new GoodStack(GoodStacks.Log, 10)));
        }
        /// <summary>Test group: WearTests.</summary>
        private static void WearTests(TestRunner t)
        {
            t.Run("New ledger starts clean", () =>
            {
                var ledger = new MaintenanceLedger();
                Assert.Near(100.0, ledger.Condition, 1e-12, "condition");
                Assert.True(ledger.AutoMaintenance, "auto default true");
                Assert.Equal(MaintenancePhase.None, ledger.Phase, "phase none");
                Assert.Near(0.0, ledger.RemainingHours, 1e-12, "remaining 0");
            });

            t.Run("Wear rate is 100/(days*24) per producing hour", () =>
            {
                var ledger = new MaintenanceLedger();
                ledger.AccumulateWear(1.0, 8.0);
                Assert.Near(100.0 - (100.0 / 192.0), ledger.Condition, 1e-9, "one hour wear");
            });

            t.Run("Wear ignores zero, negative and NaN hours", () =>
            {
                var ledger = new MaintenanceLedger();
                ledger.AccumulateWear(0.0, 8.0);
                ledger.AccumulateWear(-5.0, 8.0);
                ledger.AccumulateWear(double.NaN, 8.0);
                Assert.Near(100.0, ledger.Condition, 1e-12, "condition unchanged");
            });

            t.Run("Wear never drops below zero", () =>
            {
                var ledger = new MaintenanceLedger();
                ledger.AccumulateWear(100000.0, 8.0);
                Assert.Near(0.0, ledger.Condition, 1e-12, "clamped at zero");
            });

            t.Run("Wear freezes while maintaining", () =>
            {
                var ledger = new MaintenanceLedger();
                ledger.Reset(50.0, true, MaintenancePhase.Maintaining, 2.0);
                ledger.AccumulateWear(24.0, 8.0);
                Assert.Near(50.0, ledger.Condition, 1e-12, "no wear during downtime");
            });
        }

        /// <summary>Test group: DecideTests.</summary>
        private static void DecideTests(TestRunner t)
        {
            t.Run("Auto maintenance starts at the threshold with materials and access", () =>
            {
                var ledger = new MaintenanceLedger();
                ledger.Reset(49.0, true, MaintenancePhase.None, 0.0);
                MaintenanceDecision decision = ledger.Decide(new MaintenanceContext(true, true, false), Rules());
                Assert.Equal(MaintenanceAction.Start, decision.Action, "start action");
                Assert.Equal(ledger.Phase, decision.Phase, "decision phase matches ledger");
            });

            t.Run("Auto maintenance does not start above the threshold", () =>
            {
                var ledger = new MaintenanceLedger();
                ledger.Reset(60.0, true, MaintenancePhase.None, 0.0);
                MaintenanceDecision decision = ledger.Decide(new MaintenanceContext(true, true, false), Rules());
                Assert.Equal(MaintenanceAction.None, decision.Action, "no start");
                Assert.Equal(MaintenancePhase.None, decision.Phase, "normal phase");
            });

            t.Run("Per-pump auto flag off blocks automatic maintenance", () =>
            {
                var ledger = new MaintenanceLedger();
                ledger.AutoMaintenance = false;
                ledger.Reset(10.0, false, MaintenancePhase.None, 0.0);
                MaintenanceDecision decision = ledger.Decide(new MaintenanceContext(true, true, false), Rules());
                Assert.Equal(MaintenanceAction.None, decision.Action, "no auto start");
                Assert.Equal(MaintenancePhase.Critical, decision.Phase, "critical phase");
            });

            t.Run("Missing materials blocks without consuming anything", () =>
            {
                var ledger = new MaintenanceLedger();
                ledger.Reset(40.0, true, MaintenancePhase.None, 0.0);
                MaintenanceDecision decision = ledger.Decide(new MaintenanceContext(false, true, false), Rules());
                Assert.Equal(MaintenanceAction.BlockedByMaterials, decision.Action, "blocked materials");
                Assert.Equal(MaintenancePhase.MissingMaterials, ledger.Phase, "phase missing materials");
                Assert.Near(40.0, ledger.Condition, 1e-12, "condition untouched");
            });

            t.Run("No path blocks maintenance", () =>
            {
                var ledger = new MaintenanceLedger();
                ledger.Reset(40.0, true, MaintenancePhase.None, 0.0);
                MaintenanceDecision decision = ledger.Decide(new MaintenanceContext(true, false, false), Rules());
                Assert.Equal(MaintenanceAction.BlockedByAccess, decision.Action, "blocked access");
                Assert.Equal(MaintenancePhase.NoAccess, ledger.Phase, "phase no access");
            });

            t.Run("Blocked request stays pending until it can start", () =>
            {
                var ledger = new MaintenanceLedger();
                ledger.Reset(40.0, true, MaintenancePhase.None, 0.0);
                ledger.Decide(new MaintenanceContext(false, true, false), Rules());
                MaintenanceDecision retry = ledger.Decide(new MaintenanceContext(true, true, false), Rules());
                Assert.Equal(MaintenanceAction.Start, retry.Action, "pending retry starts");
            });

            t.Run("Manual request starts early even at high condition", () =>
            {
                var ledger = new MaintenanceLedger();
                ledger.Reset(90.0, true, MaintenancePhase.None, 0.0);
                MaintenanceDecision decision = ledger.Decide(new MaintenanceContext(true, true, true), Rules());
                Assert.Equal(MaintenanceAction.Start, decision.Action, "manual start");
            });
        }

        /// <summary>Test group: TimerTests.</summary>
        private static void TimerTests(TestRunner t)
        {
            t.Run("Downtime runs for the configured hours then completes", () =>
            {
                var ledger = new MaintenanceLedger();
                ledger.Reset(40.0, true, MaintenancePhase.None, 0.0);
                ledger.BeginMaintenance(Rules());
                Assert.Equal(MaintenancePhase.Maintaining, ledger.Phase, "maintaining");
                Assert.Near(2.0, ledger.RemainingHours, 1e-12, "duration 2h");
                ledger.AdvanceMaintenance(0.5);
                Assert.Near(1.5, ledger.RemainingHours, 1e-12, "half hour left");
                ledger.AdvanceMaintenance(2.0);
                Assert.Near(0.0, ledger.RemainingHours, 1e-12, "clamped at zero");
                MaintenanceDecision decision = ledger.Decide(new MaintenanceContext(true, true, false), Rules());
                Assert.Equal(MaintenanceAction.Completed, decision.Action, "completed action");
                ledger.CompleteMaintenance();
                Assert.Near(100.0, ledger.Condition, 1e-12, "condition restored");
                Assert.Equal(MaintenancePhase.None, ledger.Phase, "phase none");
                Assert.Near(0.0, ledger.RemainingHours, 1e-12, "timer reset");
            });

            t.Run("AdvanceMaintenance does nothing outside downtime", () =>
            {
                var ledger = new MaintenanceLedger();
                ledger.AdvanceMaintenance(5.0);
                Assert.Equal(MaintenancePhase.None, ledger.Phase, "phase unchanged");
                Assert.Near(0.0, ledger.RemainingHours, 1e-12, "remaining unchanged");
            });

            t.Run("Disabling maintenance resets condition and phase", () =>
            {
                var ledger = new MaintenanceLedger();
                ledger.Reset(40.0, true, MaintenancePhase.Maintaining, 1.5);
                MaintenanceDecision decision = ledger.Decide(
                    new MaintenanceContext(true, true, false),
                    Rules(enabled: false));
                Assert.Equal(MaintenanceAction.None, decision.Action, "no action");
                Assert.Near(100.0, ledger.Condition, 1e-12, "condition restored");
                Assert.Equal(MaintenancePhase.None, ledger.Phase, "phase cleared");
                Assert.Near(0.0, ledger.RemainingHours, 1e-12, "timer cleared");
            });

            t.Run("Condition zero reports NeedsMaintenance when not wanted", () =>
            {
                var ledger = new MaintenanceLedger();
                ledger.Reset(0.0, false, MaintenancePhase.None, 0.0);
                MaintenanceDecision decision = ledger.Decide(new MaintenanceContext(true, true, false), Rules(auto: false));
                Assert.Equal(MaintenancePhase.NeedsMaintenance, decision.Phase, "needs maintenance phase");
            });

            t.Run("Reset clamps condition and drops an irrelevant timer", () =>
            {
                var ledger = new MaintenanceLedger();
                ledger.Reset(250.0, true, MaintenancePhase.None, 5.0);
                Assert.Near(100.0, ledger.Condition, 1e-12, "clamped condition");
                Assert.Near(0.0, ledger.RemainingHours, 1e-12, "timer dropped");
                ledger.Reset(double.NaN, true, MaintenancePhase.Maintaining, 3.0);
                Assert.Near(100.0, ledger.Condition, 1e-12, "NaN condition fallback");
                Assert.Near(3.0, ledger.RemainingHours, 1e-12, "timer kept while maintaining");
            });
        }
    }
}
