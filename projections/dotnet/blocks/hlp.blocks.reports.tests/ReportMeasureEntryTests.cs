using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Harborline.Blocks.Aggregates;
using Harborline.Blocks.MeasureCatalogue;
using Harborline.Blocks.Reports.Cartridges.TrialBalance;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Blocks.Reports.Measures;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Crypto;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

/// <summary>
/// The seven shipped computations as catalogue entries. Every fixture here binds the production
/// AccessProvider over the host's gate adapter; there is no allow-all filter.
/// </summary>
public sealed class ReportMeasureEntryTests
{
    private static readonly ChartOfAccountsId Chart = ChartOfAccountsId.NewId();
    private static readonly TenantId Tenant = new("tenant-m");
    private static readonly PrincipalId Principal = PrincipalId.FromBytes(new byte[32]);
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PeriodEnd = new(2026, 6, 30, 23, 59, 59, TimeSpan.Zero);

    // measure-catalogue-eng-9 / measure-catalogue-ck-2.
    [Fact]
    public async Task Exactly_the_seven_shipped_computations_are_registered_as_bound_entries()
    {
        var entries = ReportMeasureEntries.All();
        var catalogue = new MeasureCatalogue.MeasureCatalogue(new AccessProvider(new HostGate()), entries);

        Assert.Equal(7, entries.Count);
        Assert.Equal(Enum.GetValues<ReportKind>().Length, entries.Count);
        Assert.Equal(
            ["finance.balance-sheet", "finance.profit-and-loss", "finance.profit-and-loss-by-property",
             "finance.trial-balance", "occupancy.rent-roll", "payables.aging-summary", "receivables.aging-summary"],
            entries.Select(entry => entry.Reference.Value).Order(StringComparer.Ordinal));
        foreach (var entry in entries)
        {
            Assert.NotNull(await catalogue.ResolveAsync(entry.Reference));
            // No address tells the consumer the entry is written in code.
            Assert.DoesNotContain("cartridge", entry.Reference.Value, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("report", entry.Reference.Value, StringComparison.OrdinalIgnoreCase);
        }
    }

    // measure-catalogue-eng-9 / measure-catalogue-ck-2.
    [Fact]
    public async Task Golden_inputs_produce_the_same_typed_results_as_the_source_implementation()
    {
        var source = Chart5();
        var context = new ReportExecutionContext(Tenant, "marker:1", Now, Principal);
        var direct = await new TrialBalanceCartridge(source).ExecuteAsync(context,
            new TrialBalanceParameters { ChartId = Chart, AsOfDate = new DateOnly(2026, 9, 20) });

        var result = await Evaluate(ReportMeasureEntries.TrialBalance, source, new HostGate(), Now, "marker:1");

        Assert.Equal(direct.TotalDebit.ToString(System.Globalization.CultureInfo.InvariantCulture), Total(result, "debit").Value);
        Assert.Equal(direct.TotalCredit.ToString(System.Globalization.CultureInfo.InvariantCulture), Total(result, "credit").Value);
        Assert.Equal(direct.Rows.Count, result.Groups.Count(group => group.Kind == AggregateGroupKind.Detail));
        foreach (var row in direct.Rows)
        {
            var group = result.Groups.Single(item => item.Kind == AggregateGroupKind.Detail
                && item.Keys.Single().Value as string == row.AccountCode);
            Assert.Equal(row.DebitBalance.ToString(System.Globalization.CultureInfo.InvariantCulture),
                group.Measures.Single(cell => cell.Key == "debit").Value);
            Assert.Equal(row.CreditBalance.ToString(System.Globalization.CultureInfo.InvariantCulture),
                group.Measures.Single(cell => cell.Key == "credit").Value);
        }
    }

    // measure-catalogue-eng-1 / measure-catalogue-cc-12: through a bound entry, over rows the
    // cartridge pulls for itself.
    [Fact]
    public async Task The_production_access_filter_runs_before_the_cartridge_groups_buckets_or_totals()
    {
        var source = Receivables();
        var open = await Evaluate(ReportMeasureEntries.ReceivablesAging, source, new HostGate(), Now, "marker:1");
        var narrowed = await Evaluate(ReportMeasureEntries.ReceivablesAging, source,
            new HostGate("ar-hidden-1", "ar-hidden-2"), Now, "marker:1");

        Assert.Equal(5, open.Groups.Count(group => group.Kind == AggregateGroupKind.Detail));
        Assert.Equal("1500", Total(open, "total_open").Value);

        // Three readable rows out of five interleaved: three groups, and both hidden values are
        // absent from every bucket and from the total.
        Assert.Equal(3, narrowed.Groups.Count(group => group.Kind == AggregateGroupKind.Detail));
        Assert.Equal("60", Total(narrowed, "total_open").Value);
        Assert.Equal("60", Total(narrowed, "days_90_plus").Value);
        Assert.DoesNotContain(narrowed.Groups, group => group.Keys.Any(key => (key.Value as string)?.StartsWith("ar-hidden", StringComparison.Ordinal) == true));
    }

    // measure-catalogue-eng-8 / measure-catalogue-ck-5.
    [Fact]
    public async Task One_reference_bound_to_three_bases_uses_exactly_the_callers_rows_and_instant()
    {
        var live = Receivables();
        var draft = Receivables(onlyFirst: true);

        var atNow = await Evaluate(ReportMeasureEntries.ReceivablesAging, live, new HostGate(), Now, "live");
        var atPinnedBasis = await Evaluate(ReportMeasureEntries.ReceivablesAging, live, new HostGate(), PeriodEnd, "basis:2026-06");
        var overDraft = await Evaluate(ReportMeasureEntries.ReceivablesAging, draft, new HostGate(), Now, "draft:1");

        // One definition, three bindings, three answers, and no new measure was created.
        Assert.Equal("live", atNow.Basis);
        Assert.Equal("basis:2026-06", atPinnedBasis.Basis);
        Assert.Equal("draft:1", overDraft.Basis);
        Assert.Equal(atNow.Reference, atPinnedBasis.Reference);
        Assert.Equal(atNow.Reference, overDraft.Reference);
        // Only the rows changed.
        Assert.Equal("1500", Total(atNow, "total_open").Value);
        Assert.Equal("10", Total(overDraft, "total_open").Value);
        // Only the instant changed: the same rows age into different buckets.
        Assert.Equal("1500", Total(atPinnedBasis, "total_open").Value);
        Assert.Equal("1500", Total(atNow, "days_90_plus").Value);
        Assert.Equal("0", Total(atPinnedBasis, "days_90_plus").Value);
        Assert.Equal("1500", Total(atPinnedBasis, "days_31_60").Value);
    }

    // The last acceptance line, on the bound adapter.
    [Fact]
    public async Task Null_and_unavailable_stay_distinct_from_zero_through_the_bound_adapter()
    {
        var source = new InMemoryReportQuerySource();
        source.Charts.Add(new ReportChart(Chart, "Test", "USD", true));

        var result = await Evaluate(ReportMeasureEntries.RentRoll, source, new HostGate(), Now, "marker:1");

        // The cartridge reports a zero occupancy rate over no units; a rate over nothing is absent.
        Assert.Equal(AggregateCellState.Null, Total(result, "occupancy_rate").State);
        Assert.Null(Total(result, "occupancy_rate").Value);
        // The portfolio summary carries no fully-leased figure at all, which is unavailable.
        Assert.Equal(AggregateCellState.Unavailable, Total(result, "monthly_rent_total_if_fully_leased").State);
        Assert.Null(Total(result, "monthly_rent_total_if_fully_leased").Value);
        // A genuine zero is still a value, and still zero.
        Assert.Equal(AggregateCellState.Value, Total(result, "monthly_rent_total").State);
        Assert.Equal("0", Total(result, "monthly_rent_total").Value);
        Assert.Equal(0L, Total(result, "total_units").Value);
    }

    [Fact]
    public async Task A_bound_entry_admits_only_its_declared_parameters_and_only_a_report_basis()
    {
        var catalogue = new MeasureCatalogue.MeasureCatalogue(new AccessProvider(new HostGate()), ReportMeasureEntries.All());
        var basis = new ReportMeasureBasis("marker:1", Receivables(), Tenant, Principal);

        var missing = await Assert.ThrowsAsync<MeasureException>(async () => await catalogue.EvaluateAsync(
            ReportMeasureEntries.ReceivablesAging,
            new MeasureRequest(Tenant.Value, "alice", new BasisRows(basis), Now,
                new Dictionary<string, string>(StringComparer.Ordinal))));
        Assert.Equal(MeasureCodes.ParameterMissing, missing.Code);
        Assert.Equal("/measures/receivables.aging-summary/parameters/chart", missing.Pointer);

        var foreign = await Assert.ThrowsAsync<MeasureException>(async () => await catalogue.EvaluateAsync(
            ReportMeasureEntries.ReceivablesAging,
            new MeasureRequest(Tenant.Value, "alice", new SuppliedRows([]), Now, Parameters())));
        Assert.Equal(MeasureCodes.BasisUnsupported, foreign.Code);
    }

    private static async Task<MeasureResult> Evaluate(MeasureRef reference, IReportQuerySource source,
        HostGate gate, DateTimeOffset at, string token)
    {
        var catalogue = new MeasureCatalogue.MeasureCatalogue(new AccessProvider(gate), ReportMeasureEntries.All());
        return await catalogue.EvaluateAsync(reference, new MeasureRequest(Tenant.Value, "alice",
            new BasisRows(new ReportMeasureBasis(token, source, Tenant, Principal)), at, Parameters()));
    }

    private static Dictionary<string, string> Parameters() =>
        new(StringComparer.Ordinal) { ["chart"] = Chart.Value.ToString() };

    private static AggregateCell Total(MeasureResult result, string key) =>
        result.Groups.Single(group => group.Kind == AggregateGroupKind.GrandTotal).Measures.Single(cell => cell.Key == key);

    private static InMemoryReportQuerySource Chart5()
    {
        var source = new InMemoryReportQuerySource();
        source.Charts.Add(new ReportChart(Chart, "Test", "USD", true));
        var cash = new ReportAccount(GLAccountId.NewId(), Chart, "1000", "Cash", GLAccountType.Asset, NormalBalance.Debit, true);
        var revenue = new ReportAccount(GLAccountId.NewId(), Chart, "4000", "Rent", GLAccountType.Revenue, NormalBalance.Credit, true);
        source.Accounts.AddRange([cash, revenue]);
        source.JournalEntries.Add(new ReportJournalEntry("je-1", Tenant, Chart, new DateOnly(2026, 5, 1),
            [new ReportJournalLine(cash.Id, 100m, 0m, null), new ReportJournalLine(revenue.Id, 0m, 100m, null)]));
        return source;
    }

    // Three readable and two forbidden rows, interleaved, each on its own party so a dropped row is
    // a dropped group as well as a missing sum.
    private static InMemoryReportQuerySource Receivables(bool onlyFirst = false)
    {
        var source = new InMemoryReportQuerySource();
        source.Charts.Add(new ReportChart(Chart, "Test", "USD", true));
        (string Id, string Party, decimal Amount)[] items =
        [
            ("ar-open-1", "p1", 10m),
            ("ar-hidden-1", "p2", 700m),
            ("ar-open-2", "p3", 20m),
            ("ar-hidden-2", "p4", 740m),
            ("ar-open-3", "p5", 30m),
        ];
        foreach (var (id, party, amount) in onlyFirst ? items.Take(1).ToArray() : items)
        {
            source.Receivables.Add(new ReportOpenItem(id, Tenant, Chart, new PartyId(party), null,
                new DateOnly(2026, 5, 1), amount, AgingBucket.Current));
            source.Parties.Add(new ReportParty(new PartyId(party), party.ToUpperInvariant()));
        }
        return source;
    }

    // The host's sole authorization decider. It hides exactly the ids it is told to hide.
    private sealed class HostGate(params string[] hidden) : IAuthorizationDecider
    {
        private readonly HashSet<string> _hidden = new(hidden, StringComparer.Ordinal);

        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            var allowed = !_hidden.Contains(request.Record.Id);
            return ValueTask.FromResult(new AuthorizationDecisionEvidence(request, allowed,
                allowed ? "None" : "record_not_visible", "grant:1", [], []));
        }
    }
}
