using Bindito.Core;
using MWP.UI.Panels;
using Timberborn.EntityPanelSystem;

namespace MWP.UI
{
    /// <summary>
    /// Composition root of the UI assembly (CONTRACT §10).
    ///
    /// Bindito discovers every non-abstract <see cref="Configurator"/> in the loaded assemblies
    /// and installs the ones whose <see cref="ContextAttribute"/> matches the active context, so
    /// this configurator is picked up from the mod assembly without a manual registration call.
    /// The fragment itself is injected into the entity panel through the same
    /// <see cref="EntityPanelModule"/> multi-binding the vanilla UI configurators use.
    /// </summary>
    [Context("Game")]
    public sealed class MwpUiConfigurator : Configurator
    {
        /// <inheritdoc />
        protected override void Configure()
        {
            Bind<MwpPumpPanelFragment>().AsSingleton();
            MultiBind<EntityPanelModule>().ToProvider<MwpEntityPanelModuleProvider>().AsSingleton();
        }
    }
}
