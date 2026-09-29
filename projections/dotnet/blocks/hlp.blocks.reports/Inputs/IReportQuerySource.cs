using Harborline.Foundation.Assets.Common;

namespace Harborline.Blocks.Reports.Inputs;

/// <summary>
/// Harborline-owned read boundary for report computation. Implementations adapt storage
/// models into these immutable neutral rows; no financial-package type crosses this seam.
/// </summary>
public interface IReportQuerySource
{
    /// <summary>Loads the requested chart, or <c>null</c> when the chart is absent.</summary>
    ValueTask<ReportChart?> GetChartAsync(ChartOfAccountsId chartId, CancellationToken cancellationToken = default);
    /// <summary>Loads the requested fiscal period, or <c>null</c> when the period is absent.</summary>
    ValueTask<ReportFiscalPeriod?> GetFiscalPeriodAsync(FiscalPeriodId periodId, CancellationToken cancellationToken = default);
    /// <summary>Returns accounts in the chart; inactive accounts are included only when requested.</summary>
    ValueTask<IReadOnlyList<ReportAccount>> GetAccountsAsync(ChartOfAccountsId chartId, bool includeInactive, CancellationToken cancellationToken = default);
    /// <summary>Returns posted entries in the inclusive date window, constrained by the supplied snapshot marker.</summary>
    ValueTask<IReadOnlyList<ReportJournalEntry>> GetPostedJournalEntriesAsync(TenantId tenantId, ChartOfAccountsId chartId, DateOnly? from, DateOnly through, string snapshotMarker, CancellationToken cancellationToken = default);
    /// <summary>Returns receivables still open at the supplied as-of date.</summary>
    ValueTask<IReadOnlyList<ReportOpenItem>> GetOpenReceivablesAsync(TenantId tenantId, ChartOfAccountsId chartId, DateOnly asOf, CancellationToken cancellationToken = default);
    /// <summary>Returns payables still open at the supplied as-of date.</summary>
    ValueTask<IReadOnlyList<ReportOpenItem>> GetOpenPayablesAsync(TenantId tenantId, ChartOfAccountsId chartId, DateOnly asOf, CancellationToken cancellationToken = default);
    /// <summary>Resolves the requested parties; missing parties are omitted from the returned map.</summary>
    ValueTask<IReadOnlyDictionary<PartyId, ReportParty>> GetPartiesAsync(IReadOnlyCollection<PartyId> partyIds, CancellationToken cancellationToken = default);
    /// <summary>Returns leases belonging to the tenant, including their current lifecycle phase.</summary>
    ValueTask<IReadOnlyList<ReportLease>> GetLeasesAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Identifies a chart of accounts; <see cref="NewId"/> creates a new identifier.</summary>
/// <param name="Value">The chart's globally unique identifier.</param>
public readonly record struct ChartOfAccountsId(Guid Value)
{
    /// <summary>Creates a new globally unique chart identifier.</summary>
    public static ChartOfAccountsId NewId() => new(Guid.NewGuid());
}
/// <summary>Identifies a fiscal period; <see cref="NewId"/> creates a new identifier.</summary>
/// <param name="Value">The period's globally unique identifier.</param>
public readonly record struct FiscalPeriodId(Guid Value)
{
    /// <summary>Creates a new globally unique fiscal-period identifier.</summary>
    public static FiscalPeriodId NewId() => new(Guid.NewGuid());
}
/// <summary>Identifies a general-ledger account; <see cref="NewId"/> creates a new identifier.</summary>
/// <param name="Value">The account's globally unique identifier.</param>
public readonly record struct GLAccountId(Guid Value)
{
    /// <summary>Creates a new globally unique general-ledger account identifier.</summary>
    public static GLAccountId NewId() => new(Guid.NewGuid());
}
/// <summary>Identifies a party using its stable external or domain key.</summary>
/// <param name="Value">The party key; it is preserved as supplied by the source.</param>
public readonly record struct PartyId(string Value);
/// <summary>Identifies a lease; <see cref="NewId"/> creates a new identifier.</summary>
/// <param name="Value">The lease's globally unique identifier.</param>
public readonly record struct LeaseId(Guid Value)
{
    /// <summary>Creates a new globally unique lease identifier.</summary>
    public static LeaseId NewId() => new(Guid.NewGuid());
}

/// <summary>Classifies a general-ledger account for reporting and normal-balance derivation.</summary>
public enum GLAccountType
{
    /// <summary>Resources controlled by the entity.</summary>
    Asset,
    /// <summary>Obligations owed by the entity.</summary>
    Liability,
    /// <summary>The owners' residual interest.</summary>
    Equity,
    /// <summary>Income earned by the entity.</summary>
    Revenue,
    /// <summary>Costs incurred by the entity.</summary>
    Expense
}
/// <summary>Indicates the side on which an account normally carries a balance.</summary>
public enum NormalBalance
{
    /// <summary>The account normally carries a debit balance.</summary>
    Debit,
    /// <summary>The account normally carries a credit balance.</summary>
    Credit
}
/// <summary>Lifecycle state used to determine whether a fiscal period is provisional.</summary>
public enum FiscalPeriodStatus
{
    /// <summary>The period remains open and report values may change.</summary>
    Open,
    /// <summary>The period is soft-closed but remains provisional.</summary>
    SoftClosed,
    /// <summary>The period is locked and report values are final for its source.</summary>
    Locked
}
/// <summary>Age classification for an open receivable or payable as of a report date.</summary>
public enum AgingBucket
{
    /// <summary>The item is not past due.</summary>
    Current,
    /// <summary>The item is 0 through 30 days past due.</summary>
    Days0To30,
    /// <summary>The item is 31 through 60 days past due.</summary>
    Days31To60,
    /// <summary>The item is 61 through 90 days past due.</summary>
    Days61To90,
    /// <summary>The item is more than 90 days past due.</summary>
    Days90Plus
}
/// <summary>Lifecycle phase of a lease used by occupancy reports.</summary>
public enum LeasePhase
{
    /// <summary>The lease is being prepared and is not yet executed.</summary>
    Draft,
    /// <summary>The lease has been executed but is not yet active.</summary>
    Executed,
    /// <summary>The lease is currently in force.</summary>
    Active,
    /// <summary>The lease ended according to its terms.</summary>
    Terminated,
    /// <summary>The lease was cancelled before normal completion.</summary>
    Cancelled
}

/// <summary>Neutral chart metadata consumed by report cartridges.</summary>
/// <param name="Id">The chart identifier.</param><param name="Name">The display name.</param><param name="CurrencyCode">The reporting currency code.</param><param name="IsActive">Whether the chart can be used for new reports.</param>
public sealed record ReportChart(ChartOfAccountsId Id, string Name, string CurrencyCode, bool IsActive);
/// <summary>Neutral fiscal-period metadata, including its reporting boundaries and close state.</summary>
/// <param name="Id">The period identifier.</param><param name="ChartId">The chart to which the period belongs.</param><param name="Label">The human-readable period label.</param><param name="StartDate">The inclusive start date.</param><param name="EndDate">The inclusive end date.</param><param name="Status">The close state used for provisionality.</param>
public sealed record ReportFiscalPeriod(FiscalPeriodId Id, ChartOfAccountsId ChartId, string Label, DateOnly StartDate, DateOnly EndDate, FiscalPeriodStatus Status);
/// <summary>Neutral account metadata used to compose report rows.</summary>
/// <param name="Id">The account identifier.</param><param name="ChartId">The owning chart identifier.</param><param name="Code">The stable display or ordering code.</param><param name="Name">The account name.</param><param name="Type">The account classification.</param><param name="NormalBalance">An explicit normal side, or <c>null</c> when the type supplies it.</param><param name="IsActive">Whether the account is active.</param>
public sealed record ReportAccount(GLAccountId Id, ChartOfAccountsId ChartId, string Code, string Name, GLAccountType Type, NormalBalance? NormalBalance, bool IsActive);
/// <summary>A posted journal line with debit and credit amounts in the report currency.</summary>
/// <param name="AccountId">The account affected.</param><param name="Debit">The debit amount.</param><param name="Credit">The credit amount.</param><param name="PropertyId">An optional property key for property reports.</param>
public sealed record ReportJournalLine(GLAccountId AccountId, decimal Debit, decimal Credit, string? PropertyId);
/// <summary>A posted journal entry and its balanced or unbalanced report lines.</summary>
/// <param name="Id">The source entry identifier.</param><param name="TenantId">The tenant that owns the entry.</param><param name="ChartId">The chart used by the entry.</param><param name="EntryDate">The accounting date.</param><param name="Lines">The entry's journal lines.</param>
public sealed record ReportJournalEntry(string Id, TenantId TenantId, ChartOfAccountsId ChartId, DateOnly EntryDate, IReadOnlyList<ReportJournalLine> Lines);
/// <summary>An unpaid receivable or payable classified relative to an as-of date.</summary>
/// <param name="Id">The source item identifier.</param><param name="TenantId">The owning tenant.</param><param name="ChartId">The related chart.</param><param name="PartyId">The customer or vendor party.</param><param name="PropertyId">An optional property key.</param><param name="DueDate">The contractual due date.</param><param name="OpenBalance">The remaining balance in the report currency.</param><param name="Bucket">The age classification.</param>
public sealed record ReportOpenItem(string Id, TenantId TenantId, ChartOfAccountsId ChartId, PartyId PartyId, string? PropertyId, DateOnly DueDate, decimal OpenBalance, AgingBucket Bucket);
/// <summary>Neutral party identity used to label receivables, payables, and leases.</summary>
/// <param name="Id">The party identifier.</param><param name="DisplayName">The display label for the party.</param>
public sealed record ReportParty(PartyId Id, string DisplayName);
/// <summary>Neutral lease data used to produce rent-roll and occupancy reports.</summary>
/// <param name="Id">The lease identifier.</param><param name="TenantId">The owning tenant.</param><param name="PropertyKey">The property identifier.</param><param name="UnitLabel">The unit display label.</param><param name="TenantPartyIds">The parties associated with the lease.</param><param name="StartDate">The lease start date.</param><param name="EndDate">The lease end date.</param><param name="Phase">The lease lifecycle phase.</param><param name="MonthlyRent">The scheduled monthly rent.</param>
public sealed record ReportLease(LeaseId Id, TenantId TenantId, string PropertyKey, string UnitLabel, IReadOnlyList<PartyId> TenantPartyIds, DateOnly StartDate, DateOnly EndDate, LeasePhase Phase, decimal MonthlyRent);
