using Bindito.Core;
using MWP.Core;

namespace MWP.Integration
{
    /// <summary>
    /// Composition root of the optional integration assembly (CONTRACT §11).
    ///
    /// Bindito discovers every non-abstract <see cref="Configurator"/> in the AppDomain and
    /// installs the ones whose <see cref="ContextAttribute"/> matches the active context, so a mod
    /// assembly needs no manual registration (API_FACTS_MWP §7, verified by the HDBF reference
    /// mod). The <see cref="IPipeAdapter"/> binding lives here rather than in MWP.Gameplay because
    /// gameplay may only reference MWP.Core (one <c>ProjectReference</c> per project) and therefore
    /// cannot name <see cref="MwpPipeAdapter"/>; the binding is still a session singleton that
    /// gameplay consumes through the frozen Core interface (REQ-NFR-05).
    /// </summary>
    [Context("Game")]
    public sealed class MwpIntegrationConfigurator : Configurator
    {
        /// <inheritdoc />
        protected override void Configure()
        {
            Bind<MwpPipeAdapter>().AsSingleton();
            Bind<IPipeAdapter>().ToExisting<MwpPipeAdapter>();
        }
    }
}
