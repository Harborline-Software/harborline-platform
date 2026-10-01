using System.Reflection;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.ExecutionRuntime;
using Xunit;

namespace Harborline.Foundation.ExecutionRuntime.Tests;

public sealed class ExecutionRuntimeMutationTests
{
    private static readonly TenantId Tenant = new("tenant-a");
    private static readonly RunKind WorkflowRun = new("workflow-run");
    private static readonly CapabilityRetryPolicy ExternalApi = new(
        RetryProfileName.ExternalApiStandard,
        [RetryProfileName.ExternalApiStandard]);

    [Fact(DisplayName = "T-1039 RetryProfile.cs:73: a delay already at its cap is returned without doubling")]
    public void Delay_at_cap_does_not_overflow_when_calculating_the_next_delay()
    {
        var profile = CreateProfile(TimeSpan.MaxValue, TimeSpan.MaxValue);

        Assert.Equal(TimeSpan.MaxValue, profile.DelayAfter(2));
    }

    [Fact(DisplayName = "T-1039 RetryProfile.cs:111: a null allowed collection names the public parameter")]
    public void Null_allowed_collection_names_the_public_parameter()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new CapabilityRetryPolicy(
            RetryProfileName.ImmediateOrFail,
            null!));

        Assert.Equal("allowed", exception.ParamName);
    }

    [Fact(DisplayName = "T-1039 RetryProfile.cs:113: allowed profiles are validated before the default profile")]
    public void Allowed_profiles_are_validated_before_the_default_profile()
    {
        var allowedUnknown = CreateUnknownProfileName("allowed-unknown");
        var defaultUnknown = CreateUnknownProfileName("default-unknown");

        var exception = Assert.Throws<ExecutionRuntimeRefusedException>(() => new CapabilityRetryPolicy(
            defaultUnknown,
            [allowedUnknown]));

        Assert.Equal(ExecutionRuntimeRefusals.RetryProfileUnknown, exception.Code);
        Assert.Contains("'allowed-unknown'", exception.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "T-1039 RunLifecycle.cs:51: caused_by lookup does not capture the caller context")]
    public async Task Caused_by_lookup_does_not_capture_the_caller_context()
    {
        var store = new YieldingRunStore { GetCompletion = NewCompletion<RunRecord?>() };
        var cause = NewPendingRecord();
        await store.CreateAsync(cause);
        var lifecycle = Build(store);
        var context = new RecordingSynchronizationContext();

        var operation = InvokeUnderContext(context, () => lifecycle.StartAsync(
            new StartRun(WorkflowRun, Tenant, ExternalApi, CausedBy: cause.Id)));
        store.GetCompletion.SetResult(cause);
        await operation;

        Assert.Equal(0, context.PostCount);
    }

    [Fact(DisplayName = "T-1039 RunLifecycle.cs:69: creation does not capture the caller context")]
    public async Task Creation_does_not_capture_the_caller_context()
    {
        var store = new YieldingRunStore { CreateCompletion = NewCompletion() };
        var lifecycle = Build(store);
        var context = new RecordingSynchronizationContext();

        var operation = InvokeUnderContext(context, () => lifecycle.StartAsync(
            new StartRun(WorkflowRun, Tenant, ExternalApi)));
        store.CreateCompletion.SetResult();
        await operation;

        Assert.Equal(0, context.PostCount);
    }

    [Fact(DisplayName = "T-1039 RunLifecycle.cs:164: transition lookup does not capture the caller context")]
    public async Task Transition_lookup_does_not_capture_the_caller_context()
    {
        var store = new YieldingRunStore { GetCompletion = NewCompletion<RunRecord?>() };
        var record = NewPendingRecord();
        await store.CreateAsync(record);
        var lifecycle = Build(store);
        var context = new RecordingSynchronizationContext();

        var operation = InvokeUnderContext(context, () => lifecycle.BeginAttemptAsync(Tenant, record.Id));
        store.GetCompletion.SetResult(record);
        await operation;

        Assert.Equal(0, context.PostCount);
    }

    [Fact(DisplayName = "T-1039 RunLifecycle.cs:169: transition update does not capture the caller context")]
    public async Task Transition_update_does_not_capture_the_caller_context()
    {
        var store = new YieldingRunStore { UpdateCompletion = NewCompletion() };
        var record = NewPendingRecord();
        await store.CreateAsync(record);
        var lifecycle = Build(store);
        var context = new RecordingSynchronizationContext();

        var operation = InvokeUnderContext(context, () => lifecycle.BeginAttemptAsync(Tenant, record.Id));
        store.UpdateCompletion.SetResult();
        await operation;

        Assert.Equal(0, context.PostCount);
    }

    private static RetryProfile CreateProfile(TimeSpan initialDelay, TimeSpan maxDelay) =>
        (RetryProfile)typeof(RetryProfile).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(RetryProfileName), typeof(int), typeof(TimeSpan), typeof(TimeSpan)],
            modifiers: null)!.Invoke([RetryProfileName.ImmediateOrFail, 2, initialDelay, maxDelay]);

    private static RetryProfileName CreateUnknownProfileName(string value) =>
        (RetryProfileName)typeof(RetryProfileName).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(string)],
            modifiers: null)!.Invoke([value]);

    private static RunLifecycle Build(IRunStore store)
    {
        var registry = new RunKindRegistry();
        registry.Register(WorkflowRun, "workflows");
        return new RunLifecycle(registry, store, TimeProvider.System);
    }

    private static RunRecord NewPendingRecord() => new()
    {
        Id = RunId.New(WorkflowRun),
        TenantId = Tenant,
        Status = RunStatus.Pending,
        RetryProfile = RetryProfileName.ExternalApiStandard,
        CreatedUtc = DateTimeOffset.UnixEpoch,
        UpdatedUtc = DateTimeOffset.UnixEpoch,
    };

    private static TaskCompletionSource NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<T> NewCompletion<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static Task<RunRecord> InvokeUnderContext(
        SynchronizationContext context,
        Func<ValueTask<RunRecord>> operation)
    {
        var prior = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            return operation().AsTask();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prior);
        }
    }

    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;

        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref _postCount);
            _ = Task.Run(() =>
            {
                var prior = Current;
                SetSynchronizationContext(this);
                try
                {
                    callback(state);
                }
                finally
                {
                    SetSynchronizationContext(prior);
                }
            });
        }
    }

    private sealed class YieldingRunStore : IRunStore
    {
        private readonly Dictionary<(TenantId Tenant, RunId Id), RunRecord> _records = [];

        public TaskCompletionSource? CreateCompletion { get; init; }

        public TaskCompletionSource<RunRecord?>? GetCompletion { get; init; }

        public TaskCompletionSource? UpdateCompletion { get; init; }

        public ValueTask CreateAsync(RunRecord record, CancellationToken cancellationToken = default)
        {
            if (CreateCompletion is { } completion)
            {
                return new ValueTask(completion.Task);
            }

            _records.Add((record.TenantId, record.Id), record);
            return ValueTask.CompletedTask;
        }

        public ValueTask<RunRecord?> GetAsync(TenantId tenantId, RunId id, CancellationToken cancellationToken = default)
        {
            if (GetCompletion is { } completion)
            {
                return new ValueTask<RunRecord?>(completion.Task);
            }

            return ValueTask.FromResult(_records.GetValueOrDefault((tenantId, id)));
        }

        public ValueTask UpdateAsync(RunRecord record, long expectedVersion, CancellationToken cancellationToken = default)
        {
            if (UpdateCompletion is { } completion)
            {
                return new ValueTask(completion.Task);
            }

            _records[(record.TenantId, record.Id)] = record;
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<RunRecord>> ListByStatusAsync(
            TenantId tenantId,
            RunStatus status,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<RunRecord>>(
                _records.Values.Where(record => record.TenantId == tenantId && record.Status == status).ToArray());
    }
}
