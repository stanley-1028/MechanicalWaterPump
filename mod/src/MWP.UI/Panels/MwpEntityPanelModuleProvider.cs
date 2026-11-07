using Bindito.Core;
using Timberborn.EntityPanelSystem;

namespace MWP.UI.Panels
{
    /// <summary>
    /// Contributes the pump fragment to the game's entity panel.
    ///
    /// This mirrors the pattern the vanilla UI assemblies use: each one multi-binds
    /// <see cref="EntityPanelModule"/> with an <see cref="IProvider{T}"/> that builds a module from
    /// its fragments (verified against
    /// <c>Timberborn.FieldsUI.FieldsUIConfigurator+EntityPanelModuleProvider::Get</c> and against
    /// the HDBF reference implementation, which loads in the same game build). No gameplay-side
    /// registration is needed for the panel itself; gameplay only registers the two services the
    /// fragment consumes.
    /// </summary>
    public sealed class MwpEntityPanelModuleProvider : IProvider<EntityPanelModule>
    {
        /// <summary>
        /// Order inside the content area. Vanilla building content uses order 1, so the pump rows
        /// sit next to the other building rows instead of in a separate header/footer slot.
        /// </summary>
        private const int ContentOrder = 1;

        private readonly MwpPumpPanelFragment _fragment;

        /// <summary>Creates the provider.</summary>
        /// <param name="fragment">Singleton pump panel fragment to contribute.</param>
        public MwpEntityPanelModuleProvider(MwpPumpPanelFragment fragment)
        {
            _fragment = fragment;
        }

        /// <inheritdoc />
        public EntityPanelModule Get()
        {
            var builder = new EntityPanelModule.Builder();
            builder.AddMiddleFragment(_fragment, ContentOrder);
            return builder.Build();
        }
    }
}
