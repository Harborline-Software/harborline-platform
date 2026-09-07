using Microsoft.Extensions.DependencyInjection;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Blocks.Reports.Cartridges.BalanceSheet;
using Harborline.Blocks.Reports.DependencyInjection;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

/// <summary>
/// R5 — DI wiring smoke tests for
/// <see cref="BalanceSheetServiceCollectionExtensions.AddBalanceSheetCartridge"/>.
/// </summary>
public sealed class AddBalanceSheetCartridgeTests
{
    private static System.IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IReportQuerySource>(new InMemoryReportQuerySource());

        services
            .AddBlocksReportsSubstrate()
            .AddBalanceSheetCartridge();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddBalanceSheetCartridge_RegistersCartridgeWithRegistry()
    {
        var sp = BuildProvider();
        sp.UseBlocksReports();

        var registry = sp.GetRequiredService<ReportCartridgeRegistry>();
        Assert.Contains(ReportKind.BalanceSheet, registry.RegisteredKinds);
    }

    [Fact]
    public void AddBalanceSheetCartridge_RunnerResolvesBalanceSheetCartridge()
    {
        var sp = BuildProvider();
        sp.UseBlocksReports();

        var cartridge = sp.GetRequiredService<IReportCartridge<BalanceSheetParameters, BalanceSheetResult>>();
        Assert.NotNull(cartridge);
        Assert.Equal(ReportKind.BalanceSheet, cartridge.Kind);
    }

    [Fact]
    public void AddBalanceSheetCartridge_IReportRunnerIsResolvable()
    {
        var sp = BuildProvider();
        sp.UseBlocksReports();

        var runner = sp.GetRequiredService<IReportRunner>();
        Assert.NotNull(runner);
    }
    [Fact]
    public async Task AddBalanceSheetCartridge_RunnerDispatchesEndToEnd()
    {
        var chart = ChartOfAccountsId.NewId(); var source = new InMemoryReportQuerySource();
        source.Charts.Add(new ReportChart(chart, "BS DI", "USD", true));
        var services = new ServiceCollection(); services.AddSingleton<IReportQuerySource>(source);
        services.AddBlocksReportsSubstrate().AddBalanceSheetCartridge();
        var sp = services.BuildServiceProvider(); sp.UseBlocksReports();
        var result = await sp.GetRequiredService<IReportRunner>().RunAsync<BalanceSheetParameters, BalanceSheetResult>(
            ReportKind.BalanceSheet, new() { ChartId = chart, AsOfDate = new DateOnly(2026, 5, 31) }, new Harborline.Foundation.Assets.Common.TenantId("di-bs"), Harborline.Foundation.Crypto.PrincipalId.FromBytes(new byte[32]));
        Assert.Equal(ReportKind.BalanceSheet, result.Kind); Assert.NotNull(result.Result);
        Assert.Equal(chart, result.Result.ChartId); Assert.Equal("inmem:di-bs:1", result.SnapshotMarker);
    }
}
