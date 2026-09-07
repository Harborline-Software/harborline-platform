using Microsoft.Extensions.DependencyInjection;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Blocks.Reports.Cartridges.ArAgingSummary;
using Harborline.Blocks.Reports.DependencyInjection;
using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

/// <summary>
/// W#72 PR 3 — DI wiring smoke tests for
/// <see cref="ArAgingSummaryServiceCollectionExtensions.AddArAgingSummaryCartridge"/>.
/// Verifies that after calling
/// <c>AddBlocksReportsSubstrate() + AddArAgingSummaryCartridge() + UseBlocksReports()</c>
/// the registry contains <see cref="ReportKind.ArAgingSummary"/> and the
/// runner is resolvable.
/// </summary>
public sealed class AddArAgingSummaryCartridgeTests
{
    private static System.IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IReportQuerySource>(new InMemoryReportQuerySource());

        services
            .AddBlocksReportsSubstrate()
            .AddArAgingSummaryCartridge();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddArAgingSummaryCartridge_RegistersCartridgeWithRegistry()
    {
        var sp = BuildProvider();
        sp.UseBlocksReports();

        var registry = sp.GetRequiredService<ReportCartridgeRegistry>();
        Assert.Contains(ReportKind.ArAgingSummary, registry.RegisteredKinds);
    }

    [Fact]
    public void AddArAgingSummaryCartridge_CartridgeIsResolvable()
    {
        var sp = BuildProvider();
        sp.UseBlocksReports();

        var cartridge = sp.GetRequiredService<
            IReportCartridge<ArAgingSummaryParameters, ArAgingSummaryResult>>();
        Assert.NotNull(cartridge);
        Assert.Equal(ReportKind.ArAgingSummary, cartridge.Kind);
    }

    [Fact]
    public void AddArAgingSummaryCartridge_IReportRunnerIsResolvable()
    {
        var sp = BuildProvider();
        sp.UseBlocksReports();

        var runner = sp.GetRequiredService<IReportRunner>();
        Assert.NotNull(runner);
    }
    [Fact]
    public async Task AddArAgingSummaryCartridge_RunnerDispatchesEndToEnd()
    {
        var chart = ChartOfAccountsId.NewId();
        var source = new InMemoryReportQuerySource();
        source.Charts.Add(new ReportChart(chart, "AR DI", "USD", true));
        var services = new ServiceCollection();
        services.AddSingleton<IReportQuerySource>(source);
        services.AddBlocksReportsSubstrate().AddArAgingSummaryCartridge();
        var sp = services.BuildServiceProvider(); sp.UseBlocksReports();
        var result = await sp.GetRequiredService<IReportRunner>().RunAsync<ArAgingSummaryParameters, ArAgingSummaryResult>(
            ReportKind.ArAgingSummary, new() { ChartId = chart }, new TenantId("di-ar"), Harborline.Foundation.Crypto.PrincipalId.FromBytes(new byte[32]));
        Assert.Equal(ReportKind.ArAgingSummary, result.Kind); Assert.NotNull(result.Result);
        Assert.Equal(chart, result.Result.ChartId); Assert.Equal("inmem:di-ar:1", result.SnapshotMarker);
    }
}
