using System.Reflection;
using System.Text.Json;
using Harborline.Blocks.ActivityTimeline;
using Harborline.Foundation.Assets.Common;

using var corpus = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "aggregates-vertical-cases.json")));
var root = corpus.RootElement;
var ported = root.GetProperty("portedCases");
var authored = root.GetProperty("authoredCases");
var carried = root.GetProperty("carriedUiCases");
var pairs = root.GetProperty("crossLanePairs");
Check(ported.GetArrayLength() == 7, "corpus must carry seven ported rows");
Check(authored.GetArrayLength() == 4 && authored.EnumerateArray().All(row => row.GetProperty("authored").GetBoolean()), "four authored rows must be marked");
Check(carried.GetArrayLength() == 20 && carried.EnumerateArray().All(row => row.GetProperty("carried").GetBoolean()), "twenty UI rows must be carried");
Check(pairs.GetArrayLength() == 4, "four cross-lane agreement pairs required");

var tenant = new TenantId(root.GetProperty("fixtureEntries").GetProperty("tenantId").GetString()!);
var session = new ActivitySessionId(root.GetProperty("fixtureEntries").GetProperty("sessionId").GetString()!);
var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero));
var source = new InMemoryActivityEntrySource(new CorpusAttribution(), clock);

// A-B01: starts empty.
Check(source.Read(tenant, session).Count == 0 && source.Count(tenant, session) == 0 && source.Notifications.Count == 0, Pinned(0));

// Replay the shared fixture through packaged drafts; do not construct caller-visible entries.
foreach (var row in root.GetProperty("fixtureEntries").GetProperty("entriesInAppendOrder").EnumerateArray())
{
    source.Append(new ActivityEntryDraft(
        row.GetProperty("id").GetString()!, tenant, session,
        row.GetProperty("proposedByActorId").GetString(), null,
        ParseCategory(row.GetProperty("category").GetString()!),
        row.GetProperty("timestamp").GetString()!,
        new ActivityDetail(row.GetProperty("action").GetString()!, OptionalString(row.GetProperty("detail")))));
}
var timeline = source.Read(tenant, session);
Check(timeline.Count == 2 && source.Count(tenant, session) == timeline.Count, Pinned(1));
Check(timeline.Any(entry => entry.Category == ActivityCategory.CapabilityError), Pinned(2));
Check(timeline.Select(entry => entry.Id).SequenceEqual(new[] { "act:result:j2", "act:result:job-1" }), Pinned(5));

// Notification lifecycle cases use the packaged AddResult/notification surface and injected time.
var notifications = new InMemoryActivityEntrySource(new CorpusAttribution(), clock);
notifications.AddResult(tenant, session, "j1", "A", null, true);
Check(notifications.Notifications.Count == 1 && notifications.Count(tenant, session) == 1 && notifications.Read(tenant, session)[0].Category == ActivityCategory.CapabilityResult, Pinned(1));
notifications.AddResult(tenant, session, "j2", "B", null, false);
Check(notifications.Read(tenant, session)[0].Category == ActivityCategory.CapabilityError, Pinned(2));
Check(notifications.Notifications.Select(item => item.Id).SequenceEqual(new[] { "result:j2", "result:j1" }), Pinned(5));
notifications.DismissNotification("result:j1");
Check(notifications.Notifications.Count == 1 && notifications.Notifications[0].Id == "result:j2", Pinned(3));
notifications.MarkAllRead();
Check(notifications.Notifications.All(item => item.Read), Pinned(4));
var beforeDismiss = notifications.Count(tenant, session);
notifications.DismissNotification("result:j2");
Check(notifications.Count(tenant, session) == beforeDismiss, Pinned(6));

// Authored obligation 3: both dimensions fail closed, including their count projections.
Check(source.Read(new TenantId("foreign-tenant"), session).Count == 0 && source.Count(new TenantId("foreign-tenant"), session) == 0, "AUTH-03-TENANT");
Check(source.Read(tenant, new ActivitySessionId("foreign-session")).Count == 0 && source.Count(tenant, new ActivitySessionId("foreign-session")) == 0, "AUTH-03-SESSION");

// Authored obligation 5: the returned public shape has no raw-id/actor member and attribution is sealed.
var publicNames = typeof(ActivityEntry).GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(property => property.Name).ToArray();
Check(!publicNames.Any(name => name.Contains("ActorId", StringComparison.OrdinalIgnoreCase) || name.Equals("Actor", StringComparison.OrdinalIgnoreCase)), "AUTH-05-SURFACE");
Check(timeline.All(entry => entry.ProposedBy?.DisplayName == "System") && !timeline.Any(entry => entry.ToString().Contains("raw-system-001", StringComparison.Ordinal)), "AUTH-05-SEALING");

var projection = new
{
    orderedIds = timeline.Select(entry => entry.Id).ToArray(),
    displayNames = timeline.Select(entry => entry.ProposedBy!.DisplayName).ToArray(),
    categories = timeline.Select(entry => CategoryName(entry.Category)).ToArray(),
    count = source.Count(tenant, session)
};
Console.WriteLine($"CROSS_LANE_TIMELINE:{JsonSerializer.Serialize(projection)}");
Console.WriteLine($"AGGREGATES_PACKAGE_PASS:{JsonSerializer.Serialize(new { ported = ported.GetArrayLength(), authored = authored.GetArrayLength() })}");
Console.WriteLine($"AGGREGATES_CAPABILITY_PASS:{JsonSerializer.Serialize(new { ported = ported.GetArrayLength(), authored = authored.GetArrayLength(), carried = carried.GetArrayLength(), pairs = pairs.GetArrayLength() })}");

string Pinned(int index) => ported[index].GetProperty("pinnedName").GetString()!;
static string? OptionalString(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetString();
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("FAILED: " + message); }
static ActivityCategory ParseCategory(string value) => value switch
{
    "capability-result" => ActivityCategory.CapabilityResult,
    "capability-error" => ActivityCategory.CapabilityError,
    _ => throw new InvalidOperationException("Unknown fixture category: " + value)
};
static string CategoryName(ActivityCategory value) => value switch
{
    ActivityCategory.CapabilityResult => "capability-result",
    ActivityCategory.CapabilityError => "capability-error",
    _ => throw new InvalidOperationException("Non-canonical projection category: " + value)
};

sealed class CorpusAttribution : IActivityAttribution
{
    public ActivityDisplayIdentity Resolve(string actorId) => actorId switch
    {
        "raw-system-001" => new ActivityDisplayIdentity("System"),
        _ => throw new InvalidOperationException("Unknown fixture actor id")
    };
}

sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => value;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
