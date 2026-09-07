using System;
using Microsoft.Extensions.DependencyInjection;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Blocks.Reports.Cartridges.RentRoll;
using Harborline.Blocks.Reports.DependencyInjection;
using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

/// <summary>
/// W#72 PR 6 — DI wiring smoke tests for
/// <see cref="RentRollServiceCollectionExtensions.AddRentRollCartridge"/>.
/// Wires upstream cluster stubs directly (matching the PR 3 pattern from
/// <see cref="AddArAgingSummaryCartridgeTests"/>).
/// </summary>
public sealed class AddRentRollCartridgeTests
{
    private static IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IReportQuerySource>(new InMemoryReportQuerySource());

        services
            .AddBlocksReportsSubstrate()
            .AddRentRollCartridge();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddRentRollCartridge_RegistersCartridgeWithRegistry()
    {
        var sp = BuildProvider();
        sp.UseBlocksReports();

        var registry = sp.GetRequiredService<ReportCartridgeRegistry>();
        Assert.Contains(ReportKind.RentRoll, registry.RegisteredKinds);
    }

    [Fact]
    public void AddRentRollCartridge_CartridgeIsResolvable()
    {
        var sp = BuildProvider();
        sp.UseBlocksReports();

        var cartridge = sp.GetRequiredService<
            IReportCartridge<RentRollParameters, RentRollResult>>();
        Assert.NotNull(cartridge);
        Assert.Equal(ReportKind.RentRoll, cartridge.Kind);
    }

    [Fact]
    public void AddRentRollCartridge_IReportRunnerIsResolvable()
    {
        var sp = BuildProvider();
        sp.UseBlocksReports();

        var runner = sp.GetRequiredService<IReportRunner>();
        Assert.NotNull(runner);
    }
    [Fact]
    public async Task AddRentRollCartridge_RunnerDispatchesEndToEnd()
    {
        var chart = ChartOfAccountsId.NewId(); var source = new InMemoryReportQuerySource();
        source.Charts.Add(new ReportChart(chart, "Rent roll DI", "USD", true));
        var services = new ServiceCollection(); services.AddSingleton<IReportQuerySource>(source);
        services.AddBlocksReportsSubstrate().AddRentRollCartridge();
        var sp = services.BuildServiceProvider(); sp.UseBlocksReports();
        var result = await sp.GetRequiredService<IReportRunner>().RunAsync<RentRollParameters, RentRollResult>(
            ReportKind.RentRoll, new() { ChartId = chart, AsOfDate = new DateOnly(2026, 5, 31) }, new TenantId("di-rent"), Harborline.Foundation.Crypto.PrincipalId.FromBytes(new byte[32]));
        Assert.Equal(ReportKind.RentRoll, result.Kind); Assert.NotNull(result.Result);
        // RentRollResult carries no ChartId; pin the echoed as-of date instead.
        Assert.Equal(new DateOnly(2026, 5, 31), result.Result.AsOf); Assert.Equal("inmem:di-rent:1", result.SnapshotMarker);
    }
}
