using Microsoft.Extensions.DependencyInjection;
using Harborline.Kernel.WorkItems.DependencyInjection;
using Xunit;

namespace Harborline.Kernel.WorkItems.Tests;

public sealed class WorkItemCompositionTests
{
    [Fact]
    public void InMemoryStoreIsRefusedInProduction()
    {
        var services = new ServiceCollection();
        var error = Assert.Throws<InvalidOperationException>(
            () => services.AddHarborlineWorkItemsInMemoryStore(WorkItemHostEnvironment.Production));
        Assert.Contains("not permitted in Production", error.Message, StringComparison.Ordinal);
        Assert.Empty(services);
    }

    [Theory]
    [InlineData(WorkItemHostEnvironment.Development)]
    [InlineData(WorkItemHostEnvironment.Test)]
    public void InMemoryStoreComposesOutsideProduction(WorkItemHostEnvironment environment)
    {
        using var provider = new ServiceCollection()
            .AddHarborlineWorkItemsInMemoryStore(environment)
            .BuildServiceProvider();
        var store = provider.GetRequiredService<IWorkItemStore>();
        Assert.IsType<InMemoryWorkItemStore>(store);
        Assert.Same(store, provider.GetRequiredService<IWorkItemJournalReader>());
    }

    // The durable adapter holds an InMemoryWorkItemStore as its internal index, so a guard placed on
    // the type rather than on this composition seam would disable durable persistence in Production.
    [Fact]
    public async Task FileJournalStoreRemainsComposableInProduction()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hlwi-{Guid.NewGuid():n}", "journal.hlwi");
        try
        {
            await using var provider = new ServiceCollection()
                .AddHarborlineWorkItemsFileJournalStore(path)
                .BuildServiceProvider();
            var store = provider.GetRequiredService<IWorkItemStore>();
            Assert.IsType<FileJournalWorkItemStore>(store);
            Assert.Same(store, provider.GetRequiredService<IWorkItemJournalReader>());
        }
        finally
        {
            var directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
