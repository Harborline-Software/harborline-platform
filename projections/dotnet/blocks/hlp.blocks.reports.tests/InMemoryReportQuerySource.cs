using Harborline.Blocks.Reports.Inputs;
using Harborline.Foundation.Assets.Common;

namespace Harborline.Blocks.Reports.Tests;

internal sealed class InMemoryReportQuerySource : IReportQuerySource
{
    public List<ReportChart> Charts { get; } = [];
    public List<ReportFiscalPeriod> FiscalPeriods { get; } = [];
    public List<ReportAccount> Accounts { get; } = [];
    public List<ReportJournalEntry> JournalEntries { get; } = [];
    public List<ReportOpenItem> Receivables { get; } = [];
    public List<ReportOpenItem> Payables { get; } = [];
    public List<ReportParty> Parties { get; } = [];
    public List<ReportLease> Leases { get; } = [];
    public List<string> ObservedJournalMarkers { get; } = [];
    public List<DateOnly> ObservedReceivableAsOfs { get; } = [];
    public List<DateOnly> ObservedPayableAsOfs { get; } = [];

    public ValueTask<ReportChart?> GetChartAsync(ChartOfAccountsId id, CancellationToken ct = default) => ValueTask.FromResult(Charts.SingleOrDefault(x => x.Id == id));
    public ValueTask<ReportFiscalPeriod?> GetFiscalPeriodAsync(FiscalPeriodId id, CancellationToken ct = default) => ValueTask.FromResult(FiscalPeriods.SingleOrDefault(x => x.Id == id));
    public ValueTask<IReadOnlyList<ReportAccount>> GetAccountsAsync(ChartOfAccountsId id, bool includeInactive, CancellationToken ct = default) => ValueTask.FromResult<IReadOnlyList<ReportAccount>>(Accounts.Where(x => x.ChartId == id && (includeInactive || x.IsActive)).ToList());
    public ValueTask<IReadOnlyList<ReportJournalEntry>> GetPostedJournalEntriesAsync(TenantId tenant, ChartOfAccountsId chart, DateOnly? from, DateOnly through, string marker, CancellationToken ct = default)
    {
        ObservedJournalMarkers.Add(marker);
        return ValueTask.FromResult<IReadOnlyList<ReportJournalEntry>>(JournalEntries.Where(x => x.TenantId == tenant && x.ChartId == chart && x.EntryDate <= through && (from is null || x.EntryDate >= from)).ToList());
    }

    public ValueTask<IReadOnlyList<ReportOpenItem>> GetOpenReceivablesAsync(TenantId tenant, ChartOfAccountsId chart, DateOnly asOf, CancellationToken ct = default)
    {
        ObservedReceivableAsOfs.Add(asOf);
        return ValueTask.FromResult<IReadOnlyList<ReportOpenItem>>(Receivables.Where(x => x.TenantId == tenant && x.ChartId == chart).Select(x => x with { Bucket = Classify(asOf, x.DueDate) }).ToList());
    }

    public ValueTask<IReadOnlyList<ReportOpenItem>> GetOpenPayablesAsync(TenantId tenant, ChartOfAccountsId chart, DateOnly asOf, CancellationToken ct = default)
    {
        ObservedPayableAsOfs.Add(asOf);
        return ValueTask.FromResult<IReadOnlyList<ReportOpenItem>>(Payables.Where(x => x.TenantId == tenant && x.ChartId == chart).Select(x => x with { Bucket = Classify(asOf, x.DueDate) }).ToList());
    }

    private static AgingBucket Classify(DateOnly asOf, DateOnly dueDate)
    {
        var days = asOf.DayNumber - dueDate.DayNumber;
        return days <= 0 ? AgingBucket.Current
            : days <= 30 ? AgingBucket.Days0To30
            : days <= 60 ? AgingBucket.Days31To60
            : days <= 90 ? AgingBucket.Days61To90
            : AgingBucket.Days90Plus;
    }
    public ValueTask<IReadOnlyDictionary<PartyId, ReportParty>> GetPartiesAsync(IReadOnlyCollection<PartyId> ids, CancellationToken ct = default) => ValueTask.FromResult<IReadOnlyDictionary<PartyId, ReportParty>>(Parties.Where(x => ids.Contains(x.Id)).ToDictionary(x => x.Id));
    public ValueTask<IReadOnlyList<ReportLease>> GetLeasesAsync(TenantId tenant, CancellationToken ct = default) => ValueTask.FromResult<IReadOnlyList<ReportLease>>(Leases.Where(x => x.TenantId == tenant).ToList());
}
