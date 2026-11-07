using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.Hauling;

namespace MWP.Gameplay.Components
{
    /// <summary>
    /// Vanilla-parity hauling feedback for the worker-free pump (BUG-05, 2026-11-08).
    ///
    /// Vanilla output buildings do not silently keep their goods: they carry the game's
    /// <see cref="NoHaulingPostStatus"/> and a small initializer that calls
    /// <c>NoHaulingPostStatus.Initialize(Func&lt;bool&gt;)</c> when the building finishes (see
    /// <c>FireworkLauncherStatus.OnEnterFinishedState</c>,
    /// <c>GoodConsumingBuildingStatusInitializer.OnEnterFinishedState</c>). The pump had the whole
    /// hauler chain (HaulCandidate + SimpleOutputInventoryHaulBehaviorProvider +
    /// EmptyOutputWorkplaceBehavior) but no such status, so when no Hauling Post exists in the
    /// district the stock simply stopped growing with no in-game explanation.
    ///
    /// This component supplies the missing initializer half; the status itself is added to the same
    /// <c>SimpleOutputInventory</c> component by <see cref="MwpGameConfigurator"/>.
    /// </summary>
    public sealed class MwpHaulStatus : BaseComponent, IFinishedStateListener
    {
        /// <inheritdoc />
        public void OnEnterFinishedState()
        {
            NoHaulingPostStatus status = GetComponent<NoHaulingPostStatus>();
            if (status != null)
            {
                // The pump only shows this status while it is finished and has no hauling post in
                // its district; NoHaulingPostStatus itself checks IsInHaulingCenterRange.
                status.Initialize(IsActive);
            }
        }

        /// <inheritdoc />
        public void OnExitFinishedState()
        {
            NoHaulingPostStatus status = GetComponent<NoHaulingPostStatus>();
            if (status != null)
            {
                status.Disable();
            }
        }

        private static bool IsActive()
        {
            return true;
        }
    }
}
