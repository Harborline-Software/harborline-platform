using System.Diagnostics;
using Harborline.Blocks.Scheduling.Durable;
using Xunit;

namespace Harborline.Blocks.Scheduling.DurableDraft.Tests;

public sealed class FileJournalSchedulingStoreTests
{
    [Fact]
    public async Task NodeEfCalendarCollectionStoreTests_Calendar_RoundTrips_AcrossContextReopen()
    {
        using var fixture = new JournalFixture();
        var row = new SchedulingCalendarEntity("tenant-a", SchedulingCalendarEntityKind.OwnedCalendar, "calendar-1", "{\"isDefault\":true}");
        using (var store = fixture.Open()) await store.SaveCalendarEntityAsync(row);
        using var reopened = fixture.Open();
        Assert.Equal(row, await reopened.GetCalendarEntityAsync("tenant-a", row.Kind, row.EntityId));
    }

    [Fact]
    public async Task NodeEfCalendarStoreTests_CalendarEvent_RoundTrips_AcrossContextReopen()
        => await RoundTrip(SchedulingCalendarEntityKind.CalendarEvent, "event-1", "{\"allDay\":false}");

    [Fact]
    public async Task NodeEfCalendarStoreTests_ResourceAvailability_RoundTrips_AcrossContextReopen()
        => await RoundTrip(SchedulingCalendarEntityKind.ResourceAvailability, "party:1", "{\"windows\":[\"09:00\"]}");

    [Fact]
    public async Task NodeSchedulingDraftStoreTests_Save_survives_new_store_and_records_server_actor_audit()
    {
        using var fixture = new JournalFixture();
        var instant = new DateTimeOffset(2026, 3, 8, 13, 0, 0, TimeSpan.Zero);
        using (var store = fixture.Open(new FrozenTimeProvider(instant)))
            Assert.True((await store.SaveDraftAsync("tenant-a", "definition-1", 0, "{}", "server-actor")).Saved);
        using var reopened = fixture.Open();
        Assert.Equal(1, (await reopened.GetDraftAsync("tenant-a", "definition-1"))!.Revision);
        var audit = Assert.Single(await reopened.GetAuditAsync("tenant-a", "definition-1"));
        Assert.Equal("server-actor", audit.ActorId);
        Assert.Equal(instant, audit.OccurredAt);
    }

    [Fact]
    public async Task Draft_and_audit_are_physically_co_committed_in_one_flushed_frame()
    {
        using var fixture = new JournalFixture(); using var store = fixture.Open();
        var before = store.JournalLength;
        await store.SaveDraftAsync("tenant-a", "definition-1", 0, "{}", "actor");
        Assert.True(store.JournalLength > before);
        Assert.NotNull(await store.GetDraftAsync("tenant-a", "definition-1"));
        Assert.Single(await store.GetAuditAsync("tenant-a", "definition-1"));
    }

    [Fact]
    public async Task NodeSchedulingDraftStoreTests_Expected_revision_conflict_writes_neither_revision_nor_audit_at_file_level()
    {
        using var fixture = new JournalFixture(); using var store = fixture.Open();
        await store.SaveDraftAsync("tenant-a", "definition-1", 0, "{}", "actor");
        var before = new FileInfo(fixture.Path).Length;
        var result = await store.SaveDraftAsync("tenant-a", "definition-1", 0, "{\"stale\":true}", "actor");
        Assert.False(result.Saved);
        Assert.Equal(before, new FileInfo(fixture.Path).Length);
        Assert.Single(await store.GetAuditAsync("tenant-a", "definition-1"));
    }

    [Fact]
    public void Store_holds_an_exclusive_process_lock()
    {
        using var fixture = new JournalFixture(); using var first = fixture.Open();
        Assert.Throws<IOException>(() => fixture.Open());
    }

    [Fact]
    public async Task Authenticated_incomplete_final_frame_is_truncated_without_losing_commits()
    {
        using var fixture = new JournalFixture();
        await RunProbeAsync("write-partial", fixture.Path, killAfterReady: false);
        var tornLength = new FileInfo(fixture.Path).Length;
        using var reopened = fixture.Open();
        Assert.True(reopened.JournalLength < tornLength);
        Assert.NotNull(await reopened.GetDraftAsync("tenant:probe", "definition-1"));
    }

    [Fact]
    public async Task Corruption_in_a_complete_frame_fails_startup()
    {
        using var fixture = new JournalFixture();
        using (var store = fixture.Open()) await store.SaveDraftAsync("tenant", "id", 0, "{}", "actor");
        using (var file = new FileStream(fixture.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        { file.Position = 44; var value = file.ReadByte(); file.Position = 44; file.WriteByte((byte)(value ^ 0xff)); file.Flush(true); }
        Assert.Throws<InvalidDataException>(() => fixture.Open());
    }

    [Fact]
    public async Task NodeEfCalendarCollectionStoreTests_Calendar_Survives_HostRestart_and_NodeEfCalendarStoreTests_CalendarEvent_Survives_HostRestart()
    {
        using var fixture = new JournalFixture();
        await RunProbeAsync("write-wait", fixture.Path, killAfterReady: true);
        using var reopened = fixture.Open();
        Assert.NotNull(await reopened.GetDraftAsync("tenant:probe", "definition-1"));
        Assert.NotNull(await reopened.GetCalendarEntityAsync("tenant:probe", SchedulingCalendarEntityKind.OwnedCalendar, "calendar-1"));
        Assert.NotNull(await reopened.GetCalendarEntityAsync("tenant:probe", SchedulingCalendarEntityKind.CalendarEvent, "event-1"));
        Assert.NotNull(await reopened.GetCalendarEntityAsync("tenant:probe", SchedulingCalendarEntityKind.ResourceAvailability, "party:1"));
    }

    // The two encryption-at-rest rows and the three SQL/EF migration rows are NOT claimed
    // here and carry no placeholder tests: their deferral/unmappability is recorded in the
    // scheduling behavior-map addendum and the module spec (named at-rest seam; EF topology
    // replaced by the narrowed file-journal seam). Skipped tests are not deferral records.

    [Fact]
    public async Task Unknown_journal_format_version_fails_startup()
    {
        using var fixture = new JournalFixture();
        using (var store = fixture.Open()) await store.SaveDraftAsync("tenant", "id", 0, "{}", "actor");
        using (var file = new FileStream(fixture.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        { file.Position = 4; file.WriteByte(0xff); file.Flush(true); }
        Assert.Throws<InvalidDataException>(() => fixture.Open());
    }

    private static async Task RoundTrip(SchedulingCalendarEntityKind kind, string id, string json)
    {
        using var fixture = new JournalFixture(); var row = new SchedulingCalendarEntity("tenant-a", kind, id, json);
        using (var store = fixture.Open()) await store.SaveCalendarEntityAsync(row);
        using var reopened = fixture.Open(); Assert.Equal(row, await reopened.GetCalendarEntityAsync("tenant-a", kind, id));
    }

    private static async Task RunProbeAsync(string mode, string path, bool killAfterReady)
    {
        // The workflows restart-probe convention: locate the probe DLL by repo-root-relative
        // path + the current build configuration (the tests csproj carries a
        // ReferenceOutputAssembly=false ProjectReference purely for build ordering).
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Harborline.Platform.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var configuration = new FileInfo(typeof(FileJournalSchedulingStoreTests).Assembly.Location).Directory!.Parent!.Name;
        var dll = Path.Combine(
            root!.FullName,
            "projections/dotnet/blocks/hlp.blocks.scheduling.restart-probe/bin",
            configuration,
            "net11.0",
            "Harborline.Blocks.Scheduling.RestartProbe.dll");
        Assert.True(File.Exists(dll), $"Restart probe was not built: {dll}");
        using var process = Process.Start(new ProcessStartInfo("dotnet", $"\"{dll}\" {mode} \"{path}\"")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
        if (killAfterReady)
        {
            Assert.Equal("ready", await process.StandardOutput.ReadLineAsync());
            process.Kill(entireProcessTree: true);
        }
        await process.WaitForExitAsync();
        if (!killAfterReady) Assert.Equal(0, process.ExitCode);
    }

    private sealed class FrozenTimeProvider(DateTimeOffset value) : TimeProvider { public override DateTimeOffset GetUtcNow() => value; }
    private sealed class JournalFixture : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hl-scheduling-" + Guid.NewGuid().ToString("N"));
        public JournalFixture() => Directory.CreateDirectory(directory);
        public string Path => System.IO.Path.Combine(directory, "scheduling.journal");
        public FileJournalSchedulingStore Open(TimeProvider? provider = null) => new(new() { JournalPath = Path }, provider);
        public void Dispose() { try { Directory.Delete(directory, recursive: true); } catch { } }
    }
}
