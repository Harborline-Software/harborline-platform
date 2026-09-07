using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Blocks.Reports.Exceptions;

namespace Harborline.Blocks.Reports.Cartridges.ArAgingSummary;

/// <summary>
/// W#72 PR 3 — AR Aging Summary cartridge per Stage 02 §4.14.
/// Delegates aging computation to <c>IArAgingService</c> (which owns
/// the bucket logic), then builds per-customer and per-property rollups
/// at the portfolio level.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read-side discipline.</b> No writes, no event publication.
/// The cartridge is a pure orchestrator over read-side services.
/// </para>
/// <para>
/// <b>Tenant isolation.</b> Per the hand-off D4-C precedent established
/// in Trial Balance: the caller is responsible for resolving
/// <c>ChartId → LegalEntity → TenantId</c> before calling the
/// cartridge. The cartridge validates chart existence only.
/// </para>
/// <para>
/// <b>Party name resolution.</b> Customer names are resolved via
/// <c>IPartyReadModel.GetManyAsync</c>. When a party is missing
/// or tombstoned the <see cref="PartyId.Value"/> string is used as the
/// label (degraded display, not an error).
/// </para>
/// </remarks>
public sealed class ArAgingSummaryCartridge
    : IReportCartridge<ArAgingSummaryParameters, ArAgingSummaryResult>
{
    private readonly IReportQuerySource _source;

    /// <summary>Construct bound to the AR aging service and party read model.</summary>
    public ArAgingSummaryCartridge(IReportQuerySource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <inheritdoc />
    public ReportKind Kind => ReportKind.ArAgingSummary;

    /// <inheritdoc />
    public async Task<ArAgingSummaryResult> ExecuteAsync(
        ReportExecutionContext context,
        ArAgingSummaryParameters parameters,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // 1. Parameter validation.
        if (parameters.TopDelinquentN < 0 || parameters.TopDelinquentN > 100)
            throw new ReportParameterValidationException(
                nameof(parameters.TopDelinquentN),
                "TopDelinquentN must be in the range 0..100.");
        var chart = await _source.GetChartAsync(parameters.ChartId, ct).ConfigureAwait(false);
        if (chart is null)
            throw new ReportParameterValidationException(
                nameof(parameters.ChartId),
                $"ChartId {parameters.ChartId} not found.");

        var asOf = parameters.AsOfDate
            ?? DateOnly.FromDateTime(context.AsOfUtc.UtcDateTime);

        // 2. Fetch the full aging snapshot for the chart in one call.
        //    IArAgingService.GetAgingForChartAsync returns all open-invoice rows;
        //    we group in-memory for both customer and property rollups so the
        //    service is called exactly once (determinism + minimal I/O).
        var rows = await _source.GetOpenReceivablesAsync(context.TenantId, parameters.ChartId, asOf, ct).ConfigureAwait(false);

        // 3. Apply optional customer-id filter (post-aggregation; silently excludes).
        if (parameters.CustomerIds is { Count: > 0 })
        {
            var allowed = new HashSet<string>(
                parameters.CustomerIds.Select(id => id.Value),
                StringComparer.Ordinal);
            rows = rows.Where(r => allowed.Contains(r.PartyId.Value)).ToList();
        }

        // 4. Apply optional property-id filter (post-aggregation; silently excludes).
        //    Note: rows with null PropertyId pass through unless PropertyIds is set
        //    (in which case they are excluded from the filtered view — Unassigned rows
        //    only appear when no property filter is active).
        if (parameters.PropertyIds is { Count: > 0 })
        {
            var allowed = new HashSet<string>(parameters.PropertyIds, StringComparer.Ordinal);
            rows = rows.Where(r => r.PropertyId is not null && allowed.Contains(r.PropertyId)).ToList();
        }

        // 5. Build customer rollup.
        var customerGroups = rows
            .GroupBy(r => r.PartyId.Value, StringComparer.Ordinal)
            .ToList();

        // Resolve customer display names in bulk.
        var customerPartyIds = customerGroups
            .Select(g => new PartyId(g.Key))
            .ToList();
        var partyMap = await _source.GetPartiesAsync(customerPartyIds, ct)
            .ConfigureAwait(false);

        var byCustomer = customerGroups
            .Select(g =>
            {
                var partyId = new PartyId(g.Key);
                var label = partyMap.TryGetValue(partyId, out var party)
                    ? party.DisplayName
                    : g.Key;
                return SumRows(g.Key, label, g);
            })
            .OrderBy(r => r.GroupKey, StringComparer.Ordinal)
            .ToList();

        // 6. Build property rollup.
        var propertyGroups = rows
            .GroupBy(r => r.PropertyId ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        var byProperty = propertyGroups
            .Select(g =>
            {
                var key = g.Key.Length == 0 ? "Unassigned" : g.Key;
                return SumRows(key, key, g);
            })
            // "Unassigned" sorts last.
            .OrderBy(r => r.GroupKey == "Unassigned" ? 1 : 0)
            .ThenBy(r => r.GroupKey, StringComparer.Ordinal)
            .ToList();

        // 7. Portfolio totals — sum over the (filtered) customer rollup so customer
        //    and total are always consistent.
        var totals = SumRows("All", "All", rows);

        // 8. Top-N delinquents — ranked by 90+ balance descending, then total open descending.
        var topDelinquent = parameters.TopDelinquentN == 0
            ? (IReadOnlyList<TopDelinquentCustomer>)Array.Empty<TopDelinquentCustomer>()
            : byCustomer
                .Where(r => r.Days90Plus > 0m)
                .OrderByDescending(r => r.Days90Plus)
                .ThenByDescending(r => r.TotalOpen)
                .Take(parameters.TopDelinquentN)
                .Select(r => new TopDelinquentCustomer(
                    CustomerId: new PartyId(r.GroupKey),
                    CustomerName: r.GroupLabel,
                    Days90PlusBalance: r.Days90Plus,
                    TotalOpenBalance: r.TotalOpen))
                .ToList();

        return new ArAgingSummaryResult(
            ChartId: parameters.ChartId,
            AsOf: asOf,
            ByCustomer: byCustomer,
            ByProperty: byProperty,
            Totals: totals,
            TopDelinquent: topDelinquent);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────

    private static ArAgingSummaryRow SumRows(
        string key,
        string label,
        IEnumerable<ReportOpenItem> source)
    {
        decimal current = 0m, d0to30 = 0m, d31to60 = 0m, d61to90 = 0m, d90plus = 0m;
        foreach (var row in source)
        {
            switch (row.Bucket)
            {
                case AgingBucket.Current:    current += row.OpenBalance; break;
                case AgingBucket.Days0To30:  d0to30  += row.OpenBalance; break;
                case AgingBucket.Days31To60: d31to60 += row.OpenBalance; break;
                case AgingBucket.Days61To90: d61to90 += row.OpenBalance; break;
                case AgingBucket.Days90Plus: d90plus += row.OpenBalance; break;
            }
        }
        return new ArAgingSummaryRow(
            GroupKey: key,
            GroupLabel: label,
            Current: current,
            Days0To30: d0to30,
            Days31To60: d31to60,
            Days61To90: d61to90,
            Days90Plus: d90plus,
            TotalOpen: current + d0to30 + d31to60 + d61to90 + d90plus);
    }
}
