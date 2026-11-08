using Bindito.Core;
using MWP.Core;
using MWP.Gameplay.Components;
using Timberborn.Emptying;
using Timberborn.Hauling;
using Timberborn.SimpleOutputBuildings;
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

            // Hauling bridge (2026-11-07 fix): a building with a SimpleOutputInventory only gets
            // haulers if it carries the same decorator set vanilla uses for output buildings
            // (GathererFlag / FarmHouse / BuilderHub): HaulCandidate + EmptyOutputWorkplaceBehavior
            // + SimpleOutputInventoryHaulBehaviorProvider. Without it beavers never carry the pump's
            // water to tanks. Verified with ApiIndex IL:
            //  - HaulCandidate.Awake collects GetComponents<IHaulBehaviorProvider>(), so the provider
            //    must be decorated before HaulCandidate;
            //  - provider.GetWeightedBehaviors needs EmptyOutputWorkplaceBehavior, which is resolved
            //    with GetComponent in its Awake;
            //  - HaulingConfigurator adds HaulPrioritizable to every HaulCandidate, and
            //    BuildingsConfigurator adds BlockableObject to every BuildingSpec (no extra spec needed).
            builder.AddDecorator<SimpleOutputInventory, EmptyOutputWorkplaceBehavior>();
            builder.AddDecorator<SimpleOutputInventory, SimpleOutputInventoryHaulBehaviorProvider>();
            builder.AddDecorator<SimpleOutputInventory, HaulCandidate>();

            // Intentional: the pump does NOT carry the vanilla NoHaulingPostStatus ("Requires
            // haulers to function"), removed in 0.2.2 at the user's request. The pump is
            // worker-free: without haulers or a pipe it simply fills its internal tank and stops
            // like any vanilla output building, and the warning wrongly suggested it cannot work.
            // The hauler chain above stays, so ordinary haulers still empty the pump when the
            // district has a staffed Hauling Post.
            return builder.Build();
        }
    }
}
