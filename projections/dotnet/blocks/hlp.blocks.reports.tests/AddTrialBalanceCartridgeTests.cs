using Microsoft.Extensions.DependencyInjection;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Blocks.Reports.Cartridges.TrialBalance;
using Harborline.Blocks.Reports.DependencyInjection;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

/// <summary>
/// W#72 PR 2 — DI wiring smoke tests for
/// <see cref="TrialBalanceServiceCollectionExtensions.AddTrialBalanceCartridge"/>.
/// Verifies that after calling <c>AddBlocksReportsSubstrate()</c> +
/// <c>AddTrialBalanceCartridge()</c> + upstream-cluster registrations +
/// <c>UseBlocksReports()</c>, the registry contains
/// <see cref="ReportKind.TrialBalance"/> and the runner can dispatch it.
/// </summary>
public sealed class AddTrialBalanceCartridgeTests
{
    private static IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IReportQuerySource>(new InMemoryReportQuerySource());

        services
            .AddBlocksReportsSubstrate()
            .AddTrialBalanceCartridge();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddTrialBalanceCartridge_RegistersCartridgeWithRegistry()
    {
        var sp = BuildProvider();
        sp.UseBlocksReports();

        var registry = sp.GetRequiredService<ReportCartridgeRegistry>();
        Assert.Contains(ReportKind.TrialBalance, registry.RegisteredKinds);
    }

    [Fact]
    public void AddTrialBalanceCartridge_RunnerResolvesTrialBalanceCartridge()
    {
        var sp = BuildProvider();
        sp.UseBlocksReports();

        // Smoke: resolving the typed cartridge from DI does not throw.
        var cartridge = sp.GetRequiredService<IReportCartridge<TrialBalanceParameters, TrialBalanceResult>>();
        Assert.NotNull(cartridge);
        Assert.Equal(ReportKind.TrialBalance, cartridge.Kind);
    }

    [Fact]
    public void AddTrialBalanceCartridge_IReportRunnerIsResolvable()
    {
        var sp = BuildProvider();
        sp.UseBlocksReports();

        var runner = sp.GetRequiredService<IReportRunner>();
        Assert.NotNull(runner);
    }
    [Fact]
    public async Task AddTrialBalanceCartridge_RunnerDispatchesEndToEnd()
    {
        var chart = ChartOfAccountsId.NewId(); var source = new InMemoryReportQuerySource();
        source.Charts.Add(new ReportChart(chart, "TB DI", "USD", true));
        var services = new ServiceCollection(); services.AddSingleton<IReportQuerySource>(source);
        services.AddBlocksReportsSubstrate().AddTrialBalanceCartridge();
        var sp = services.BuildServiceProvider(); sp.UseBlocksReports();
        var result = await sp.GetRequiredService<IReportRunner>().RunAsync<TrialBalanceParameters, TrialBalanceResult>(
            ReportKind.TrialBalance, new() { ChartId = chart, AsOfDate = new DateOnly(2026, 5, 31) }, new Harborline.Foundation.Assets.Common.TenantId("di-tb"), Harborline.Foundation.Crypto.PrincipalId.FromBytes(new byte[32]));
        Assert.Equal(ReportKind.TrialBalance, result.Kind); Assert.NotNull(result.Result);
        Assert.Equal(chart, result.Result.ChartId); Assert.Equal("inmem:di-tb:1", result.SnapshotMarker);
    }
}
