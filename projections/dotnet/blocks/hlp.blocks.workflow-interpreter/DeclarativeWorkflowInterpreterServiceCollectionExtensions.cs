using Microsoft.Extensions.DependencyInjection;

using Harborline.Blocks.Workflow.Durable;

namespace Harborline.Blocks.Workflow.Interpreter;

/// <summary>
/// DI registration for the ADR 0135 A1 declarative interpreter. Registers
/// <see cref="IDeclarativeWorkflowInterpreter"/> so the <see cref="WorkflowTriggerDispatcher"/> resolves it as
/// the fallback for definitions with no typed handler.
/// </summary>
public static class DeclarativeWorkflowInterpreterServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="DeclarativeWorkflowInterpreter"/> as <see cref="IDeclarativeWorkflowInterpreter"/>.
    /// Requires (register FIRST): the durable engine (<c>AddDurableWorkflowEngine</c> — the broker +
    /// <see cref="IWorkflowEffectCatalog"/>), the definition store (<c>AddInMemoryWorkflowDefinitionStore</c>
    /// — the <see cref="IWorkflowDefinitionExecutionStore"/> face), and a host
    /// kernel <see cref="TimeProvider"/> and <see cref="IWorkflowConfirmationContext"/> (the server-derived confirmer + engine proposer).
    /// </summary>
    public static IServiceCollection AddDeclarativeWorkflowInterpreter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IDeclarativeWorkflowInterpreter>(sp => new DeclarativeWorkflowInterpreter(
            sp.GetRequiredService<IWorkflowDefinitionExecutionStore>(),
            sp.GetRequiredService<IWorkflowEffectBroker>(),
            sp.GetRequiredService<IWorkflowEffectCatalog>(),
            sp.GetRequiredService<IWorkflowConfirmationContext>(),
            RequireClock(sp),
            sp.GetService<IWorkflowGateAccess>()));
        return services;
    }

    // This assembly composes only public engine seams; the engine's internal clock resolver is not exported.
    private static TimeProvider RequireClock(IServiceProvider services) =>
        services.GetService<TimeProvider>() ?? throw new InvalidOperationException(
            "The workflow interpreter requires the host's kernel clock: register a TimeProvider before composing it "
            + "(DES-0029 kernel-core-ck-9). It never falls back to wall time.");
}
