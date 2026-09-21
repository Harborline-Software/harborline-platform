using System.Globalization;
using System.Text.Json.Nodes;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;

namespace Harborline.Blocks.Reports.Measures;

/// <summary>
/// Folds the catalogue's bound Access predicate into the report read boundary, so every row a
/// cartridge sees has already passed it. The cartridge math is unchanged: it never learns that a
/// row was withheld, and no count, bucket, group or total can include a row the caller cannot open.
/// This class contains no access policy of its own.
/// </summary>
internal sealed class AccessFilteredReportQuerySource(
    IReportQuerySource inner,
    AccessSetPredicate predicate,
    string tenant,
    string recordKind) : IReportQuerySource
{
    private static readonly IReadOnlyDictionary<string, JsonNode?> NoFields =
        new Dictionary<string, JsonNode?>(StringComparer.Ordinal);

    public async ValueTask<ReportChart?> GetChartAsync(ChartOfAccountsId chartId, CancellationToken cancellationToken = default)
    {
        var chart = await inner.GetChartAsync(chartId, cancellationToken).ConfigureAwait(false);
        return chart is null || !await VisibleAsync(chart.Id.Value.ToString(), cancellationToken).ConfigureAwait(false) ? null : chart;
    }

    public async ValueTask<ReportFiscalPeriod?> GetFiscalPeriodAsync(FiscalPeriodId periodId, CancellationToken cancellationToken = default)
    {
        var period = await inner.GetFiscalPeriodAsync(periodId, cancellationToken).ConfigureAwait(false);
        return period is null || !await VisibleAsync(period.Id.Value.ToString(), cancellationToken).ConfigureAwait(false) ? null : period;
    }

    public async ValueTask<IReadOnlyList<ReportAccount>> GetAccountsAsync(ChartOfAccountsId chartId, bool includeInactive, CancellationToken cancellationToken = default) =>
        await NarrowAsync(await inner.GetAccountsAsync(chartId, includeInactive, cancellationToken).ConfigureAwait(false),
            account => Record(account.Id.Value.ToString(), ("code", account.Code)), cancellationToken).ConfigureAwait(false);

    public async ValueTask<IReadOnlyList<ReportJournalEntry>> GetPostedJournalEntriesAsync(TenantId tenantId, ChartOfAccountsId chartId, DateOnly? from, DateOnly through, string snapshotMarker, CancellationToken cancellationToken = default) =>
        await NarrowAsync(await inner.GetPostedJournalEntriesAsync(tenantId, chartId, from, through, snapshotMarker, cancellationToken).ConfigureAwait(false),
            entry => Record(entry.Id, ("entryDate", entry.EntryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))), cancellationToken).ConfigureAwait(false);

    public async ValueTask<IReadOnlyList<ReportOpenItem>> GetOpenReceivablesAsync(TenantId tenantId, ChartOfAccountsId chartId, DateOnly asOf, CancellationToken cancellationToken = default) =>
        await NarrowAsync(await inner.GetOpenReceivablesAsync(tenantId, chartId, asOf, cancellationToken).ConfigureAwait(false),
            item => Record(item.Id, ("party", item.PartyId.Value), ("property", item.PropertyId ?? string.Empty)), cancellationToken).ConfigureAwait(false);

    public async ValueTask<IReadOnlyList<ReportOpenItem>> GetOpenPayablesAsync(TenantId tenantId, ChartOfAccountsId chartId, DateOnly asOf, CancellationToken cancellationToken = default) =>
        await NarrowAsync(await inner.GetOpenPayablesAsync(tenantId, chartId, asOf, cancellationToken).ConfigureAwait(false),
            item => Record(item.Id, ("party", item.PartyId.Value), ("property", item.PropertyId ?? string.Empty)), cancellationToken).ConfigureAwait(false);

    public async ValueTask<IReadOnlyDictionary<PartyId, ReportParty>> GetPartiesAsync(IReadOnlyCollection<PartyId> partyIds, CancellationToken cancellationToken = default)
    {
        var parties = await inner.GetPartiesAsync(partyIds, cancellationToken).ConfigureAwait(false);
        var visible = await NarrowAsync(parties.Values.ToArray(), party => Record(party.Id.Value), cancellationToken).ConfigureAwait(false);
        return visible.ToDictionary(party => party.Id);
    }

    public async ValueTask<IReadOnlyList<ReportLease>> GetLeasesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await NarrowAsync(await inner.GetLeasesAsync(tenantId, cancellationToken).ConfigureAwait(false),
            lease => Record(lease.Id.Value.ToString(), ("property", lease.PropertyKey), ("unit", lease.UnitLabel)), cancellationToken).ConfigureAwait(false);

    private ValueTask<IReadOnlyList<T>> NarrowAsync<T>(IReadOnlyList<T> rows, Func<T, AccessRecord> record, CancellationToken cancellationToken) =>
        predicate.FilterAsync(rows, record, cancellationToken);

    private async ValueTask<bool> VisibleAsync(string id, CancellationToken cancellationToken) =>
        (await predicate.CheckAsync(Record(id), cancellationToken).ConfigureAwait(false)).Allowed;

    private AccessRecord Record(string id, params (string Name, string Value)[] fields) =>
        new(tenant, recordKind, id, fields.Length == 0
            ? NoFields
            : fields.ToDictionary(field => field.Name, field => (JsonNode?)JsonValue.Create(field.Value), StringComparer.Ordinal));
}
