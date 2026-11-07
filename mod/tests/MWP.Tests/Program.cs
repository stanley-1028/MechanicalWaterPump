using System;

namespace MWP.Tests
{
    /// <summary>
    /// Entry point of the L1 test runner. Runs every suite, prints the last line as
    /// "PASS n/n" on success and returns a non-zero exit code on any failure.
    /// </summary>
    internal static class Program
    {
        private static int Main()
        {
            var runner = new TestRunner();
            BalanceFormulasTests.Run(runner);
            StateMachineTests.Run(runner);
            MaintenanceTests.Run(runner);
            AverageYieldTests.Run(runner);
            MiniJsonTests.Run(runner);
            ConfigTests.Run(runner);
            ConfigClampTests.Run(runner);
            SaveTests.Run(runner);
            SaveRoundtripTests.Run(runner);
            ThrottledLogTests.Run(runner);
            return runner.Report();
        }
    }
}
