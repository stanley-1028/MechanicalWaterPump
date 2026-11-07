namespace MWP.UI.Localization
{
    /// <summary>
    /// Base-game localization keys the pump panel reuses instead of shipping duplicate text.
    ///
    /// Keys that belong to MWP itself live in <see cref="MWP.Core.MwpLocKeys"/> (CONTRACT §8) and
    /// are checked against the three shipped CSVs by the data validator. The keys below are not
    /// ours: they are baked into the installed game (verified by grepping the shipped
    /// localization tables of Timberborn 1.1.2.8 for every constant here), so using them cannot
    /// show a raw key to the player (REQ-UI-05) and needs no extra CSV rows.
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

        /// <summary>Full sentence telling the player that the building needs deeper water (NoWater hint).</summary>
        public const string HintNoWater = "Status.Buildings.NeedsWater";

        /// <summary>Full sentence telling the player that no road connects the building (NoAccess hint).</summary>
        public const string HintNoAccess = "Status.Buildings.Unconnected";

        /// <summary>
        /// "Power input / maximum: {0} / {1} ({2}%)". Used as the tooltip of the power-supply row so
        /// the delivered hp is visible next to the percentage (CONTRACT §10 item 2).
        /// </summary>
        public const string PowerDetail = "Mechanical.PowerInputMaximum";

        /// <summary>
        /// "{0} hp" / "{0} 匹馬力": the game's horsepower unit. The power tooltip formats its hp
        /// values with this key so the unit follows the active language, exactly like the vanilla
        /// mechanical-node formatter does.
        /// </summary>
        public const string PowerUnit = "Unit.HorsePower.NumberAndUnit";
    }
}
