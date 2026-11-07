using Bindito.Core;
using MWP.Core;
using MWP.Gameplay.Components;
using Timberborn.TemplateInstantiation;

namespace MWP.Gameplay
{
    /// <summary>
    /// Game-context composition root for MWP.Gameplay (CONTRACT §5, API_FACTS_MWP §7).
    ///
    /// Registrations:
    /// <list type="bullet">
    /// <item><see cref="MwpRuntime"/>: session config + one shared throttled log;</item>
    /// <item><see cref="MwpPumpRegistry"/>: one instance exposed as both frozen Core interfaces with
    /// <c>ToExisting</c> so UI query and UI commands hit the same dictionary (REQ-UI-07);</item>
    /// <item><see cref="MwpPumpController"/>: transient, one per pump entity, added by the template
    /// decorator below (REQ-ARCH-07);</item>
    /// <item>the template module: every blueprint carrying
    /// <see cref="MechanicalWaterPumpSpec"/> gets exactly one controller.</item>
    /// </list>
    ///
    /// No Harmony patch is registered: the whole mod uses native specs, decorators and DI
    /// (requirements §11.6, REQ-IMPL-07).
    /// </summary>
    [Context("Game")]
    public sealed class MwpGameConfigurator : Configurator
    {
        /// <inheritdoc />
        protected override void Configure()
        {
            Bind<MwpRuntime>().AsSingleton();

            Bind<MwpPumpRegistry>().AsSingleton();
            Bind<IMwpPumpRegistry>().ToExisting<MwpPumpRegistry>();
            Bind<IMwpPumpQuery>().ToExisting<MwpPumpRegistry>();
            Bind<IMwpPumpCommands>().ToExisting<MwpPumpRegistry>();

            Bind<MwpPumpController>().AsTransient();

            MultiBind<TemplateModule>().ToProvider(ProvideTemplateModule).AsSingleton();
        }

        /// <summary>
        /// Binds the custom spec to the shared controller. One decorator for all four tiers keeps
        /// the blueprints data-only and balancing code-free (REQ-NFR-04).
        /// </summary>
        /// <returns>The template module for the game container.</returns>
        private static TemplateModule ProvideTemplateModule()
        {
            var builder = new TemplateModule.Builder();
            builder.AddDecorator<MechanicalWaterPumpSpec, MwpPumpController>();
            return builder.Build();
        }
    }
}
