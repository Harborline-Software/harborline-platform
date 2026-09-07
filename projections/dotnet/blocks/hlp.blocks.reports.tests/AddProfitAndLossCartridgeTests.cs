using Microsoft.Extensions.DependencyInjection;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Blocks.Reports.Cartridges.ProfitAndLoss;
using Harborline.Blocks.Reports.DependencyInjection;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

/// <summary>
/// R5 — DI wiring smoke tests for
/// <see cref="ProfitAndLossServiceCollectionExtensions.AddProfitAndLossCartridge"/>.
/// </summary>
public sealed class AddProfitAndLossCartridgeTests
{
    private static System.IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IReportQuerySource>(new InMemoryReportQuerySource());

        services
            .AddBlocksReportsSubstrate()
            .AddProfitAndLossCartridge();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddProfitAndLossCartridge_RegistersCartridgeWithRegistry()
    {
        var sp = BuildProvider();
        sp.UseBlocksReports();

        var registry = sp.GetRequiredService<ReportCartridgeRegistry>();
        Assert.Contains(ReportKind.ProfitAndLoss, registry.RegisteredKinds);
    }

    [Fact]
    public void AddProfitAndLossCartridge_RunnerResolvesProfitAndLossCartridge()
    {
        var sp = BuildProvider();
        sp.UseBlocksReports();

        var cartridge = sp.GetRequiredService<IReportCartridge<ProfitAndLossParameters, ProfitAndLossResult>>();
        Assert.NotNull(cartridge);
        Assert.Equal(ReportKind.ProfitAndLoss, cartridge.Kind);
    }

    [Fact]
    public void AddProfitAndLossCartridge_IReportRunnerIsResolvable()
    {
        var sp = BuildProvider();
        sp.UseBlocksReports();

        var runner = sp.GetRequiredService<IReportRunner>();
        Assert.NotNull(runner);
    }
    [Fact]
    public async Task AddProfitAndLossCartridge_RunnerDispatchesEndToEnd()
    {
        var chart = ChartOfAccountsId.NewId(); var source = new InMemoryReportQuerySource();
        source.Charts.Add(new ReportChart(chart, "P&L DI", "USD", true));
        var services = new ServiceCollection(); services.AddSingleton<IReportQuerySource>(source);
        services.AddBlocksReportsSubstrate().AddProfitAndLossCartridge();
        var sp = services.BuildServiceProvider(); sp.UseBlocksReports();
        var result = await sp.GetRequiredService<IReportRunner>().RunAsync<ProfitAndLossParameters, ProfitAndLossResult>(
            ReportKind.ProfitAndLoss, new() { ChartId = chart }, new Harborline.Foundation.Assets.Common.TenantId("di-pnl"), Harborline.Foundation.Crypto.PrincipalId.FromBytes(new byte[32]));
        Assert.Equal(ReportKind.ProfitAndLoss, result.Kind); Assert.NotNull(result.Result);
        Assert.Equal(chart, result.Result.ChartId); Assert.Equal("inmem:di-pnl:1", result.SnapshotMarker);
    }
}
