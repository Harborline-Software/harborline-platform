using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Blocks.Reports.Exceptions;

namespace Harborline.Blocks.Reports.Cartridges.BalanceSheet;

/// <summary>
/// R5 — Balance Sheet cartridge. Computes asset, liability, and equity
/// sections from the GL as of a given date and verifies the accounting
/// equation (Assets = Liabilities + Equity).
/// </summary>
/// <remarks>
/// <para>
/// <b>Data source.</b> Balances are read via
/// <c>IGeneralLedgerReadModel.GetAccountBalancesAsOfAsync</c>
/// (signed, posted-only, as-of semantics). The cartridge filters
/// accounts by type (Asset / Liability / Equity), ignoring Revenue and
/// Expense because those are income-statement accounts whose period
/// activity flows through Retained Earnings (an equity account) at
/// period close.
/// </para>
/// <para>
/// <b>Sign convention.</b>
/// <c>IGeneralLedgerReadModel</c> returns raw balances as
/// debit − credit. Asset accounts are debit-normal (positive raw →
/// positive display). Liability and Equity accounts are credit-normal
/// (negative raw → positive display amount via negation).
/// </para>
/// <para>
/// <b>Accounting equation check.</b>
/// <c>Assets = Liabilities + Equity</c>. When the check fails a
/// warning is emitted; the cartridge does NOT throw — it is the
/// caller's responsibility to act on the warning.
/// </para>
/// <para>
/// <b>Tenant isolation.</b> Per D4-C precedent (Trial Balance): the
/// caller (UI / Bridge endpoint) is responsible for resolving
/// <c>ChartId → LegalEntity → TenantId</c> before calling the
/// cartridge.
/// </para>
/// <para>
/// <b>Provisionality.</b> When bound to a
/// <c>FiscalPeriod</c>, the cartridge reports
/// <c>IsProvisional</c> = <c>true</c> for
/// <see cref="FiscalPeriodStatus.Open"/> and
/// <see cref="FiscalPeriodStatus.SoftClosed"/>. Explicit
/// <c>AsOfDate</c> always reports <c>IsProvisional</c> =
/// <c>false</c>.
/// </para>
/// </remarks>
public sealed class BalanceSheetCartridge
    : IReportCartridge<BalanceSheetParameters, BalanceSheetResult>
{
    private readonly IReportQuerySource _source;

    /// <summary>Construct bound to the four upstream cluster surfaces.</summary>
    public BalanceSheetCartridge(IReportQuerySource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <inheritdoc />
    public ReportKind Kind => ReportKind.BalanceSheet;

    /// <inheritdoc />
    public async Task<BalanceSheetResult> ExecuteAsync(
        ReportExecutionContext context,
        BalanceSheetParameters parameters,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // 1. Parameter validation — exactly one of (FiscalPeriodId, AsOfDate).
        if (parameters.FiscalPeriodId is null && parameters.AsOfDate is null)
            throw new ReportParameterValidationException(
                nameof(parameters),
                "BalanceSheet requires either FiscalPeriodId or AsOfDate.");
        if (parameters.FiscalPeriodId is not null && parameters.AsOfDate is not null)
            throw new ReportParameterValidationException(
                nameof(parameters),
                "BalanceSheet accepts FiscalPeriodId OR AsOfDate, not both.");

        // 2. Chart existence.
        var chart = await _source.GetChartAsync(parameters.ChartId, ct).ConfigureAwait(false);
        if (chart is null)
            throw new ReportParameterValidationException(
                nameof(parameters.ChartId),
                $"ChartId {parameters.ChartId} not found.");

        // 3. Resolve as-of date + provisionality.
        var warnings = new List<string>();
        var isProvisional = false;
        DateOnly asOf;
        if (parameters.FiscalPeriodId is not null)
        {
            var period = await _source.GetFiscalPeriodAsync(parameters.FiscalPeriodId.Value, ct).ConfigureAwait(false);
            if (period is null)
                throw new ReportParameterValidationException(
                    nameof(parameters.FiscalPeriodId),
                    $"FiscalPeriodId {parameters.FiscalPeriodId.Value} not found.");
            if (period.ChartId != parameters.ChartId)
                throw new ReportParameterValidationException(
                    nameof(parameters.FiscalPeriodId),
                    $"FiscalPeriodId {parameters.FiscalPeriodId.Value} belongs to a different chart ({period.ChartId}) than the requested ChartId ({parameters.ChartId}).");
            asOf = period.EndDate;
            if (period.Status != FiscalPeriodStatus.Locked)
            {
                isProvisional = true;
                warnings.Add($"Period {period.Label} is {period.Status}; values may shift on close.");
            }
        }
        else
        {
            asOf = parameters.AsOfDate!.Value;
        }

        // 4. Enumerate accounts (active by default; inactive included when requested).
        var accounts = await _source.GetAccountsAsync(
            parameters.ChartId,
            includeInactive: parameters.IncludeInactiveAccounts,
            ct).ConfigureAwait(false);

        // 5. Read raw signed balances as of the resolved date.
        var entries = await _source.GetPostedJournalEntriesAsync(context.TenantId, parameters.ChartId, null, asOf, context.SnapshotMarker, ct).ConfigureAwait(false);
        var balances = entries.SelectMany(e => e.Lines).GroupBy(l => l.AccountId).ToDictionary(g => g.Key, g => g.Sum(l => l.Debit - l.Credit));

        // 6. Partition into sections, ordered by Code (ordinal) then Id (stable tie-break).
        var orderedAccounts = accounts
            .OrderBy(a => a.Code, StringComparer.Ordinal)
            .ThenBy(a => a.Id.ToString(), StringComparer.Ordinal);

        var assetLines      = new List<BalanceSheetLine>();
        var liabilityLines  = new List<BalanceSheetLine>();
        var equityLines     = new List<BalanceSheetLine>();
        decimal totalAssets = 0m, totalLiabilities = 0m, totalEquity = 0m;

        foreach (var account in orderedAccounts)
        {
            if (account.Type is not (GLAccountType.Asset or GLAccountType.Liability or GLAccountType.Equity))
                continue;   // Revenue / Expense are income-statement accounts; exclude from BS.

            balances.TryGetValue(account.Id, out var raw);
            var displayAmount = ToDisplayAmount(account.Type, raw);

            if (displayAmount == 0m && !parameters.IncludeZeroBalanceAccounts) continue;

            var line = new BalanceSheetLine(
                AccountId:   account.Id,
                AccountCode: account.Code,
                AccountName: account.Name,
                AccountType: account.Type,
                Amount:      displayAmount);

            switch (account.Type)
            {
                case GLAccountType.Asset:
                    assetLines.Add(line);
                    totalAssets += displayAmount;
                    break;
                case GLAccountType.Liability:
                    liabilityLines.Add(line);
                    totalLiabilities += displayAmount;
                    break;
                case GLAccountType.Equity:
                    equityLines.Add(line);
                    totalEquity += displayAmount;
                    break;
            }
        }

        // 7. Accounting equation check: Assets = Liabilities + Equity.
        var isBalanced = totalAssets == totalLiabilities + totalEquity;
        if (!isBalanced)
            warnings.Add(
                $"Accounting equation unbalanced: Assets {totalAssets:N2} != " +
                $"Liabilities {totalLiabilities:N2} + Equity {totalEquity:N2}.");

        return new BalanceSheetResult(
            ChartId:         parameters.ChartId,
            AsOf:            asOf,
            PeriodId:        parameters.FiscalPeriodId,
            Assets:          assetLines,
            Liabilities:     liabilityLines,
            Equity:          equityLines,
            TotalAssets:     totalAssets,
            TotalLiabilities: totalLiabilities,
            TotalEquity:     totalEquity,
            IsBalanced:      isBalanced,
            IsProvisional:   isProvisional,
            Warnings:        warnings);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Convert a raw signed GL balance to the display amount shown in
    /// the balance sheet section.
    ///
    /// Raw semantics: debit positive, credit negative (per IGeneralLedgerReadModel).
    ///
    /// Assets are debit-normal: positive raw → positive display.
    /// Liabilities and Equity are credit-normal: negative raw → positive display
    /// (negate to get a human-readable value that increases with normal activity).
    /// </summary>
    private static decimal ToDisplayAmount(GLAccountType type, decimal raw) => type switch
    {
        GLAccountType.Asset => raw,
        GLAccountType.Liability or GLAccountType.Equity => -raw,
        _ => 0m,
    };
}
