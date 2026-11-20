using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Harborline.Blocks.Workflow.Durable;

using Xunit;

namespace Harborline.Blocks.Workflow.Tests;

public sealed class WorkflowLifecycleFactsTests
{
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.Parse("2001-02-03T04:05:06Z");

    [Fact]
    public async Task Engine_start_and_each_advance_map_from_reopened_history_alone()
    {
        using var fixture = new FileJournalWorkflowStoreTests.JournalFixture();
        using (var store = fixture.Open(new FixedClock()))
        {
            var definitions = new InMemoryWorkflowDefinitionStore();
            var authored = JsonSerializer.SerializeToElement(new
            {
                initialState = "A",
                states = new[] { new { id = "A", kind = "Normal" }, new { id = "B", kind = "Normal" }, new { id = "C", kind = "Terminal" } },
                triggers = new[] { new { id = "issued", kind = "Event", eventType = "Issued" } },
                transitions = new[] { new { id = "ab", from = "A", @on = "issued", to = "B" }, new { id = "bc", from = "B", @on = "issued", to = "C" } },
            });
            await definitions.RegisterAsync(WorkflowDefinitionWireMapper.ToModel(authored, "tenant", "flow", "1.2.3"), authored);
            await definitions.PublishAsync("tenant", "flow", "1.2.3");
            await new WorkflowInstanceCreator(definitions, store).CreateAsync(Instance());
            var dispatcher = new WorkflowTriggerDispatcher(store, [new Handler()], definitionStore: definitions);
            var authority = new WorkflowDispatchAuthority("operator", "tenant", "records:write", "record", "run", true);
            Assert.Equal(WorkflowDispatchResult.Advanced, await dispatcher.DispatchAsync(WorkflowTrigger.For(WorkflowTriggerKind.Event, "run", "A"), authority));
            Assert.Equal(WorkflowDispatchResult.Advanced, await dispatcher.DispatchAsync(WorkflowTrigger.For(WorkflowTriggerKind.Event, "run", "B"), authority));
        }

        Assert.Equal(new byte[] { 1, 2, 2 }, Frames(fixture.JournalPath).Select(f => f.Type));
        using var reopened = fixture.Open();
        var facts = await reopened.EventsAsync("run");
        Assert.Equal(new long[] { 1, 2, 3 }, facts.Select(f => f.Seq));
        Assert.Equal(new[] { "WorkflowStarted", "TaskCompleted", "TaskCompleted" }, facts.Select(f => f.EventType));
        var events = facts.SelectMany(f => WorkflowLifecycleProjection.Map(f, new("journal", "tenant"))).ToArray();
        Assert.Equal(new[]
        {
            "io.serverlessworkflow.workflow.started.v1", "io.serverlessworkflow.task.completed.v1",
            "io.serverlessworkflow.task.completed.v1", "io.serverlessworkflow.workflow.completed.v1",
        }, events.Select(e => e.Type));
        Assert.Equal("/A", events[1].Data.GetProperty("task").GetString());
        Assert.Equal("2001-02-03T04:06:00.0000000+00:00", events[1].Data.GetProperty("completedAt").GetString());
        Assert.Equal("/B", events[2].Data.GetProperty("task").GetString());
        Assert.NotEqual(events[2].Id, events[3].Id);
        using var data = JsonDocument.Parse(facts[2].DataJson);
        Assert.Equal("completed", data.RootElement.GetProperty("workflowStatus").GetString());
    }

    [Fact]
    public async Task Start_failure_before_frame_leaves_neither_instance_nor_fact_and_retry_is_atomic()
    {
        using var fixture = new FileJournalWorkflowStoreTests.JournalFixture();
        var options = new FileJournalWorkflowStoreOptions { JournalPath = fixture.JournalPath, MaximumFrameBytes = 1024 };
        using (var store = new FileJournalWorkflowStore(options, new FixedClock()))
        {
            var oversized = Instance();
            oversized.StateJson = new string('x', 2048);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateInstanceAsync(oversized));
            Assert.Null(await store.LoadAsync("run"));
            Assert.Empty(await store.EventsAsync("run"));
        }
        Assert.Empty(File.ReadAllBytes(fixture.JournalPath));
        using (var reopened = fixture.Open())
        {
            Assert.Null(await reopened.LoadAsync("run"));
            Assert.Empty(await reopened.EventsAsync("run"));
            await reopened.CreateInstanceAsync(Instance());
            AssertStarted(Assert.Single(await reopened.EventsAsync("run")));
        }
        Assert.Equal((Version: (byte)2, Type: (byte)1), Assert.Single(Frames(fixture.JournalPath)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Staging_failure_leaves_no_advance_fact_and_committed_outcome_survives_reopen(bool failed)
    {
        using var fixture = new FileJournalWorkflowStoreTests.JournalFixture();
        using (var store = fixture.Open(new FixedClock()))
        {
            await store.CreateInstanceAsync(Instance());
            var before = new FileInfo(fixture.JournalPath).Length;
            var effect = new WorkflowEffect((unit, _) =>
            {
                ((FileJournalWorkflowUnitOfWork)unit).StageEffectPayload("must-not-commit");
                throw new InvalidOperationException("staging fault");
            });
            var status = failed ? WorkflowStatus.Failed : WorkflowStatus.Completed;
            var key = new WorkflowStepKey("run", 0, "A");
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.AdvanceAsync(key, effect, "{}", "Outcome", "{}", "B", status));
            Assert.Equal(before, new FileInfo(fixture.JournalPath).Length);
            AssertStarted(Assert.Single(await store.EventsAsync("run")));
            Assert.Null(await store.FindStepResultAsync(key));
            Assert.Empty(await store.CommittedEffectPayloadsAsync("run"));
            Assert.Equal(WorkflowStatus.Running, (await store.LoadAsync("run"))!.Status);
            await store.AdvanceAsync(key, null, "{}", "Outcome", "{}", "B", status);
        }

        Assert.Equal(new byte[] { 1, 2 }, Frames(fixture.JournalPath).Select(f => f.Type));
        using var reopened = fixture.Open();
        var facts = await reopened.EventsAsync("run");
        Assert.Equal(new long[] { 1, 2 }, facts.Select(f => f.Seq));
        var events = WorkflowLifecycleProjection.Map(facts[1], new("journal", "tenant"));
        Assert.Equal(failed
            ? new[] { "io.serverlessworkflow.task.faulted.v1", "io.serverlessworkflow.workflow.faulted.v1" }
            : new[] { "io.serverlessworkflow.task.completed.v1", "io.serverlessworkflow.workflow.completed.v1" }, events.Select(e => e.Type));
        Assert.NotEqual(events[0].Id, events[1].Id);
    }

    [Fact]
    public async Task Legacy_history_stays_missing_while_new_frames_use_version_two()
    {
        using var fixture = new FileJournalWorkflowStoreTests.JournalFixture();
        WriteLegacyJournal(fixture.JournalPath);
        using (var store = fixture.Open(new FixedClock()))
        {
            Assert.Equal((2, 1, 1, 1), await store.CountsAsync());
            Assert.Equal("B", (await store.LoadAsync("run"))!.CurrentStep);
            Assert.Equal("{\"saved\":true}", (await store.LoadAsync("other"))!.StateJson);
            Assert.Equal(new[] { "old-effect" }, await store.CommittedEffectPayloadsAsync("run"));
            Assert.Equal("{}", (await store.FindStepResultAsync(new WorkflowStepKey("run", 0, "A")))!.ResultJson);
            var old = Assert.Single(await store.EventsAsync("run"));
            Assert.Equal("Advanced", old.EventType);
            Assert.Equal("{\"old\":true}", old.DataJson);
            Assert.Equal(1, old.Seq);
            Assert.Empty(WorkflowLifecycleProjection.Map(old, new("journal", "tenant")));
            Assert.Empty(await store.EventsAsync("other"));
            await store.AdvanceAsync(new WorkflowStepKey("run", 0, "B"), null, "{}", "Completed", "{}", "C", WorkflowStatus.Completed);
        }
        Assert.Equal(new byte[] { 1, 1, 1, 2 }, Frames(fixture.JournalPath).Select(f => f.Version));
        using var reopened = fixture.Open();
        var history = await reopened.EventsAsync("run");
        Assert.Equal(new[] { "Advanced", "TaskCompleted" }, history.Select(f => f.EventType));
        Assert.DoesNotContain(history, f => f.EventType == "WorkflowStarted");
        Assert.Empty(await reopened.EventsAsync("other"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(255)]
    public void Unreadable_frame_version_is_refused_without_truncation(byte version)
    {
        using var fixture = new FileJournalWorkflowStoreTests.JournalFixture();
        WriteFrame(fixture.JournalPath, version, 1, "{}");
        var before = File.ReadAllBytes(fixture.JournalPath);
        Assert.Throws<InvalidDataException>(() => fixture.Open());
        Assert.Equal(before, File.ReadAllBytes(fixture.JournalPath));
    }

    private static void WriteLegacyJournal(string path)
    {
        // Literal v1 DTOs and framing from the pre-slice journal contract, independent of the writer.
        WriteFrame(path, 1, 1, """{"Id":"run","TenantId":"tenant","DefinitionKey":"flow","DefinitionVersion":"1.2.3","CurrentStep":"A","Iteration":0,"Status":0,"StateJson":"{}","CreatedAt":"2001-02-03T04:05:06.0000000+00:00","UpdatedAt":"2001-02-03T04:05:06.0000000+00:00"}""");
        WriteFrame(path, 1, 2, """{"InstanceId":"run","Iteration":0,"Step":"A","Key":"run:0:A","EffectPayloads":["old-effect"],"ResultJson":"{}","Event":{"InstanceId":"run","Seq":1,"Step":"A","EventType":"Advanced","DataJson":"{\"old\":true}","At":"2001-02-03T04:06:00.0000000+00:00"},"NextStep":"B","NextStatus":0,"At":"2001-02-03T04:06:00.0000000+00:00"}""");
        WriteFrame(path, 1, 1, """{"Id":"other","TenantId":"tenant","DefinitionKey":"flow","DefinitionVersion":"1.2.3","CurrentStep":"A","Iteration":0,"Status":0,"StateJson":"{\"saved\":true}","CreatedAt":"2001-02-03T04:05:06.0000000+00:00","UpdatedAt":"2001-02-03T04:05:06.0000000+00:00"}""");
    }

    private static void WriteFrame(string path, byte version, byte type, string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var header = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(header, 0x4A574C48);
        header[4] = version;
        header[5] = type;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), payload.Length);
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write);
        stream.Write(header);
        stream.Write(SHA256.HashData(header));
        stream.Write(payload);
        stream.Write(SHA256.HashData(header.Concat(payload).ToArray()));
    }

    private static IReadOnlyList<(byte Version, byte Type)> Frames(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var frames = new List<(byte, byte)>();
        for (var offset = 0; offset < bytes.Length;)
        {
            frames.Add((bytes[offset + 4], bytes[offset + 5]));
            offset += 76 + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 8));
            Assert.True(offset <= bytes.Length);
        }
        return frames;
    }

    [Fact]
    public async Task Start_commits_pinned_fact_with_instance_and_survives_reopen()
    {
        using var fixture = new FileJournalWorkflowStoreTests.JournalFixture();
        using (var store = fixture.Open())
        {
            var instance = Instance();
            await store.CreateInstanceAsync(instance);
            instance.DefinitionKey = "later";
            instance.DefinitionVersion = "9.0.0";
            var fact = Assert.Single(await store.EventsAsync("run"));
            AssertStarted(fact);
        }

        using var reopened = fixture.Open();
        Assert.Equal("flow", (await reopened.LoadAsync("run"))!.DefinitionKey);
        AssertStarted(Assert.Single(await reopened.EventsAsync("run")));
    }

    private static void AssertStarted(WorkflowEventRecord fact)
    {
        Assert.Equal(1, fact.Seq);
        Assert.Equal("WorkflowStarted", fact.EventType);
        Assert.Equal(StartedAt, fact.OccurredAt);
        var projected = Assert.Single(WorkflowLifecycleProjection.Map(fact, new("journal", "tenant")));
        Assert.Equal("io.serverlessworkflow.workflow.started.v1", projected.Type);
        Assert.Equal("run", projected.Data.GetProperty("name").GetString());
        Assert.Equal("2001-02-03T04:05:06.0000000+00:00", projected.Data.GetProperty("startedAt").GetString());
        var definition = projected.Data.GetProperty("definition");
        Assert.Equal("flow", definition.GetProperty("name").GetString());
        Assert.Equal("tenant", definition.GetProperty("namespace").GetString());
        Assert.Equal("1.2.3", definition.GetProperty("version").GetString());
    }

    private static WorkflowInstanceRecord Instance() => new()
    {
        Id = "run", TenantId = "tenant", DefinitionKey = "flow", DefinitionVersion = "1.2.3",
        CurrentStep = "A", Status = WorkflowStatus.Running, CreatedAt = StartedAt, UpdatedAt = StartedAt,
    };

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2001-02-03T04:06:00Z");
    }

    private sealed class Handler : IWorkflowStepHandler
    {
        public string DefinitionKey => "flow";
        public ValueTask<WorkflowStepOutcome> DecideAsync(WorkflowInstanceRecord instance, WorkflowTrigger trigger, CancellationToken ct = default)
            => ValueTask.FromResult(trigger.Step == "A" ? WorkflowStepOutcome.Advance("B") : WorkflowStepOutcome.Complete("C"));
    }
}
