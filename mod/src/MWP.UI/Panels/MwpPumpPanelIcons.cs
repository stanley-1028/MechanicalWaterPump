using System;
using System.Collections.Generic;
using MWP.Core;
using UnityEngine;

namespace MWP.UI.Panels
{
    /// <summary>
    /// The vanilla sprite set the panel rows use.
    ///
    /// Every path is a shipped game resource (<c>Resources.Load&lt;Sprite&gt;("UI/Images/...")</c>),
    /// the same lookup the game's own UI code performs: no sprite, atlas or texture is shipped by
    /// the mod. A path that cannot be loaded resolves to <c>null</c> and the row then renders
    /// without its icon instead of throwing (REQ-SAFE-01). The cache is per panel process and is
    /// only touched from the main thread (UI rendering).
    /// </summary>
    internal static class MwpPumpPanelIcons
    {
        /// <summary>Water drop: internal stock and the actual output row.</summary>
        public const string Water = "UI/Images/Game/unfiltered-fluid";

        /// <summary>Contamination drop: water quality.</summary>
        public const string WaterQuality = "UI/Images/Game/ico-contamination";

        /// <summary>Valve wheel (open): the intake-depth fallback row.</summary>
        public const string Depth = "UI/Images/Game/ico-water-source-regulator-open";

        /// <summary>Valve wheel (closed): pipe/adapter state row.</summary>
        public const string Pipe = "UI/Images/Game/ico-water-source-regulator-closed";

        /// <summary>Mechanical power: NoPower and LowPower.</summary>
        public const string Power = "UI/Images/Game/ico-power";

        /// <summary>Closed gate: no hauling path (NoAccess, NoAccessForMaintenance).</summary>
        public const string NoAccess = "UI/Images/Game/ico-gate-closed";

        /// <summary>Goods: full storage and missing maintenance materials.</summary>
        public const string Goods = "UI/Images/Game/storage-goods";

        /// <summary>Durability: condition/maintenance states.</summary>
        public const string Durability = "UI/Images/Game/ico-durability";

        /// <summary>Clock: next maintenance countdown and the maintaining state.</summary>
        public const string Clock = "UI/Images/Game/ico-clock";

        /// <summary>Paused glyph of the vanilla pause button (bare, without the round frame).</summary>
        public const string Paused = "UI/Images/Game/pause-button-paused-bare";

        /// <summary>Positive productivity arrow: the Producing state.</summary>
        public const string Producing = "UI/Images/Game/productivity-high";

        /// <summary>Yellow warning triangle: generic warning/unknown.</summary>
        public const string Warning = "UI/Images/Core/warning_triangle";

        private static readonly Dictionary<string, Sprite?> Cache = new(StringComparer.Ordinal);

        /// <summary>Returns the vanilla sprite for the path, or null when it is missing.</summary>
        /// <param name="path">Unity resource path such as <c>UI/Images/Game/ico-power</c>.</param>
        /// <returns>The loaded sprite; null for an empty/missing path.</returns>
        public static Sprite? Get(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            if (Cache.TryGetValue(path!, out Sprite? cached))
            {
                return cached;
            }

            Sprite? sprite = null;
            try
            {
                sprite = Resources.Load<Sprite>(path!);
            }
            catch (Exception)
            {
                // A missing resource is not a panel failure: the row renders without its icon.
            }

            Cache[path!] = sprite;
            return sprite;
        }

        /// <summary>Picks the vanilla icon that matches the highest-priority pump state.</summary>
        /// <param name="state">State already selected by the Core state machine.</param>
        /// <returns>Unity resource path of the status icon.</returns>
        public static string StatusIcon(PumpState state)
        {
            switch (state)
            {
                case PumpState.NoPower:
                case PumpState.LowPower:
                    return Power;
                case PumpState.NoWater:
                    return WaterQuality;
                case PumpState.NoAccess:
                case PumpState.NoAccessForMaintenance:
                    return NoAccess;
                case PumpState.StorageFull:
                case PumpState.MissingMaintenanceMaterials:
                    return Goods;
                case PumpState.NeedsMaintenance:
                case PumpState.MaintenanceDue:
                case PumpState.Critical:
                    return Durability;
                case PumpState.Maintaining:
                    return Clock;
                case PumpState.Paused:
                    return Paused;
                case PumpState.Producing:
                    return Producing;
                default:
                    return Warning;
            }
        }
    }
}
