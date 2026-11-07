using Bindito.Core;
using MWP.UI.Panels;
using Timberborn.EntityPanelSystem;
using Timberborn.SimpleOutputBuildings;
using Timberborn.SimpleOutputBuildingsUI;
using Timberborn.TemplateInstantiation;

namespace MWP.UI
{
    /// <summary>
    /// Composition root of the UI assembly (CONTRACT §10).
    ///
    /// Bindito discovers every non-abstract <see cref="Configurator"/> in the loaded assemblies
    /// and installs the ones whose <see cref="ContextAttribute"/> matches the active context, so
    /// this configurator is picked up from the mod assembly without a manual registration call.
    /// Two contributions are installed here:
    /// * the pump panel fragment, through the same <see cref="EntityPanelModule"/> multi-binding
    ///   the vanilla UI configurators use;
    /// * a <see cref="TemplateModule"/> decorator that adds
    ///   <see cref="SimpleOutputInventoryFragmentEnabler"/> to any building carrying a
    ///   <see cref="SimpleOutputInventory"/>. Without it the game's own
    ///   <c>SimpleOutputInventoryFragment</c> stays hidden, and the pump would have no native
    ///   Water-stock row (the vanilla configurators use the same decorator for their buildings).
    /// </summary>
    [Context("Game")]
    public sealed class MwpUiConfigurator : Configurator
    {
        /// <inheritdoc />
        protected override void Configure()
        {
            Bind<MwpPumpPanelFragment>().AsSingleton();
            MultiBind<EntityPanelModule>().ToProvider<MwpEntityPanelModuleProvider>().AsSingleton();
            MultiBind<TemplateModule>().ToProvider(CreateTemplateModule).AsSingleton();
        }

        /// <summary>
        /// Adds the vanilla inventory-fragment enabler to the pump template so the native
        /// internal-stock row appears in the entity panel.
        /// </summary>
        /// <returns>The template module fragment contributed by the UI assembly.</returns>
        private static TemplateModule CreateTemplateModule()
        {
            var builder = new TemplateModule.Builder();
            builder.AddDecorator<SimpleOutputInventory, SimpleOutputInventoryFragmentEnabler>();
            return builder.Build();
        }
    }
}
