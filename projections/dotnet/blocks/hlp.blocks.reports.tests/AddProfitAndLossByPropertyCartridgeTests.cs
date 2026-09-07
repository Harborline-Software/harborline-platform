using Microsoft.Extensions.DependencyInjection;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Blocks.Reports.Cartridges.ProfitAndLossByProperty;
using Harborline.Blocks.Reports.DependencyInjection;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

/// <summary>
/// W#72 PR 5 — DI wiring smoke tests for
/// <see cref="ProfitAndLossByPropertyServiceCollectionExtensions.AddProfitAndLossByPropertyCartridge"/>.
/// Verifies that after calling
/// <c>AddBlocksReportsSubstrate() + AddProfitAndLossByPropertyCartridge() + UseBlocksReports()</c>
/// the registry contains <see cref="ReportKind.ProfitAndLossByProperty"/> and the
/// runner is resolvable.
/// </summary>
public sealed class AddProfitAndLossByPropertyCartridgeTests
{
    private static System.IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IReportQuerySource>(new InMemoryReportQuerySource());

        services
            .AddBlocksReportsSubstrate()
            .AddProfitAndLossByPropertyCartridge();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddProfitAndLossByPropertyCartridge_RegistersCartridgeWithRegistry()
    {
        var sp = BuildProvider();
        sp.UseBlocksReports();

        var registry = sp.GetRequiredService<ReportCartridgeRegistry>();
        Assert.Contains(ReportKind.ProfitAndLossByProperty, registry.RegisteredKinds);
    }

    [Fact]
    public void AddProfitAndLossByPropertyCartridge_CartridgeIsResolvable()
    {
        var sp = BuildProvider();
        sp.UseBlocksReports();

        var cartridge = sp.GetRequiredService<
            IReportCartridge<ProfitAndLossByPropertyParameters, ProfitAndLossByPropertyResult>>();
        Assert.NotNull(cartridge);
        Assert.Equal(ReportKind.ProfitAndLossByProperty, cartridge.Kind);
    }

    [Fact]
    public void AddProfitAndLossByPropertyCartridge_IReportRunnerIsResolvable()
    {
        var sp = BuildProvider();
        sp.UseBlocksReports();

        var runner = sp.GetRequiredService<IReportRunner>();
        Assert.NotNull(runner);
    }
    [Fact]
    public async Task AddProfitAndLossByPropertyCartridge_RunnerDispatchesEndToEnd()
    {
        var chart = ChartOfAccountsId.NewId(); var source = new InMemoryReportQuerySource();
        source.Charts.Add(new ReportChart(chart, "P&L property DI", "USD", true));
        var services = new ServiceCollection(); services.AddSingleton<IReportQuerySource>(source);
        services.AddBlocksReportsSubstrate().AddProfitAndLossByPropertyCartridge();
        var sp = services.BuildServiceProvider(); sp.UseBlocksReports();
        var result = await sp.GetRequiredService<IReportRunner>().RunAsync<ProfitAndLossByPropertyParameters, ProfitAndLossByPropertyResult>(
            ReportKind.ProfitAndLossByProperty, new() { ChartId = chart }, new Harborline.Foundation.Assets.Common.TenantId("di-pnl-prop"), Harborline.Foundation.Crypto.PrincipalId.FromBytes(new byte[32]));
        Assert.Equal(ReportKind.ProfitAndLossByProperty, result.Kind); Assert.NotNull(result.Result);
        Assert.Equal(chart, result.Result.ChartId); Assert.Equal("inmem:di-pnl-prop:1", result.SnapshotMarker);
    }
}
