using Harborline.Foundation.Assets.Common;

namespace Harborline.Blocks.Reports.Inputs;

/// <summary>
/// Harborline-owned read boundary for report computation. Implementations adapt storage
/// models into these immutable neutral rows; no financial-package type crosses this seam.
/// </summary>
public interface IReportQuerySource
{
    ValueTask<ReportChart?> GetChartAsync(ChartOfAccountsId chartId, CancellationToken cancellationToken = default);
    ValueTask<ReportFiscalPeriod?> GetFiscalPeriodAsync(FiscalPeriodId periodId, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<ReportAccount>> GetAccountsAsync(ChartOfAccountsId chartId, bool includeInactive, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<ReportJournalEntry>> GetPostedJournalEntriesAsync(TenantId tenantId, ChartOfAccountsId chartId, DateOnly? from, DateOnly through, string snapshotMarker, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<ReportOpenItem>> GetOpenReceivablesAsync(TenantId tenantId, ChartOfAccountsId chartId, DateOnly asOf, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<ReportOpenItem>> GetOpenPayablesAsync(TenantId tenantId, ChartOfAccountsId chartId, DateOnly asOf, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyDictionary<PartyId, ReportParty>> GetPartiesAsync(IReadOnlyCollection<PartyId> partyIds, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<ReportLease>> GetLeasesAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

public readonly record struct ChartOfAccountsId(Guid Value) { public static ChartOfAccountsId NewId() => new(Guid.NewGuid()); }
public readonly record struct FiscalPeriodId(Guid Value) { public static FiscalPeriodId NewId() => new(Guid.NewGuid()); }
public readonly record struct GLAccountId(Guid Value) { public static GLAccountId NewId() => new(Guid.NewGuid()); }
public readonly record struct PartyId(string Value);
public readonly record struct LeaseId(Guid Value) { public static LeaseId NewId() => new(Guid.NewGuid()); }

public enum GLAccountType { Asset, Liability, Equity, Revenue, Expense }
public enum NormalBalance { Debit, Credit }
public enum FiscalPeriodStatus { Open, SoftClosed, Locked }
public enum AgingBucket { Current, Days0To30, Days31To60, Days61To90, Days90Plus }
public enum LeasePhase { Draft, Executed, Active, Terminated, Cancelled }

public sealed record ReportChart(ChartOfAccountsId Id, string Name, string CurrencyCode, bool IsActive);
public sealed record ReportFiscalPeriod(FiscalPeriodId Id, ChartOfAccountsId ChartId, string Label, DateOnly StartDate, DateOnly EndDate, FiscalPeriodStatus Status);
public sealed record ReportAccount(GLAccountId Id, ChartOfAccountsId ChartId, string Code, string Name, GLAccountType Type, NormalBalance? NormalBalance, bool IsActive);
public sealed record ReportJournalLine(GLAccountId AccountId, decimal Debit, decimal Credit, string? PropertyId);
public sealed record ReportJournalEntry(string Id, TenantId TenantId, ChartOfAccountsId ChartId, DateOnly EntryDate, IReadOnlyList<ReportJournalLine> Lines);
public sealed record ReportOpenItem(string Id, TenantId TenantId, ChartOfAccountsId ChartId, PartyId PartyId, string? PropertyId, DateOnly DueDate, decimal OpenBalance, AgingBucket Bucket);
public sealed record ReportParty(PartyId Id, string DisplayName);
public sealed record ReportLease(LeaseId Id, TenantId TenantId, string PropertyKey, string UnitLabel, IReadOnlyList<PartyId> TenantPartyIds, DateOnly StartDate, DateOnly EndDate, LeasePhase Phase, decimal MonthlyRent);
