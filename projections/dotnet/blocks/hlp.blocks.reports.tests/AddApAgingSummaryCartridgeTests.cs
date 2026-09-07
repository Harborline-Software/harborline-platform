using Microsoft.Extensions.DependencyInjection;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Blocks.Reports.Cartridges.ApAgingSummary;
using Harborline.Blocks.Reports.DependencyInjection;
using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

/// <summary>
/// DI wiring smoke tests for
/// <see cref="ApAgingSummaryServiceCollectionExtensions.AddApAgingSummaryCartridge"/>.
/// Verifies that after calling
/// <c>AddBlocksReportsSubstrate() + AddApAgingSummaryCartridge() + UseBlocksReports()</c>
/// the registry contains <see cref="ReportKind.ApAgingSummary"/> and the
/// runner is resolvable.
/// Mirrors <see cref="AddArAgingSummaryCartridgeTests"/> with AP semantics.
/// </summary>
public sealed class AddApAgingSummaryCartridgeTests
{
    private static System.IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IReportQuerySource>(new InMemoryReportQuerySource());

        services
            .AddBlocksReportsSubstrate()
            .AddApAgingSummaryCartridge();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddApAgingSummaryCartridge_RegistersCartridgeWithRegistry()
    {
        var sp = BuildProvider();
        sp.UseBlocksReports();

        var registry = sp.GetRequiredService<ReportCartridgeRegistry>();
        Assert.Contains(ReportKind.ApAgingSummary, registry.RegisteredKinds);
    }

    [Fact]
    public void AddApAgingSummaryCartridge_CartridgeIsResolvable()
    {
        var sp = BuildProvider();
        sp.UseBlocksReports();

        var cartridge = sp.GetRequiredService<
            IReportCartridge<ApAgingSummaryParameters, ApAgingSummaryResult>>();
        Assert.NotNull(cartridge);
        Assert.Equal(ReportKind.ApAgingSummary, cartridge.Kind);
    }

    [Fact]
    public void AddApAgingSummaryCartridge_IReportRunnerIsResolvable()
    {
        var sp = BuildProvider();
        sp.UseBlocksReports();

        var runner = sp.GetRequiredService<IReportRunner>();
        Assert.NotNull(runner);
    }
    [Fact]
    public async Task AddApAgingSummaryCartridge_RunnerDispatchesEndToEnd()
    {
        var chart = ChartOfAccountsId.NewId(); var source = new InMemoryReportQuerySource();
        source.Charts.Add(new ReportChart(chart, "AP DI", "USD", true));
        var services = new ServiceCollection(); services.AddSingleton<IReportQuerySource>(source);
        services.AddBlocksReportsSubstrate().AddApAgingSummaryCartridge();
        var sp = services.BuildServiceProvider(); sp.UseBlocksReports();
        var result = await sp.GetRequiredService<IReportRunner>().RunAsync<ApAgingSummaryParameters, ApAgingSummaryResult>(
            ReportKind.ApAgingSummary, new() { ChartId = chart }, new TenantId("di-ap"), Harborline.Foundation.Crypto.PrincipalId.FromBytes(new byte[32]));
        Assert.Equal(ReportKind.ApAgingSummary, result.Kind); Assert.NotNull(result.Result);
        Assert.Equal(chart, result.Result.ChartId); Assert.Equal("inmem:di-ap:1", result.SnapshotMarker);
    }
}
