using System;
using System.Linq;
using System.Threading.Tasks;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Blocks.Reports.Cartridges.ArAgingSummary;
using Harborline.Blocks.Reports.Exceptions;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Crypto;
using Xunit;

namespace Harborline.Blocks.Reports.Tests;

/// <summary>
/// W#72 PR 3 — unit tests for <see cref="ArAgingSummaryCartridge"/>.
/// Each test seeds neutral report rows through the shared query source.
/// </summary>
public sealed class ArAgingSummaryCartridgeTests
{
    private static readonly ChartOfAccountsId Chart = ChartOfAccountsId.NewId();
    private static readonly TenantId Tenant = new("tenant-ar-aging");
    private static readonly PrincipalId Principal = PrincipalId.FromBytes(new byte[32]);
    // Reference date: 2026-05-17 (today per env)
    private static readonly DateOnly Today = new(2026, 5, 17);
    private static int _invoiceSeq = 0;

    private static ReportExecutionContext Context(DateOnly? asOf = null)
    {
        var dt = asOf ?? Today;
        var utc = new DateTimeOffset(dt.Year, dt.Month, dt.Day, 12, 0, 0, TimeSpan.Zero);
        return new ReportExecutionContext(Tenant, "marker:ar:1", utc, Principal);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────

    private static (ArAgingSummaryCartridge Cartridge,
                    InMemoryReportQuerySource Source,
                    InMemoryReportQuerySource Parties)
        Build()
    {
        var source = new InMemoryReportQuerySource();
        source.Charts.Add(new ReportChart(Chart, "AR", "USD", true));
        return (new ArAgingSummaryCartridge(source), source, source);
    }

    [Fact]
    public async Task ArAgingSummary_UnknownChart_ThrowsValidationException()
    {
        var (sut, source, _) = Build();
        source.Charts.Clear();
        await Assert.ThrowsAsync<ReportParameterValidationException>(() => sut.ExecuteAsync(
            Context(), new ArAgingSummaryParameters { ChartId = Chart }));
    }

    private static ReportOpenItem MakeIssuedInvoice(
        PartyId customerId,
        DateOnly dueDate,
        decimal amount,
        string? propertyId = null)
    {
        var seq = System.Threading.Interlocked.Increment(ref _invoiceSeq);
        // Inert seed value: the fake classifies from DueDate and the requested asOf.
        return new ReportOpenItem($"INV-2026-05-17-TT-{seq:D4}", Tenant, Chart, customerId,
            propertyId, dueDate, amount, AgingBucket.Current);
    }

    private static Task<PartyId> SeedPartyAsync(InMemoryReportQuerySource parties, string name)
    {
        var id = NewPartyId();
        parties.Parties.Add(new ReportParty(id, name));
        return Task.FromResult(id);
    }

    private static PartyId NewPartyId() => new(Guid.NewGuid().ToString());
    [Theory]
    [InlineData(0, nameof(ArAgingSummaryRow.Current))]
    [InlineData(1, nameof(ArAgingSummaryRow.Days0To30))]
    [InlineData(30, nameof(ArAgingSummaryRow.Days0To30))]
    [InlineData(31, nameof(ArAgingSummaryRow.Days31To60))]
    [InlineData(60, nameof(ArAgingSummaryRow.Days31To60))]
    [InlineData(61, nameof(ArAgingSummaryRow.Days61To90))]
    [InlineData(90, nameof(ArAgingSummaryRow.Days61To90))]
    [InlineData(91, nameof(ArAgingSummaryRow.Days90Plus))]
    public async Task ArAgingSummary_BucketBoundaries_AreInclusive(int daysPastDue, string bucket)
    {
        var (sut, source, _) = Build();
        source.Receivables.Add(MakeIssuedInvoice(NewPartyId(), Today.AddDays(-daysPastDue), 17m));
        var totals = (await sut.ExecuteAsync(Context(),
            new ArAgingSummaryParameters { ChartId = Chart })).Totals;

        Assert.Equal(bucket == nameof(ArAgingSummaryRow.Current) ? 17m : 0m, totals.Current);
        Assert.Equal(bucket == nameof(ArAgingSummaryRow.Days0To30) ? 17m : 0m, totals.Days0To30);
        Assert.Equal(bucket == nameof(ArAgingSummaryRow.Days31To60) ? 17m : 0m, totals.Days31To60);
        Assert.Equal(bucket == nameof(ArAgingSummaryRow.Days61To90) ? 17m : 0m, totals.Days61To90);
        Assert.Equal(bucket == nameof(ArAgingSummaryRow.Days90Plus) ? 17m : 0m, totals.Days90Plus);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Edge case — empty
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ArAgingSummary_EmptyChart_ReturnsZeroRowsAndZeroTotals()
    {
        var (sut, _, _) = Build();
        var result = await sut.ExecuteAsync(Context(),
            new ArAgingSummaryParameters { ChartId = Chart });

        Assert.Empty(result.ByCustomer);
        Assert.Empty(result.ByProperty);
        Assert.Empty(result.TopDelinquent);
        Assert.Equal(0m, result.Totals.Current);
        Assert.Equal(0m, result.Totals.Days0To30);
        Assert.Equal(0m, result.Totals.Days31To60);
        Assert.Equal(0m, result.Totals.Days61To90);
        Assert.Equal(0m, result.Totals.Days90Plus);
        Assert.Equal(0m, result.Totals.TotalOpen);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Edge case — single record
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ArAgingSummary_SingleInvoiceCurrent_AppearsInCurrentBucket()
    {
        var (sut, invoices, _) = Build();
        var customer = NewPartyId();
        // Due tomorrow — still current.
        var inv = MakeIssuedInvoice(customer, Today.AddDays(1), 100m);
        await invoices.UpsertAsync(Tenant, inv);

        var result = await sut.ExecuteAsync(Context(),
            new ArAgingSummaryParameters { ChartId = Chart });

        Assert.Single(result.ByCustomer);
        Assert.Equal(100m, result.ByCustomer[0].Current);
        Assert.Equal(0m, result.ByCustomer[0].Days90Plus);
        Assert.Equal(100m, result.Totals.Current);
    }

    [Fact]
    public async Task ArAgingSummary_SingleInvoice90PlusDays_AppearsInTopDelinquent()
    {
        var (sut, invoices, parties) = Build();
        var customerId = await SeedPartyAsync(parties, "Big Debtor");

        // 100 days past due.
        var inv = MakeIssuedInvoice(customerId, Today.AddDays(-100), 500m);
        await invoices.UpsertAsync(Tenant, inv);

        var result = await sut.ExecuteAsync(Context(),
            new ArAgingSummaryParameters { ChartId = Chart, TopDelinquentN = 5 });

        Assert.Single(result.TopDelinquent);
        Assert.Equal(customerId, result.TopDelinquent[0].CustomerId);
        Assert.Equal("Big Debtor", result.TopDelinquent[0].CustomerName);
        Assert.Equal(500m, result.TopDelinquent[0].Days90PlusBalance);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Customer rollup
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ArAgingSummary_MultipleInvoicesSameCustomer_AggregatedInOneRow()
    {
        var (sut, invoices, _) = Build();
        var customer = NewPartyId();
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(customer, Today.AddDays(5), 100m));
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(customer, Today.AddDays(10), 200m));
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(customer, Today.AddDays(-10), 50m));

        var result = await sut.ExecuteAsync(Context(),
            new ArAgingSummaryParameters { ChartId = Chart });

        Assert.Single(result.ByCustomer);
        Assert.Equal(300m, result.ByCustomer[0].Current); // 100 + 200 current
        Assert.Equal(50m, result.ByCustomer[0].Days0To30); // 10 days past due
        Assert.Equal(350m, result.ByCustomer[0].TotalOpen);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Property rollup
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ArAgingSummary_MultipleInvoicesSameProperty_AggregatedInOneRow()
    {
        var (sut, invoices, _) = Build();
        var c1 = NewPartyId();
        var c2 = NewPartyId();
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(c1, Today.AddDays(5), 100m, "prop-A"));
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(c2, Today.AddDays(5), 200m, "prop-A"));

        var result = await sut.ExecuteAsync(Context(),
            new ArAgingSummaryParameters { ChartId = Chart });

        var propRow = result.ByProperty.Single(r => r.GroupKey == "prop-A");
        Assert.Equal(300m, propRow.Current);
    }

    [Fact]
    public async Task ArAgingSummary_InvoiceWithNullPropertyId_RolledIntoUnassigned()
    {
        var (sut, invoices, _) = Build();
        var customer = NewPartyId();
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(customer, Today.AddDays(5), 75m, propertyId: null));

        var result = await sut.ExecuteAsync(Context(),
            new ArAgingSummaryParameters { ChartId = Chart });

        var unassigned = result.ByProperty.Single(r => r.GroupKey == "Unassigned");
        Assert.Equal(75m, unassigned.Current);
    }

    [Fact]
    public async Task ArAgingSummary_UnassignedSortsLast()
    {
        var (sut, invoices, _) = Build();
        var customer = NewPartyId();
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(customer, Today.AddDays(5), 50m, "prop-Z"));
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(customer, Today.AddDays(5), 50m, propertyId: null));

        var result = await sut.ExecuteAsync(Context(),
            new ArAgingSummaryParameters { ChartId = Chart });

        Assert.Equal("Unassigned", result.ByProperty.Last().GroupKey);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Filters
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ArAgingSummary_CustomerIdsFilter_OmitsOtherCustomers()
    {
        var (sut, invoices, _) = Build();
        var included = NewPartyId();
        var excluded = NewPartyId();
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(included, Today.AddDays(5), 100m));
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(excluded, Today.AddDays(5), 200m));

        var result = await sut.ExecuteAsync(Context(),
            new ArAgingSummaryParameters
            {
                ChartId = Chart,
                CustomerIds = new[] { included },
            });

        Assert.Single(result.ByCustomer);
        Assert.Equal(included.Value, result.ByCustomer[0].GroupKey);
        Assert.Equal(100m, result.Totals.TotalOpen);
    }

    [Fact]
    public async Task ArAgingSummary_PropertyIdsFilter_OmitsOtherProperties()
    {
        var (sut, invoices, _) = Build();
        var customer = NewPartyId();
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(customer, Today.AddDays(5), 100m, "prop-A"));
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(customer, Today.AddDays(5), 200m, "prop-B"));

        var result = await sut.ExecuteAsync(Context(),
            new ArAgingSummaryParameters
            {
                ChartId = Chart,
                PropertyIds = new[] { "prop-A" },
            });

        Assert.Single(result.ByProperty);
        Assert.Equal("prop-A", result.ByProperty[0].GroupKey);
        Assert.Equal(100m, result.Totals.TotalOpen);
    }

    // When the property filter is active, invoices with null PropertyId
    // (Unassigned) are excluded from the filtered view.
    [Fact]
    public async Task ArAgingSummary_PropertyIdsFilter_ExcludesUnassigned()
    {
        var (sut, invoices, _) = Build();
        var customer = NewPartyId();
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(customer, Today.AddDays(5), 100m, "prop-A"));
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(customer, Today.AddDays(5), 50m, propertyId: null));

        var result = await sut.ExecuteAsync(Context(),
            new ArAgingSummaryParameters
            {
                ChartId = Chart,
                PropertyIds = new[] { "prop-A" },
            });

        Assert.DoesNotContain(result.ByProperty, r => r.GroupKey == "Unassigned");
        Assert.Equal(100m, result.Totals.TotalOpen);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Parameter validation
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ArAgingSummary_TopDelinquentNNegative_ThrowsValidationException()
    {
        var (sut, _, _) = Build();
        await Assert.ThrowsAsync<ReportParameterValidationException>(() =>
            sut.ExecuteAsync(Context(),
                new ArAgingSummaryParameters { ChartId = Chart, TopDelinquentN = -1 }));
    }

    [Fact]
    public async Task ArAgingSummary_TopDelinquentNOverCap_ThrowsValidationException()
    {
        var (sut, _, _) = Build();
        await Assert.ThrowsAsync<ReportParameterValidationException>(() =>
            sut.ExecuteAsync(Context(),
                new ArAgingSummaryParameters { ChartId = Chart, TopDelinquentN = 101 }));
    }

    // ──────────────────────────────────────────────────────────────────
    //  Top-delinquent behaviour
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ArAgingSummary_TopDelinquent_OrderedDescendingBy90Plus()
    {
        var (sut, invoices, _) = Build();
        var small = NewPartyId();
        var large = NewPartyId();
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(small, Today.AddDays(-100), 100m));
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(large, Today.AddDays(-100), 500m));

        var result = await sut.ExecuteAsync(Context(),
            new ArAgingSummaryParameters { ChartId = Chart, TopDelinquentN = 10 });

        Assert.Equal(2, result.TopDelinquent.Count);
        Assert.Equal(500m, result.TopDelinquent[0].Days90PlusBalance);
        Assert.Equal(100m, result.TopDelinquent[1].Days90PlusBalance);
    }

    [Fact]
    public async Task ArAgingSummary_TopDelinquentN_RespectsCap()
    {
        var (sut, invoices, _) = Build();
        for (var i = 0; i < 5; i++)
        {
            var c = NewPartyId();
            await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(c, Today.AddDays(-100), 100m));
        }

        var result = await sut.ExecuteAsync(Context(),
            new ArAgingSummaryParameters { ChartId = Chart, TopDelinquentN = 3 });

        Assert.Equal(3, result.TopDelinquent.Count);
    }

    [Fact]
    public async Task ArAgingSummary_TopDelinquentN_ZeroReturnsEmpty()
    {
        var (sut, invoices, _) = Build();
        var c = NewPartyId();
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(c, Today.AddDays(-100), 100m));

        var result = await sut.ExecuteAsync(Context(),
            new ArAgingSummaryParameters { ChartId = Chart, TopDelinquentN = 0 });

        Assert.Empty(result.TopDelinquent);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Totals consistency
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ArAgingSummary_Totals_EqualSumOfByCustomerRows()
    {
        var (sut, invoices, _) = Build();
        var c1 = NewPartyId();
        var c2 = NewPartyId();
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(c1, Today.AddDays(5), 100m));
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(c1, Today.AddDays(-10), 50m));
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(c2, Today.AddDays(-50), 200m));

        var result = await sut.ExecuteAsync(Context(),
            new ArAgingSummaryParameters { ChartId = Chart });

        var summedFromCustomers = result.ByCustomer.Sum(r => r.TotalOpen);
        Assert.Equal(summedFromCustomers, result.Totals.TotalOpen);
    }

    [Fact]
    public async Task ArAgingSummary_Totals_EqualSumOfByPropertyRows()
    {
        var (sut, invoices, _) = Build();
        var customer = NewPartyId();
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(customer, Today.AddDays(5), 100m, "prop-A"));
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(customer, Today.AddDays(-10), 50m, "prop-B"));
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(customer, Today.AddDays(-50), 200m, propertyId: null));

        var result = await sut.ExecuteAsync(Context(),
            new ArAgingSummaryParameters { ChartId = Chart });

        var summedFromProperty = result.ByProperty.Sum(r => r.TotalOpen);
        Assert.Equal(summedFromProperty, result.Totals.TotalOpen);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Party name resolution
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ArAgingSummary_CustomerName_ResolvedFromPartyReadModel()
    {
        var (sut, invoices, parties) = Build();
        var customerId = await SeedPartyAsync(parties, "Jane Tenant");
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(customerId, Today.AddDays(5), 100m));

        var result = await sut.ExecuteAsync(Context(),
            new ArAgingSummaryParameters { ChartId = Chart });

        Assert.Equal("Jane Tenant", result.ByCustomer.Single().GroupLabel);
    }

    [Fact]
    public async Task ArAgingSummary_UnknownCustomer_FallsBackToPartyIdValue()
    {
        var (sut, invoices, _) = Build();
        var customerId = NewPartyId();
        // Do NOT seed a Party record — resolution should degrade gracefully.
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(customerId, Today.AddDays(5), 100m));

        var result = await sut.ExecuteAsync(Context(),
            new ArAgingSummaryParameters { ChartId = Chart });

        Assert.Equal(customerId.Value, result.ByCustomer.Single().GroupLabel);
    }

    // ──────────────────────────────────────────────────────────────────
    //  AsOfDate wiring
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ArAgingSummary_ExplicitAsOfDate_UsedForBucketClassification()
    {
        var (sut, invoices, _) = Build();
        var customer = NewPartyId();
        var contextDate = new DateOnly(2026, 5, 17);
        var explicitAsOf = new DateOnly(2026, 6, 1);
        var dueDate = new DateOnly(2026, 4, 22);
        await invoices.UpsertAsync(Tenant, MakeIssuedInvoice(customer, dueDate, 300m));

        var result = await sut.ExecuteAsync(Context(contextDate),
            new ArAgingSummaryParameters { ChartId = Chart, AsOfDate = explicitAsOf });

        Assert.Equal(explicitAsOf, result.AsOf);
        // Explicit asOf: 40 days => Days31To60; context date would be 25 => Days0To30.
        Assert.Equal(300m, result.ByCustomer.Single().Days31To60);
        Assert.Equal(0m, result.ByCustomer.Single().Days0To30);
    }

    [Fact]
    public async Task ArAgingSummary_NullAsOfDate_DefaultsToContextDate()
    {
        var (sut, _, _) = Build();
        var result = await sut.ExecuteAsync(Context(),
            new ArAgingSummaryParameters { ChartId = Chart });

        Assert.Equal(Today, result.AsOf);
    }

    [Fact]
    public async Task ArAgingSummary_ForwardsResolvedAsOf()
    {
        var (sut, source, _) = Build();
        await sut.ExecuteAsync(Context(), new ArAgingSummaryParameters { ChartId = Chart });
        Assert.Equal([Today], source.ObservedReceivableAsOfs);
    }
}

internal static class ArAgingSummarySourceSeedExtensions
{
    internal static Task UpsertAsync(
        this InMemoryReportQuerySource source,
        TenantId tenantId,
        ReportOpenItem item)
    {
        _ = tenantId;
        source.Receivables.Add(item);
        return Task.CompletedTask;
    }
}
