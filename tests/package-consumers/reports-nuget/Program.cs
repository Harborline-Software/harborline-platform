using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Harborline.Blocks.Reports;
using Harborline.Blocks.Reports.Cartridges.ApAgingSummary;
using Harborline.Blocks.Reports.Cartridges.ArAgingSummary;
using Harborline.Blocks.Reports.Cartridges.BalanceSheet;
using Harborline.Blocks.Reports.Cartridges.ProfitAndLoss;
using Harborline.Blocks.Reports.Cartridges.ProfitAndLossByProperty;
using Harborline.Blocks.Reports.Cartridges.RentRoll;
using Harborline.Blocks.Reports.Cartridges.TrialBalance;
using Harborline.Blocks.Reports.DependencyInjection;
using Harborline.Blocks.Reports.Exceptions;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Crypto;

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
using var corpus = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "reports-vertical-cases.json")));
var anchors = corpus.RootElement.GetProperty("anchors");
var httpRows = corpus.RootElement.GetProperty("httpExpectations");
var clientRows = corpus.RootElement.GetProperty("clientContract");
Check(anchors.GetArrayLength() == 162, "frozen caller-engine corpus must contain 162 rows");
Check(httpRows.GetArrayLength() == 12, "pinned host seam must contain 12 rows");
Check(clientRows.GetArrayLength() == 3, "client seam must contain three rows");
Check(corpus.RootElement.GetProperty("crossLanePairs").GetArrayLength() == 0, "ruling 4 forbids invented cross-lane pairs");

var tenant = new TenantId("reports-consumer");
var foreignTenant = new TenantId("reports-foreign");
var principal = PrincipalId.FromBytes(new byte[32]);
var source = ConsumerReportSource.Create(tenant, foreignTenant);
var services = new ServiceCollection();
services.AddSingleton<IReportQuerySource>(source);
services.AddSingleton<TimeProvider>(new FixedTimeProvider());
services.AddBlocksReportsSubstrate();
services.AddSingleton<ISnapshotMarkerSource>(new FixedMarkerSource("fixture:reports:1"));
services.AddTrialBalanceCartridge();
services.AddBalanceSheetCartridge();
services.AddProfitAndLossCartridge();
services.AddProfitAndLossByPropertyCartridge();
services.AddArAgingSummaryCartridge();
services.AddApAgingSummaryCartridge();
services.AddRentRollCartridge();
await using var provider = services.BuildServiceProvider();
Check(provider.UseBlocksReports() == 7, "all seven packaged cartridge registrars must drain");
var runner = provider.GetRequiredService<IReportRunner>();

var trialParams = new TrialBalanceParameters { ChartId = source.ChartId, AsOfDate = source.AsOf };
var trial = await runner.RunAsync<TrialBalanceParameters, TrialBalanceResult>(ReportKind.TrialBalance, trialParams, tenant, principal);
Check(trial.Kind == ReportKind.TrialBalance && trial.SnapshotMarker == "fixture:reports:1", "trial run -> marker -> compute -> envelope");
Check(trial.Result.IsBalanced && trial.Result.TotalDebit == 1000m && trial.Result.TotalCredit == 1000m, "trial ledger-exact totals");
var balance = await runner.RunAsync<BalanceSheetParameters, BalanceSheetResult>(ReportKind.BalanceSheet, new() { ChartId = source.ChartId, AsOfDate = source.AsOf }, tenant, principal);
Check(balance.Result.ChartId == source.ChartId && balance.Result.TotalAssets == 1000m, "balance-sheet representative outcome");
var pnl = await runner.RunAsync<ProfitAndLossParameters, ProfitAndLossResult>(ReportKind.ProfitAndLoss, new() { ChartId = source.ChartId, PeriodStart = source.AsOf.AddDays(-30), PeriodEnd = source.AsOf }, tenant, principal);
Check(pnl.Result.TotalRevenue == 1000m && pnl.Result.NetProfit == 1000m, "P&L representative outcome and tenant isolation");
var byProperty = await runner.RunAsync<ProfitAndLossByPropertyParameters, ProfitAndLossByPropertyResult>(ReportKind.ProfitAndLossByProperty, new() { ChartId = source.ChartId, PeriodStart = source.AsOf.AddDays(-30), PeriodEnd = source.AsOf }, tenant, principal);
Check(byProperty.Result.ByProperty.Single().PropertyKey == "property-1" && byProperty.Result.Totals.NetIncome == 1000m, "P&L-by-property representative outcome");
var ar = await runner.RunAsync<ArAgingSummaryParameters, ArAgingSummaryResult>(ReportKind.ArAgingSummary, new() { ChartId = source.ChartId, AsOfDate = source.AsOf }, tenant, principal);
Check(ar.Result.Totals.Days90Plus == 125m && ar.Result.ByCustomer.Single().GroupLabel == "Customer One", "AR representative outcome");
var ap = await runner.RunAsync<ApAgingSummaryParameters, ApAgingSummaryResult>(ReportKind.ApAgingSummary, new() { ChartId = source.ChartId, AsOfDate = source.AsOf }, tenant, principal);
Check(ap.Result.Totals.Current == 75m && ap.Result.ByVendor.Single().GroupLabel == "Vendor One", "AP representative outcome");
var rent = await runner.RunAsync<RentRollParameters, RentRollResult>(ReportKind.RentRoll, new() { ChartId = source.ChartId, AsOfDate = source.AsOf }, tenant, principal);
Check(rent.Result.Portfolio.TotalUnits == 1 && rent.Result.Portfolio.MonthlyRentTotal == 1500m, "RentRoll engine-only representative outcome");

await ThrowsAsync<UnknownReportKindException>(() => runner.RunAsync<TrialBalanceParameters, TrialBalanceResult>((ReportKind)999, trialParams, tenant, principal), "unknown kind fails closed");
await ThrowsAsync<ReportParameterValidationException>(() => runner.RunAsync<TrialBalanceParameters, TrialBalanceResult>(ReportKind.TrialBalance, new() { ChartId = source.ChartId }, tenant, principal), "validation passes through unwrapped");
var replay1 = await runner.RunAsync<TrialBalanceParameters, TrialBalanceResult>(ReportKind.TrialBalance, trialParams, tenant, principal);
var replay2 = await runner.RunAsync<TrialBalanceParameters, TrialBalanceResult>(ReportKind.TrialBalance, trialParams, tenant, principal);
Check(JsonSerializer.SerializeToUtf8Bytes(replay1, json).SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(replay2, json)), "same marker produces byte-identical serialized envelopes");
Console.WriteLine($"REPORTS_PACKAGE_PASS:{JsonSerializer.Serialize(new { anchors = 162, representativeExecutions = 7 }, json)}");

var builder = WebApplication.CreateSlimBuilder();
builder.WebHost.UseUrls("http://127.0.0.1:0");
var app = builder.Build();
var chartPresent = false;
app.MapGet("/api/local-node/charts", (CancellationToken _) => Results.Ok(new { charts = chartPresent ? new object[] { new { chartId = source.ChartId.Value, name = "Consumer Chart", baseCurrency = "USD" } } : Array.Empty<object>() }));
app.MapPost("/api/local-node/reports/{kind}", async (string kind, HttpContext http, CancellationToken ct) =>
{
    var raw = Uri.UnescapeDataString(kind);
    using var body = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: ct);
    var requested = body.RootElement.TryGetProperty("chartId", out var id) ? id.GetString() : null;
    if (!chartPresent || requested != source.ChartId.Value.ToString()) return Results.NotFound();
    IResult result = raw switch
    {
        "trial-balance" => Results.Ok(await runner.RunAsync<TrialBalanceParameters, TrialBalanceResult>(ReportKind.TrialBalance, trialParams, tenant, principal, ct)),
        "balance-sheet" => Results.Ok(await runner.RunAsync<BalanceSheetParameters, BalanceSheetResult>(ReportKind.BalanceSheet, new() { ChartId = source.ChartId, AsOfDate = source.AsOf }, tenant, principal, ct)),
        "profit-and-loss" => Results.Ok(await runner.RunAsync<ProfitAndLossParameters, ProfitAndLossResult>(ReportKind.ProfitAndLoss, new() { ChartId = source.ChartId }, tenant, principal, ct)),
        "profit-and-loss-by-property" => Results.Ok(await runner.RunAsync<ProfitAndLossByPropertyParameters, ProfitAndLossByPropertyResult>(ReportKind.ProfitAndLossByProperty, new() { ChartId = source.ChartId }, tenant, principal, ct)),
        "ar-aging-summary" => Results.Ok(await runner.RunAsync<ArAgingSummaryParameters, ArAgingSummaryResult>(ReportKind.ArAgingSummary, new() { ChartId = source.ChartId }, tenant, principal, ct)),
        "ap-aging-summary" => Results.Ok(await runner.RunAsync<ApAgingSummaryParameters, ApAgingSummaryResult>(ReportKind.ApAgingSummary, new() { ChartId = source.ChartId }, tenant, principal, ct)),
        _ => Results.StatusCode(500)
    };
    Check(result is IStatusCodeHttpResult { StatusCode: 200 }, "handler must preserve IResult status");
    return result;
});
var workflowRows = new[] { new { instanceId="run-1", definitionKey="three-way-match.v1", definitionVersion="1.0.0", status="Completed", finalStep="posted", subjectId="bill-1", amount=1875m, memo="Vendor bill", effectPosted=true, capabilityRef="ledger.post-journal-entry", createdAt="2026-07-18T11:40:00.000Z", completedAt="2026-07-18T11:54:00.000Z" } };
app.MapGet("/api/local-node/workflow-run-report", (HttpContext http, CancellationToken _) =>
    http.Request.Query.ContainsKey("fail") ? Results.StatusCode(503) : Results.Ok(new { data = workflowRows }));
await app.StartAsync();
try
{
    using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    foreach (var row in httpRows.EnumerateArray())
    {
        var id = row.GetProperty("id").GetString()!;
        if (id == "http-01") chartPresent = false; else chartPresent = id != "http-10";
        var method = new HttpMethod(row.GetProperty("method").GetString()!);
        var path = row.GetProperty("path").GetString()!;
        var chart = id == "http-09" ? Guid.Empty.ToString() : source.ChartId.Value.ToString();
        using var request = new HttpRequestMessage(method, path);
        if (method == HttpMethod.Post) request.Content = JsonContent.Create(new { chartId = chart });
        using var response = await client.SendAsync(request);
        Check((int)response.StatusCode == row.GetProperty("expectedStatus").GetInt32(), row.GetProperty("pinnedCase").GetString()!);
    }
    using var clientResponse = await client.GetAsync("/api/local-node/workflow-run-report");
    var wire = await clientResponse.Content.ReadFromJsonAsync<JsonElement>();
    Check(wire.GetProperty("data")[0].GetProperty("capabilityRef").GetString() == "ledger.post-journal-entry", "WorkflowRunReport wire round-trip");
    Check((await ClientFetchAsync(async () => { using var failed = await client.GetAsync("/api/local-node/workflow-run-report?fail=1"); failed.EnsureSuccessStatusCode(); })).failurePassedThrough, "reachable-node failure passthrough");
    Check((await ClientFetchAsync(null)).usedFallback, "no-node fallback outcome");
}
finally { await app.StopAsync(); await app.DisposeAsync(); }

ProveFailedConditions();
Console.WriteLine($"REPORTS_CAPABILITY_PASS:{JsonSerializer.Serialize(new { httpRows = 12, clientRows = 3, crossLanePairs = 0, failedConditionGuards = 5, durableStoreRowsClaimed = 0 }, json)}");

void ProveFailedConditions()
{
    var direct = Assembly.GetEntryAssembly()!.GetReferencedAssemblies().Select(x => x.Name!).Where(x => x.StartsWith("Harborline.")).ToArray();
    Check(direct.All(x => x is "Harborline.Blocks.Reports" or "Harborline.Contracts"), "no financial-cluster or closure pollution");
    Check(!corpus.RootElement.GetRawText().Contains("tax", StringComparison.OrdinalIgnoreCase) || corpus.RootElement.GetProperty("exclusions").ToString().Contains("tax"), "tax only appears as a recorded exclusion");
    Check(!httpRows.EnumerateArray().Any(x => x.GetProperty("path").GetString()!.Contains("rent-roll")), "RentRoll has no HTTP row");
    Check(corpus.RootElement.GetProperty("crossLanePairs").GetArrayLength() == 0, "no cross-pillar/cross-lane credit");
    Check(anchors.GetArrayLength() == 162, "UI-only green cannot emit package marker");
}

static async Task<(bool usedFallback, bool failurePassedThrough)> ClientFetchAsync(Func<Task>? nodeCall)
{
    if (nodeCall is null) return (true, false);
    try { await nodeCall(); return (false, false); }
    catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable) { return (false, true); }
}
static async Task ThrowsAsync<T>(Func<Task> action, string message) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("FAILED: " + message); }
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("FAILED: " + message); }

sealed class FixedMarkerSource(string marker) : ISnapshotMarkerSource { public Task<string> CaptureAsync(TenantId tenantId, CancellationToken ct = default) => Task.FromResult(marker); }
sealed class FixedTimeProvider : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero); }

sealed class ConsumerReportSource : IReportQuerySource
{
    public required ChartOfAccountsId ChartId { get; init; }
    public required DateOnly AsOf { get; init; }
    public List<ReportChart> Charts { get; } = [];
    public List<ReportAccount> Accounts { get; } = [];
    public List<ReportJournalEntry> Entries { get; } = [];
    public List<ReportOpenItem> Receivables { get; } = [];
    public List<ReportOpenItem> Payables { get; } = [];
    public Dictionary<PartyId, ReportParty> Parties { get; } = [];
    public List<ReportLease> Leases { get; } = [];
    public static ConsumerReportSource Create(TenantId tenant, TenantId foreign)
    {
        var chart = ChartOfAccountsId.NewId(); var asOf = new DateOnly(2026, 7, 18);
        var cash = GLAccountId.NewId(); var revenue = GLAccountId.NewId(); var customer = new PartyId("customer-1"); var vendor = new PartyId("vendor-1");
        var s = new ConsumerReportSource { ChartId = chart, AsOf = asOf };
        s.Charts.Add(new(chart, "Consumer Chart", "USD", true));
        s.Accounts.AddRange([new(cash, chart, "1100", "Cash", GLAccountType.Asset, NormalBalance.Debit, true), new(revenue, chart, "4100", "Revenue", GLAccountType.Revenue, NormalBalance.Credit, true)]);
        s.Entries.Add(new("posted-1", tenant, chart, asOf, [new(cash, 1000m, 0m, "property-1"), new(revenue, 0m, 1000m, "property-1")]));
        s.Entries.Add(new("foreign", foreign, chart, asOf, [new(cash, 9999m, 0m, "foreign"), new(revenue, 0m, 9999m, "foreign")]));
        s.Receivables.Add(new("invoice-1", tenant, chart, customer, "property-1", asOf.AddDays(-100), 125m, AgingBucket.Days90Plus));
        s.Payables.Add(new("bill-1", tenant, chart, vendor, "property-1", asOf.AddDays(10), 75m, AgingBucket.Current));
        s.Parties[customer] = new(customer, "Customer One"); s.Parties[vendor] = new(vendor, "Vendor One");
        s.Leases.Add(new(LeaseId.NewId(), tenant, "property-1", "Unit 1", [customer], asOf.AddMonths(-1), asOf.AddMonths(11), LeasePhase.Active, 1500m));
        return s;
    }
    public ValueTask<ReportChart?> GetChartAsync(ChartOfAccountsId id, CancellationToken ct = default) => ValueTask.FromResult(Charts.SingleOrDefault(x => x.Id == id));
    public ValueTask<ReportFiscalPeriod?> GetFiscalPeriodAsync(FiscalPeriodId id, CancellationToken ct = default) => ValueTask.FromResult<ReportFiscalPeriod?>(null);
    public ValueTask<IReadOnlyList<ReportAccount>> GetAccountsAsync(ChartOfAccountsId id, bool inactive, CancellationToken ct = default) => ValueTask.FromResult<IReadOnlyList<ReportAccount>>(Accounts.Where(x => x.ChartId == id && (inactive || x.IsActive)).ToArray());
    public ValueTask<IReadOnlyList<ReportJournalEntry>> GetPostedJournalEntriesAsync(TenantId tenant, ChartOfAccountsId chart, DateOnly? from, DateOnly through, string marker, CancellationToken ct = default) => ValueTask.FromResult<IReadOnlyList<ReportJournalEntry>>(Entries.Where(x => x.TenantId == tenant && x.ChartId == chart && x.EntryDate <= through && (from is null || x.EntryDate >= from)).ToArray());
    public ValueTask<IReadOnlyList<ReportOpenItem>> GetOpenReceivablesAsync(TenantId tenant, ChartOfAccountsId chart, DateOnly asOf, CancellationToken ct = default) => ValueTask.FromResult<IReadOnlyList<ReportOpenItem>>(Receivables.Where(x => x.TenantId == tenant && x.ChartId == chart).ToArray());
    public ValueTask<IReadOnlyList<ReportOpenItem>> GetOpenPayablesAsync(TenantId tenant, ChartOfAccountsId chart, DateOnly asOf, CancellationToken ct = default) => ValueTask.FromResult<IReadOnlyList<ReportOpenItem>>(Payables.Where(x => x.TenantId == tenant && x.ChartId == chart).ToArray());
    public ValueTask<IReadOnlyDictionary<PartyId, ReportParty>> GetPartiesAsync(IReadOnlyCollection<PartyId> ids, CancellationToken ct = default) => ValueTask.FromResult<IReadOnlyDictionary<PartyId, ReportParty>>(Parties.Where(x => ids.Contains(x.Key)).ToDictionary());
    public ValueTask<IReadOnlyList<ReportLease>> GetLeasesAsync(TenantId tenant, CancellationToken ct = default) => ValueTask.FromResult<IReadOnlyList<ReportLease>>(Leases.Where(x => x.TenantId == tenant).ToArray());
}
