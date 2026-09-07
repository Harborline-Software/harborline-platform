using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Harborline.Kernel.WorkItems.DependencyInjection;

/// <summary>Host environment for work-item composition decisions.</summary>
public enum WorkItemHostEnvironment
{
    /// <summary>Local development; volatile adapters are permitted.</summary>
    Development,

    /// <summary>Automated test host; volatile adapters are permitted.</summary>
    Test,

    /// <summary>Production; volatile adapters are refused.</summary>
    Production,
}

/// <summary>Composition entry points for the work-item kernel's persistence adapters.</summary>
public static class WorkItemServiceCollectionExtensions
{
    /// <summary>
    /// Registers the volatile in-memory work-item store. The adapter keeps every work item, receipt,
    /// audit row and outbox row in process memory only, so composing it in Production would silently
    /// discard tenant state on restart; this registration therefore fails closed there.
    /// </summary>
    public static IServiceCollection AddHarborlineWorkItemsInMemoryStore(
        this IServiceCollection services,
        WorkItemHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (environment == WorkItemHostEnvironment.Production)
            throw new InvalidOperationException("The in-memory work-item store is not permitted in Production.");
        services.TryAddSingleton<InMemoryWorkItemStore>();
        services.TryAddSingleton<IWorkItemStore>(provider => provider.GetRequiredService<InMemoryWorkItemStore>());
        services.TryAddSingleton<IWorkItemJournalReader>(provider => provider.GetRequiredService<InMemoryWorkItemStore>());
        return services;
    }

    /// <summary>
    /// Registers the durable single-process journal store. This adapter is Production-composable; it
    /// uses an in-memory index internally, which is unrelated to the volatile adapter above.
    /// </summary>
    public static IServiceCollection AddHarborlineWorkItemsFileJournalStore(
        this IServiceCollection services,
        string journalPath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);
        services.TryAddSingleton<FileJournalWorkItemStore>(_ => new FileJournalWorkItemStore(journalPath));
        services.TryAddSingleton<IWorkItemStore>(provider => provider.GetRequiredService<FileJournalWorkItemStore>());
        services.TryAddSingleton<IWorkItemJournalReader>(provider => provider.GetRequiredService<FileJournalWorkItemStore>());
        return services;
    }
}
