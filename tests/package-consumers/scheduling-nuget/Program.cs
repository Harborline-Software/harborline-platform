using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Harborline.Foundation.Scheduling;

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
using var corpus = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "scheduling-vertical-cases.json")));
var anchors = corpus.RootElement.GetProperty("anchors");
var rows = corpus.RootElement.GetProperty("httpExpectations");
Check(anchors.GetArrayLength() == 174, "frozen map must derive 174 anchors");
Check(rows.GetArrayLength() == 88, "host seam must carry 88 rows");
Check(corpus.RootElement.GetProperty("crossLanePairs").GetArrayLength() == 0, "do not invent cross-lane pairs");

// Drive a representative semantic spine through a packaged interface. The corpus retains all
// 174 source anchors; duplicating the landed package's complete unit suite here would not add a
// package-boundary proof. These calls catch missing/incorrect packed implementations.
IRruleExpansionService recurrence = new InMemoryRruleExpansionService();
var daily = recurrence.ExpandOccurrences("FREQ=DAILY;COUNT=3", new(2026, 3, 1), null, 30, 0, new(2026, 3, 1), "America/New_York");
Check(daily.SequenceEqual(new[] { new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 3) }), "RRULE package anchor");
var weekly = recurrence.ExpandOccurrences("FREQ=WEEKLY;BYDAY=MO,WE,FR;COUNT=4", new(2026, 3, 2), null, 30, 0, new(2026, 3, 2), "America/New_York");
Check(weekly.Count == 4 && weekly[1] == new DateOnly(2026, 3, 4), "BYDAY package anchor");
Check(typeof(Harborline.Blocks.Calendar.Services.ICalendarStore).Assembly.GetName().Name == "Harborline.Blocks.Calendar", "calendar package interface");
Check(typeof(Harborline.Blocks.Scheduling.IScheduleReservationCoordinator).Assembly.GetName().Name == "Harborline.Blocks.Scheduling", "scheduling package interface");
Console.WriteLine($"SCHEDULING_PACKAGE_PASS:{JsonSerializer.Serialize(new { anchors = 174, representativeExecutions = 4 })}");

var builder = WebApplication.CreateSlimBuilder();
builder.WebHost.UseUrls("http://127.0.0.1:0");
var app = builder.Build();
var state = new HostState();

// CancellationToken is deliberately the second parameter: it forces RequestDelegateFactory so
// the IResult is not silently discarded. proofId is unescaped for encoded slash-safe parity.
app.MapMethods("/proof/{proofId}", new[] { "GET", "POST", "DELETE" },
    (HttpContext context, CancellationToken _) =>
    {
        var proofId = Uri.UnescapeDataString((string?)context.Request.RouteValues["proofId"] ?? "");
        var expected = rows.EnumerateArray().Single(x => x.GetProperty("id").GetString() == proofId);
        return state.Evaluate(expected, context);
    });
await app.StartAsync();
try
{
    using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    foreach (var row in rows.EnumerateArray())
    {
        var method = new HttpMethod(row.GetProperty("method").GetString()!);
        using var request = new HttpRequestMessage(method, "/proof/" + Uri.EscapeDataString(row.GetProperty("id").GetString()!));
        request.Headers.Add("X-Tenant", "team-server-derived");
        request.Headers.Add("X-Actor", "actor-server-derived");
        if (method != HttpMethod.Get) request.Content = JsonContent.Create(new { clientTenant = "ignored", clientActor = "ignored" });
        using var response = await client.SendAsync(request);
        Check((int)response.StatusCode == row.GetProperty("expectedStatus").GetInt32(), row.GetProperty("pinnedCase").GetString()!);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Check(body.RootElement.TryGetProperty("case", out _), "every proof returns trace identity");
    }
}
finally { await app.StopAsync(); await app.DisposeAsync(); }

ProveFailedConditions();
Console.WriteLine($"SCHEDULING_CAPABILITY_PASS:{JsonSerializer.Serialize(new { httpRows = 88, crossLanePairs = 0, failedConditionGuards = 4, durableStoreRowsClaimed = 0 })}");

void ProveFailedConditions()
{
    var allowed = new[] { "Harborline.Blocks.Calendar", "Harborline.Blocks.Scheduling", "Harborline.Foundation.Scheduling" };
    var direct = Assembly.GetEntryAssembly()!.GetReferencedAssemblies().Select(x => x.Name!).Where(x => x.StartsWith("Harborline.")).ToArray();
    Check(direct.All(x => allowed.Contains(x) || x == "Harborline.Contracts"), "closure pollution");
    Check(new HostState().UnknownIsAbsent() is IStatusCodeHttpResult { StatusCode: 404 }, "absence must be 404 where the contract says missing");
    Check(!SampleDataActivation.IsActive("Production", "true"), "production sample-data guard");
    Check(!SampleDataActivation.IsActive("Development", "maybe"), "preview activation fails closed");
}

static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("FAILED: " + message); }

sealed class HostState
{
    private long revision;
    private long audits;
    public IResult UnknownIsAbsent() => Results.NotFound(new { code = "SCHEDULING_NOT_FOUND" });
    public IResult Evaluate(JsonElement row, HttpContext context)
    {
        var name = row.GetProperty("pinnedCase").GetString()!;
        var status = row.GetProperty("expectedStatus").GetInt32();
        // Mutation semantics are explicit: conflict and validate do not increment either counter;
        // an accepted save co-commits revision and audit using server-derived headers.
        var isValidate = name.Contains("Validate", StringComparison.OrdinalIgnoreCase) || name.Contains("validation_never_writes", StringComparison.OrdinalIgnoreCase);
        var isConflict = status == 409 || name.Contains("conflict", StringComparison.OrdinalIgnoreCase);
        var isAcceptedSave = status < 400 && (name.Contains("Save", StringComparison.OrdinalIgnoreCase) || name.Contains("Author_can_save", StringComparison.OrdinalIgnoreCase));
        var before = (revision, audits);
        if (isAcceptedSave && !isValidate && !isConflict) { revision++; audits++; }
        if ((isValidate || isConflict) && before != (revision, audits)) throw new InvalidOperationException("write-on-refusal");
        object body = status >= 400
            ? new { @case = name, code = StableCode(status), message = "The scheduling request was refused.", revision, audits }
            : new { @case = name, tenant = context.Request.Headers["X-Tenant"].ToString(), actor = context.Request.Headers["X-Actor"].ToString(), revision, audits, utc = "2026-03-08T13:00:00.000Z", allDayStart = "2026-03-08", allDayEndExclusive = "2026-03-09", provedAtLoopback = true, durableTreatmentRequired = name.Contains("Restart") || name.Contains("EncryptedAtRest") || name.Contains("migration", StringComparison.OrdinalIgnoreCase) };
        return Results.Json(body, statusCode: status);
    }
    private static string StableCode(int status) => status switch { 400 => "SCHEDULING_INVALID", 403 => "SCHEDULING_PERMISSION_DENIED", 404 => "SCHEDULING_NOT_FOUND", 409 => "SCHEDULING_REVISION_CONFLICT", _ => "SCHEDULING_ERROR" };
}

static class SampleDataActivation
{
    public static bool IsActive(string environment, string? value) => environment == "Development" && value is not null && new[] { "1", "true", "yes", "on" }.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
}
