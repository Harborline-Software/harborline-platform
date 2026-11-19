using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Foundation.ExecutionRuntime.Tests;

public sealed class RetryTimingTests
{
    private static readonly TenantId Tenant = new("retry-tenant");
    private static readonly RunKind Kind = new("retry-run");
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Theory(DisplayName = "execution-runtime-ck-9: fixed full jitter halves capped doubling delays")]
    [InlineData("external-api-standard", 2, 1, 2)]
    [InlineData("durable-delivery", 9, 15, 1800)]
    public async Task Fixed_jitter(string profile, int retries, int firstSeconds, int lastSeconds)
    {
        // Oracle: slice acceptance literals. Mutation: wait the full capped delay.
        var random = new FixedRandom(0.5);
        var (lifecycle, _, clock, run) = await Start(profile, random);
        for (var attempt = 1; attempt <= retries; attempt++)
        {
            await lifecycle.BeginAttemptAsync(Tenant, run.Id);
            run = await lifecycle.FailAttemptAsync(Tenant, run.Id, new RunFailure("transient", true));
            if (attempt == 1) Assert.Equal(clock.Now.AddSeconds(firstSeconds), run.NextAttemptDueUtc);
            if (attempt == retries) Assert.Equal(clock.Now.AddSeconds(lastSeconds), run.NextAttemptDueUtc);
            Assert.Null(run.Attempts[^1].ServerToldWait);
            clock.Now = run.NextAttemptDueUtc!.Value;
        }
        Assert.Equal(retries, random.Draws);
    }

    [Theory(DisplayName = "execution-runtime-ck-9: seeded draws match every retry and floor fractional ticks")]
    [InlineData("external-api-standard")]
    [InlineData("durable-delivery")]
    public async Task Seeded_jitter(string profile)
    {
        // Oracle: independent seeded RNG and literal capped sequences from slice/ADR-0118, never production output.
        // Mutation: reuse the first random draw (or round fractional ticks up).
        var seconds = profile == "external-api-standard" ? new[] { 2, 4 } : [30, 60, 120, 240, 480, 960, 1920, 3600, 3600];
        var expectedRandom = new Random(528);
        var (lifecycle, _, clock, run) = await Start(profile, new Random(528));
        foreach (var ceiling in seconds)
        {
            await lifecycle.BeginAttemptAsync(Tenant, run.Id);
            var failedAt = clock.Now;
            run = await lifecycle.FailAttemptAsync(Tenant, run.Id, new RunFailure("transient", true));
            var ticks = (long)(expectedRandom.NextDouble() * (ceiling * 10_000_000L));
            Assert.Equal(failedAt.AddTicks(ticks), run.NextAttemptDueUtc);
            clock.Now = run.NextAttemptDueUtc!.Value;
            Assert.InRange((clock.Now - failedAt).Ticks, 0, ceiling * 10_000_000L - 1);
        }
    }

    [Fact(DisplayName = "execution-runtime-ck-9: zero jitter permits an immediate retry")]
    public async Task Zero_jitter()
    {
        // Oracle: slice acceptance zero boundary. Mutation: impose a positive minimum delay.
        var (lifecycle, _, _, run) = await Start("external-api-standard", new FixedRandom(0));
        await lifecycle.BeginAttemptAsync(Tenant, run.Id);
        run = await lifecycle.FailAttemptAsync(Tenant, run.Id, new RunFailure("transient", true));
        Assert.Equal(Now, run.NextAttemptDueUtc);
        Assert.Equal(2, (await lifecycle.BeginAttemptAsync(Tenant, run.Id)).Attempts.Count);
    }

    [Fact(DisplayName = "execution-runtime-ck-9 execution-runtime-ck-10: dead letters draw nothing and record no server wait")]
    public async Task Dead_letters_do_not_draw()
    {
        // Oracle: slice single-attempt/non-retryable rules. Mutation: draw or record before deciding to retry.
        foreach (var profile in new[] { "immediate-or-fail", "manual-reconciliation", "external-api-standard", "durable-delivery" })
        foreach (var retryable in new[] { false, true })
        {
            var random = new FixedRandom(0.5);
            var (lifecycle, _, clock, run) = await Start(profile, random);
            var count = retryable ? profile switch { "external-api-standard" => 3, "durable-delivery" => 10, _ => 1 } : 1;
            for (var attempt = 1; attempt <= count; attempt++)
            {
                await lifecycle.BeginAttemptAsync(Tenant, run.Id);
                run = await lifecycle.FailAttemptAsync(Tenant, run.Id, new RunFailure("failure", retryable, new RetryAfter(TimeSpan.FromDays(10))));
                if (run.NextAttemptDueUtc is { } due) clock.Now = due;
            }
            Assert.Equal(RunStatus.DeadLettered, run.Status);
            Assert.Equal(retryable ? DeadLetterReason.RetriesExhausted : DeadLetterReason.NotRetryable, run.DeadLetter!.Reason);
            Assert.Null(run.Attempts[^1].ServerToldWait);
            Assert.Null(run.NextAttemptDueUtc);
            Assert.Equal(count - 1, random.Draws);
        }
    }

    [Theory(DisplayName = "execution-runtime-ck-10: both Retry-After forms lengthen within the cap and retain the uncapped wait")]
    [InlineData("external-api-standard", 10, 10)]
    [InlineData("external-api-standard", 120, 30)]
    [InlineData("durable-delivery", 864000, 3600)]
    public async Task Server_wait_and_cap(string profile, int toldSeconds, int expectedSeconds)
    {
        // Oracle: slice literals and RFC 9110 10.2.3. Mutations: drop cap; resolve date against attempt start; cap the record.
        foreach (var instant in new[] { false, true })
        {
            var (lifecycle, store, clock, run) = await Start(profile, new FixedRandom(0.5));
            await lifecycle.BeginAttemptAsync(Tenant, run.Id);
            clock.Now = Now.AddSeconds(7);
            var told = instant ? new RetryAfter(clock.Now.AddSeconds(toldSeconds)) : new RetryAfter(TimeSpan.FromSeconds(toldSeconds));
            Assert.Equal(instant, told.Date.HasValue);
            Assert.Equal(!instant, told.Delta.HasValue);
            run = await lifecycle.FailAttemptAsync(Tenant, run.Id, new RunFailure("busy", true, told));
            Assert.Equal(Now.AddSeconds(7 + expectedSeconds), run.NextAttemptDueUtc);
            Assert.Equal(TimeSpan.FromSeconds(toldSeconds), run.Attempts[^1].ServerToldWait);
            var stored = (await store.GetAsync(Tenant, run.Id))!;
            Assert.Equal(TimeSpan.FromSeconds(toldSeconds), stored.Attempts[^1].ServerToldWait);
            Assert.Equal(told, stored.Attempts[^1].Failure!.RetryAfter);
        }
    }

    [Theory(DisplayName = "execution-runtime-ck-10: absent shorter equal zero and past waits never shorten jitter or record a wait")]
    [InlineData(-2)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(1)]
    public async Task Server_wait_never_shortens(double seconds)
    {
        // Oracle: slice strict-longer rule. Mutations: replace longer jitter; record an equal wait.
        foreach (var instant in new[] { false, true })
        {
            var (lifecycle, _, clock, run) = await Start("external-api-standard", new FixedRandom(0.5));
            await lifecycle.BeginAttemptAsync(Tenant, run.Id);
            clock.Now = Now.AddSeconds(7);
            RetryAfter? told = seconds == -2 ? null : instant ? new RetryAfter(clock.Now.AddSeconds(seconds))
                : seconds == -1 ? default(RetryAfter) : new RetryAfter(TimeSpan.FromSeconds(seconds));
            run = await lifecycle.FailAttemptAsync(Tenant, run.Id, new RunFailure("busy", true, told));
            Assert.Equal(Now.AddSeconds(8), run.NextAttemptDueUtc);
            Assert.Null(run.Attempts[^1].ServerToldWait);
        }
    }

    [Fact(DisplayName = "execution-runtime-ck-10: negative durations refuse at construction")]
    public void Negative_duration_refused()
    {
        // Oracle: RFC 9110 10.2.3 non-negative delay. Mutation: remove negative-duration guard.
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryAfter(TimeSpan.FromTicks(-1)));
        Assert.Equal(TimeSpan.Zero, new RetryAfter(TimeSpan.Zero).Delta);
    }

    private static async Task<(RunLifecycle Lifecycle, InMemoryRunStore Store, Clock Clock, RunRecord Run)> Start(string profile, Random random)
    {
        var registry = new RunKindRegistry();
        registry.Register(Kind, "tests");
        var store = new InMemoryRunStore();
        var clock = new Clock();
        var lifecycle = new RunLifecycle(registry, store, clock, random);
        var name = RetryProfileName.Parse(profile);
        var run = await lifecycle.StartAsync(new StartRun(Kind, Tenant, new CapabilityRetryPolicy(name, [name])));
        return (lifecycle, store, clock, run);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = RetryTimingTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

internal sealed class FixedRandom(double value) : Random
{
    public int Draws { get; private set; }
    public override double NextDouble()
    {
        Draws++;
        return value;
    }
}
