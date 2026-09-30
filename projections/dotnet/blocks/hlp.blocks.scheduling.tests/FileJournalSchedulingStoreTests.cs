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

    [Fact(DisplayName = "DES-0022 ruling 8: a journal holding a retired integer-revision draft frame refuses at startup")]
    public async Task Retired_integer_revision_draft_frame_refuses_at_startup()
    {
        using var fixture = new JournalFixture();
        using (var store = fixture.Open()) await store.SaveCalendarEntityAsync(Calendar());
        var before = new FileInfo(fixture.Path).Length;
        await using (var file = new FileStream(fixture.Path, FileMode.Append, FileAccess.Write, FileShare.None))
        {
            var payload = System.Text.Encoding.UTF8.GetBytes(
                "{\"TenantId\":\"tenant-a\",\"DefinitionId\":\"definition-1\",\"Revision\":1,\"DocumentJson\":\"{}\",\"ActorId\":\"actor\",\"OccurredAt\":\"2026-03-08T13:00:00+00:00\"}");
            await file.WriteAsync(Frame(1, payload));
            file.Flush(true);
        }
        var refusal = Assert.Throws<InvalidDataException>(() => fixture.Open());
        Assert.Contains("integer-revision", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("shared versioned-definition store", refusal.Message, StringComparison.Ordinal);
        Assert.True(new FileInfo(fixture.Path).Length > before);
    }

    [Fact(DisplayName = "DES-0022 ruling 8: the journal no longer stores scheduling definitions or exposes an integer revision")]
    public void Journal_exposes_no_definition_draft_or_integer_revision()
    {
        var members = typeof(FileJournalSchedulingStore).GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(member => member.Name).ToArray();
        Assert.DoesNotContain(members, name => name.Contains("Draft", StringComparison.Ordinal) || name.Contains("Audit", StringComparison.Ordinal));
        var exported = typeof(FileJournalSchedulingStore).Assembly.GetExportedTypes();
        Assert.DoesNotContain(exported, type => type.GetProperties().Any(property =>
            property.Name.Contains("Revision", StringComparison.Ordinal) && property.PropertyType == typeof(long)));
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
        Assert.NotNull(await reopened.GetCalendarEntityAsync("tenant:probe", SchedulingCalendarEntityKind.ResourceAvailability, "party:1"));
    }

    [Fact]
    public async Task Corruption_in_a_complete_frame_fails_startup()
    {
        using var fixture = new JournalFixture();
        using (var store = fixture.Open()) await store.SaveCalendarEntityAsync(Calendar());
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
        using (var store = fixture.Open()) await store.SaveCalendarEntityAsync(Calendar());
        using (var file = new FileStream(fixture.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        { file.Position = 4; file.WriteByte(0xff); file.Flush(true); }
        Assert.Throws<InvalidDataException>(() => fixture.Open());
    }

    private static SchedulingCalendarEntity Calendar()
        => new("tenant", SchedulingCalendarEntityKind.OwnedCalendar, "calendar-1", "{}");

    // The journal's frame layout: 12-byte header, SHA-256 of the header, payload, SHA-256 of header+payload.
    private static byte[] Frame(byte recordType, byte[] payload)
    {
        var header = new byte[12];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(header, 0x4A534C48);
        header[4] = 1; header[5] = recordType;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), payload.Length);
        var framed = header.Concat(payload).ToArray();
        return [.. header, .. System.Security.Cryptography.SHA256.HashData(header), .. payload, .. System.Security.Cryptography.SHA256.HashData(framed)];
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

    private sealed class JournalFixture : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hl-scheduling-" + Guid.NewGuid().ToString("N"));
        public JournalFixture() => Directory.CreateDirectory(directory);
        public string Path => System.IO.Path.Combine(directory, "scheduling.journal");
        public FileJournalSchedulingStore Open() => new(new() { JournalPath = Path });
        public void Dispose() { try { Directory.Delete(directory, recursive: true); } catch { } }
    }
}
