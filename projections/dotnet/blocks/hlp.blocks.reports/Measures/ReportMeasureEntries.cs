using System.Globalization;
using Harborline.Blocks.Aggregates;
using Harborline.Blocks.MeasureCatalogue;
using Harborline.Blocks.Reports.Cartridges.ApAgingSummary;
using Harborline.Blocks.Reports.Cartridges.ArAgingSummary;
using Harborline.Blocks.Reports.Cartridges.BalanceSheet;
using Harborline.Blocks.Reports.Cartridges.ProfitAndLoss;
using Harborline.Blocks.Reports.Cartridges.ProfitAndLossByProperty;
using Harborline.Blocks.Reports.Cartridges.RentRoll;
using Harborline.Blocks.Reports.Cartridges.TrialBalance;
using Harborline.Blocks.Reports.Inputs;

namespace Harborline.Blocks.Reports.Measures;

/// <summary>
/// The seven shipped report computations, promoted into the shared catalogue as bound entries.
/// Each entry runs the existing cartridge unchanged over the caller's basis, behind the catalogue's
/// bound Access filter, and projects its figures into the catalogue's typed cells. No math is
/// rewritten here and Reports keeps its layout, basis and issuance integration.
/// </summary>
public static class ReportMeasureEntries
{
    /// <summary>The Access operation every report entry binds its row filter with.</summary>
    public const string Operation = "reports:read";

    /// <summary>The Access record kind every report row is checked as.</summary>
    public const string RecordKind = "report.row";

    /// <summary>The address of the trial balance.</summary>
    public static MeasureRef TrialBalance { get; } = new("finance.trial-balance");
    /// <summary>The address of the balance sheet.</summary>
    public static MeasureRef BalanceSheet { get; } = new("finance.balance-sheet");
    /// <summary>The address of the entity profit and loss.</summary>
    public static MeasureRef ProfitAndLoss { get; } = new("finance.profit-and-loss");
    /// <summary>The address of the profit and loss by property.</summary>
    public static MeasureRef ProfitAndLossByProperty { get; } = new("finance.profit-and-loss-by-property");
    /// <summary>The address of the receivables aging summary.</summary>
    public static MeasureRef ReceivablesAging { get; } = new("receivables.aging-summary");
    /// <summary>The address of the payables aging summary.</summary>
    public static MeasureRef PayablesAging { get; } = new("payables.aging-summary");
    /// <summary>The address of the rent roll.</summary>
    public static MeasureRef RentRoll { get; } = new("occupancy.rent-roll");

    // The caller supplies the clock: every entry takes its as-of date, and a period measure its
    // period end, from the evaluation instant rather than from a parameter that could disagree
    // with it. Only a window's opening edge is authored.
    private static readonly string[] OnlyChart = ["chart"];
    private static readonly string[] ChartAndStart = ["chart", "period-start"];

    /// <summary>Every report entry, for registration in one catalogue alongside declared entries.</summary>
    /// <returns>Exactly the seven shipped computations.</returns>
    public static IReadOnlyList<IMeasureEntry> All() =>
    [
        new ReportMeasureEntry(TrialBalance, OnlyChart, RunTrialBalanceAsync),
        new ReportMeasureEntry(BalanceSheet, OnlyChart, RunBalanceSheetAsync),
        new ReportMeasureEntry(ProfitAndLoss, ChartAndStart, RunProfitAndLossAsync),
        new ReportMeasureEntry(ProfitAndLossByProperty, ChartAndStart, RunProfitAndLossByPropertyAsync),
        new ReportMeasureEntry(ReceivablesAging, OnlyChart, RunReceivablesAgingAsync),
        new ReportMeasureEntry(PayablesAging, OnlyChart, RunPayablesAgingAsync),
        new ReportMeasureEntry(RentRoll, OnlyChart, RunRentRollAsync),
    ];

    private static async Task<IReadOnlyList<AggregateGroup>> RunTrialBalanceAsync(
        IReportQuerySource source, ReportExecutionContext context, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var result = await new TrialBalanceCartridge(source).ExecuteAsync(context,
            new TrialBalanceParameters { ChartId = Chart(parameters), AsOfDate = Instant(context) },
            cancellationToken).ConfigureAwait(false);
        var groups = result.Rows
            .Select(row => Detail([MeasureCells.Text("account", row.AccountCode)],
                [MeasureCells.Money("debit", row.DebitBalance), MeasureCells.Money("credit", row.CreditBalance)]))
            .ToList();
        groups.Add(Total([MeasureCells.Money("debit", result.TotalDebit), MeasureCells.Money("credit", result.TotalCredit)]));
        return groups;
    }

    private static async Task<IReadOnlyList<AggregateGroup>> RunBalanceSheetAsync(
        IReportQuerySource source, ReportExecutionContext context, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var result = await new BalanceSheetCartridge(source).ExecuteAsync(context,
            new BalanceSheetParameters { ChartId = Chart(parameters), AsOfDate = Instant(context) },
            cancellationToken).ConfigureAwait(false);
        var groups = new List<AggregateGroup>();
        foreach (var (section, lines) in new[] { ("asset", result.Assets), ("liability", result.Liabilities), ("equity", result.Equity) })
        {
            groups.AddRange(lines.Select(line => Detail(
                [MeasureCells.Text("section", section), MeasureCells.Text("account", line.AccountCode)],
                [MeasureCells.Money("amount", line.Amount)])));
        }
        groups.Add(Total([
            MeasureCells.Money("total_assets", result.TotalAssets),
            MeasureCells.Money("total_liabilities", result.TotalLiabilities),
            MeasureCells.Money("total_equity", result.TotalEquity)]));
        return groups;
    }

    private static async Task<IReadOnlyList<AggregateGroup>> RunProfitAndLossAsync(
        IReportQuerySource source, ReportExecutionContext context, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var result = await new ProfitAndLossCartridge(source).ExecuteAsync(context,
            new ProfitAndLossParameters
            {
                ChartId = Chart(parameters),
                PeriodStart = Date(parameters, "period-start"),
                PeriodEnd = Instant(context),
            },
            cancellationToken).ConfigureAwait(false);
        var groups = new List<AggregateGroup>();
        foreach (var (section, lines) in new[] { ("revenue", result.RevenueLines), ("expense", result.ExpenseLines) })
        {
            groups.AddRange(lines.Select(line => Detail(
                [MeasureCells.Text("section", section), MeasureCells.Text("account", line.AccountCode)],
                [MeasureCells.Money("amount", line.Amount)])));
        }
        groups.Add(Total([
            MeasureCells.Money("total_revenue", result.TotalRevenue),
            MeasureCells.Money("total_expenses", result.TotalExpenses),
            MeasureCells.Money("net_profit", result.NetProfit)]));
        return groups;
    }

    private static async Task<IReadOnlyList<AggregateGroup>> RunProfitAndLossByPropertyAsync(
        IReportQuerySource source, ReportExecutionContext context, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var result = await new ProfitAndLossByPropertyCartridge(source).ExecuteAsync(context,
            new ProfitAndLossByPropertyParameters
            {
                ChartId = Chart(parameters),
                PeriodStart = Date(parameters, "period-start"),
                PeriodEnd = Instant(context),
            },
            cancellationToken).ConfigureAwait(false);
        var groups = result.ByProperty
            .Select(row => Detail([MeasureCells.Text("property", row.PropertyKey)],
            [
                MeasureCells.Money("total_revenue", row.TotalRevenue),
                MeasureCells.Money("total_expenses", row.TotalExpenses),
                MeasureCells.Money("net_income", row.NetIncome),
            ]))
            .ToList();
        groups.Add(Total([
            MeasureCells.Money("total_revenue", result.Totals.TotalRevenue),
            MeasureCells.Money("total_expenses", result.Totals.TotalExpenses),
            MeasureCells.Money("net_income", result.Totals.NetIncome)]));
        return groups;
    }

    private static async Task<IReadOnlyList<AggregateGroup>> RunReceivablesAgingAsync(
        IReportQuerySource source, ReportExecutionContext context, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var result = await new ArAgingSummaryCartridge(source).ExecuteAsync(context,
            new ArAgingSummaryParameters { ChartId = Chart(parameters), AsOfDate = Instant(context) },
            cancellationToken).ConfigureAwait(false);
        var groups = result.ByCustomer.Select(row => Detail([MeasureCells.Text("party", row.GroupKey)], Aging(
            row.Current, row.Days0To30, row.Days31To60, row.Days61To90, row.Days90Plus, row.TotalOpen))).ToList();
        var totals = result.Totals;
        groups.Add(Total(Aging(totals.Current, totals.Days0To30, totals.Days31To60, totals.Days61To90, totals.Days90Plus, totals.TotalOpen)));
        return groups;
    }

    private static async Task<IReadOnlyList<AggregateGroup>> RunPayablesAgingAsync(
        IReportQuerySource source, ReportExecutionContext context, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var result = await new ApAgingSummaryCartridge(source).ExecuteAsync(context,
            new ApAgingSummaryParameters { ChartId = Chart(parameters), AsOfDate = Instant(context) },
            cancellationToken).ConfigureAwait(false);
        var groups = result.ByVendor.Select(row => Detail([MeasureCells.Text("party", row.GroupKey)], Aging(
            row.Current, row.Days0To30, row.Days31To60, row.Days61To90, row.Days90Plus, row.TotalOpen))).ToList();
        var totals = result.Totals;
        groups.Add(Total(Aging(totals.Current, totals.Days0To30, totals.Days31To60, totals.Days61To90, totals.Days90Plus, totals.TotalOpen)));
        return groups;
    }

    private static async Task<IReadOnlyList<AggregateGroup>> RunRentRollAsync(
        IReportQuerySource source, ReportExecutionContext context, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var result = await new RentRollCartridge(source).ExecuteAsync(context,
            new RentRollParameters { ChartId = Chart(parameters), AsOfDate = Instant(context) },
            cancellationToken).ConfigureAwait(false);
        var groups = result.Properties
            .Select(block => Detail([MeasureCells.Text("property", block.PropertyKey)],
            [
                MeasureCells.Count("total_units", block.Summary.TotalUnits),
                MeasureCells.Count("occupied_units", block.Summary.OccupiedUnits),
                // A rate over no units is not a zero rate; it is absent, and stays absent.
                block.Summary.TotalUnits == 0
                    ? MeasureCells.Absent("occupancy_rate")
                    : MeasureCells.Money("occupancy_rate", block.Summary.OccupancyRate),
                MeasureCells.Money("monthly_rent_total", block.Summary.MonthlyRentTotal),
                MeasureCells.Money("monthly_rent_total_if_fully_leased", block.Summary.MonthlyRentTotalIfFullyLeased),
                MeasureCells.Money("open_balance_total", block.Summary.OpenBalanceTotal),
            ]))
            .ToList();
        var portfolio = result.Portfolio;
        groups.Add(Total(
        [
            MeasureCells.Count("total_units", portfolio.TotalUnits),
            MeasureCells.Count("occupied_units", portfolio.OccupiedUnits),
            portfolio.TotalUnits == 0
                ? MeasureCells.Absent("occupancy_rate")
                : MeasureCells.Money("occupancy_rate", portfolio.OccupancyRate),
            MeasureCells.Money("monthly_rent_total", portfolio.MonthlyRentTotal),
            // The portfolio summary does not carry a fully-leased figure. The measure exists and the
            // number does not, which is unavailable and never zero.
            MeasureCells.Unavailable("monthly_rent_total_if_fully_leased"),
            MeasureCells.Money("open_balance_total", portfolio.OpenBalanceTotal),
        ]));
        return groups;
    }

    private static AggregateCell[] Aging(decimal current, decimal days0To30, decimal days31To60, decimal days61To90, decimal days90Plus, decimal totalOpen) =>
    [
        MeasureCells.Money("current", current),
        MeasureCells.Money("days_0_30", days0To30),
        MeasureCells.Money("days_31_60", days31To60),
        MeasureCells.Money("days_61_90", days61To90),
        MeasureCells.Money("days_90_plus", days90Plus),
        MeasureCells.Money("total_open", totalOpen),
    ];

    private static AggregateGroup Detail(IReadOnlyList<AggregateKey> keys, IReadOnlyList<AggregateCell> cells) =>
        new(AggregateGroupKind.Detail, keys.Count, keys, cells);

    private static AggregateGroup Total(IReadOnlyList<AggregateCell> cells) =>
        new(AggregateGroupKind.GrandTotal, 0, Array.Empty<AggregateKey>(), cells);

    private static ChartOfAccountsId Chart(IReadOnlyDictionary<string, string> parameters)
    {
        if (!Guid.TryParse(parameters["chart"], CultureInfo.InvariantCulture, out var value))
        {
            throw new MeasureException(MeasureCodes.ParameterInvalid, "/parameters/chart", "Parameter 'chart' must be a GUID.");
        }
        return new ChartOfAccountsId(value);
    }

    // The caller's instant, as the cartridges' as-of date.
    private static DateOnly Instant(ReportExecutionContext context) => DateOnly.FromDateTime(context.AsOfUtc.UtcDateTime);

    private static DateOnly Date(IReadOnlyDictionary<string, string> parameters, string name)
    {
        if (!DateOnly.TryParseExact(parameters[name], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            throw new MeasureException(MeasureCodes.ParameterInvalid, $"/parameters/{name}", $"Parameter '{name}' must be a yyyy-MM-dd date.");
        }
        return value;
    }
}

/// <summary>Typed cell and key construction shared by the bound report entries.</summary>
internal static class MeasureCells
{
    internal static AggregateKey Text(string key, string value) => new(key, AggregateValueType.String, value);

    internal static AggregateCell Count(string key, int value) =>
        new(key, AggregateValueType.Integer, AggregateCellState.Value, (long)value);

    internal static AggregateCell Money(string key, decimal value) =>
        new(key, AggregateValueType.Decimal, AggregateCellState.Value,
            CanonicalDecimal.Parse(value.ToString(CultureInfo.InvariantCulture)).ToString());

    internal static AggregateCell Absent(string key) => new(key, AggregateValueType.Decimal, AggregateCellState.Null, null);

    internal static AggregateCell Unavailable(string key) => new(key, AggregateValueType.Decimal, AggregateCellState.Unavailable, null);
}

/// <summary>
/// One bound entry. It reads the caller's basis through the catalogue's Access predicate, runs the
/// existing cartridge over that narrowed source, and returns typed cells. The kind of entry is not
/// visible to the caller: the address, descriptor and result look the same as a declared entry's.
/// </summary>
internal sealed class ReportMeasureEntry(
    MeasureRef reference,
    IReadOnlyList<string> parameterNames,
    Func<IReportQuerySource, ReportExecutionContext, IReadOnlyDictionary<string, string>, CancellationToken, Task<IReadOnlyList<AggregateGroup>>> run)
    : IMeasureEntry
{
    public MeasureRef Reference { get; } = reference;

    public string Operation => ReportMeasureEntries.Operation;

    public string RecordKind => ReportMeasureEntries.RecordKind;

    public AggregateFilter? AuthoredFilter => null;

    public IReadOnlyList<string> ParameterNames { get; } = parameterNames;

    public async ValueTask<MeasureResult> EvaluateAsync(MeasureEvaluation evaluation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        if (evaluation.Basis is not ReportMeasureBasis basis)
        {
            throw new MeasureException(MeasureCodes.BasisUnsupported, $"/measures/{Reference.Value}/rows",
                "This measure reads a report basis the caller supplies.");
        }
        var source = new AccessFilteredReportQuerySource(basis.Source, evaluation.Filter, evaluation.Tenant, RecordKind);
        var context = new ReportExecutionContext(basis.Tenant, basis.Token, evaluation.At, basis.RequestedBy);
        var groups = await run(source, context, evaluation.Parameters, cancellationToken).ConfigureAwait(false);
        return new MeasureResult(Reference, evaluation.BasisToken, groups);
    }
}
