using Harborline.Blocks.Scheduling.Definitions;
using Harborline.Blocks.Scheduling.DependencyInjection;
using Harborline.Foundation.Scheduling.DependencyInjection;
using Harborline.Kernel.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class OccurrenceDependencyInjectionTests
{
    [Fact]
    public void DueQueriesComposeWithTheHostClockAndProducerExactlyOnce()
    {
        var clock = new KernelClock(new FixedClock());
        var services = new ServiceCollection();
        services.AddSingleton(clock);
        services.AddFoundationScheduling();
        services.AddHarborlineSchedulingDueQueries();
        services.AddHarborlineSchedulingDueQueries();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var query = Assert.Single(provider.GetServices<IDueQueueQueryService>());
        Assert.Same(query, provider.GetRequiredService<IDueQueueQueryService>());
        Assert.Same(clock, provider.GetRequiredService<KernelClock>());
        var definition = new ScheduleDefinition(null, "Inspection", new(ScheduleStartKind.Form, "inspection"), DueDate: "2026-07-01T12:00:00Z");
        var occurrence = Assert.Single(query.Query(new(new("series", "asset", definition),
            new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), new(2026, 7, 2, 0, 0, 0, TimeSpan.Zero), [], [])));
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero), occurrence.Due);
        Assert.Equal(TimeSpan.FromHours(8), occurrence.Overdue);
        Assert.True(occurrence.IsDue);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void DueQueryHostMustSupplyItsClockAndProducer(bool supplyClock, bool supplyProducer)
    {
        var services = new ServiceCollection();
        if (supplyClock) services.AddSingleton(new KernelClock(new FixedClock()));
        if (supplyProducer) services.AddFoundationScheduling();
        services.AddHarborlineSchedulingDueQueries();
        Assert.Throws<AggregateException>(() =>
        {
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        });
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 7, 1, 20, 0, 0, TimeSpan.Zero);
    }
}
