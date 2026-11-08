namespace MWP.UI.Localization
{
    /// <summary>
    /// Base-game localization keys the pump panel reuses instead of shipping duplicate text.
    ///
    /// Keys that belong to MWP itself live in <see cref="MWP.Core.MwpLocKeys"/> (CONTRACT §8) and
    /// are checked against the three shipped CSVs by the data validator. The keys below are not
    /// ours: they are baked into the installed game (verified against the shipped localization
    /// tables of Timberborn 1.1.2.8), so using them cannot show a raw key to the player
    /// (REQ-UI-05) and needs no extra CSV rows.
    ///
    /// Power, hp values and the internal stock are no longer formatted here: those rows are
    /// rendered by the game's own fragments (MechanicalNodeFragment, SimpleOutputInventoryFragment,
    /// WaterInputPipeDepthFragment). The panel only formats the hint sentences and material names,
    /// and every key is called with one fixed parameter-type list so the localization wrapper does
    /// not report "parameter types have changed".
    /// </summary>
    public static class MwpUiVanillaKeys
    {
        /// <summary>Prefix of a good's display-name key: <c>"Good." + goodId + ".DisplayName"</c>.</summary>
        public const string GoodDisplayNamePrefix = "Good.";

        /// <summary>Suffix of a good's display-name key.</summary>
        public const string GoodDisplayNameSuffix = ".DisplayName";

        /// <summary>
        /// Full sentence telling the player that a building needs power and must be connected to a
        /// generator: the "reason + next step" hint for <c>PumpState.NoPower</c> (REQ-UI-02).
        /// </summary>
        public const string HintNoPower = "Status.Mechanical.NoPower";

        /// <summary>Full sentence telling the player that no road connects the building (NoAccess hint).</summary>
        public const string HintNoAccess = "Status.Buildings.Unconnected";
    }
}
