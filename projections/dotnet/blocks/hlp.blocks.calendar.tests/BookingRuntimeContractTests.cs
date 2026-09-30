using System.Text.Json;
using System.Text.Json.Nodes;

using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.Calendar.Booking;
using Harborline.Blocks.Calendar.Models;
using Harborline.Kernel.Core;

using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>T-605: the Allocation and hold runtime contracts, which never travel as definitions.</summary>
public sealed class BookingRuntimeContractTests
{
    private static readonly DateTimeOffset Expiry = new(2026, 10, 1, 9, 15, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions SnakeCase = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [Fact(DisplayName = "booking-ck-16: an Allocation is a catalogue record of six members, with no archive namespace, refused as a definition")]
    public void AllocationIsACatalogueRecord()
    {
        var allocation = new BookingAllocation("induction", "learner.7",
            TimeInterval.Of(Expiry, Expiry.AddMinutes(90)), "held", ["instructor", "room"], null);
        var json = JsonSerializer.SerializeToNode(allocation, SnakeCase)!.AsObject();
        Assert.Equal(["bookable_id", "subject_id", "window", "state", "held_resources", "swapped_from_id"], json.Select(member => member.Key));
        Assert.DoesNotContain(Enum.GetNames<DefinitionKind>(), name => name.Contains("Allocation", StringComparison.Ordinal));

        json["kind"] = "allocation";
        json["envelope"] = Fixtures.Envelope();
        foreach (var kind in new[] { DefinitionKind.Resources, DefinitionKind.Bookables })
            Assert.Equal([new DefinitionRefusal(BookingDefinitionCodes.RuntimeDataNotADefinition, "/kind")],
                BookingDefinitionAdmission.Validate(Fixtures.Document(kind, json), Fixtures.Context()));
    }

    [Theory(DisplayName = "booking-ck-22: expiry wins at the kernel commit instant, never the request's start")]
    [InlineData(-1, BookingHoldState.Confirmed, null)]
    [InlineData(0, BookingHoldState.Expired, BookingHoldCodes.Expired)]
    [InlineData(1, BookingHoldState.Expired, BookingHoldCodes.Expired)]
    public void ExpiryWinsAtTheCommitInstant(int commitOffsetTicks, BookingHoldState state, string? refusal)
    {
        // The confirm began before expiry; only the instant read inside the commit decides.
        var outcome = new BookingHold("hold.1", Expiry).Confirm(Expiry.AddTicks(commitOffsetTicks));
        Assert.Equal(state, outcome.Hold.State);
        Assert.Equal(refusal, outcome.Refusal);
    }

    [Fact(DisplayName = "booking-ck-22: a hold reaches exactly one terminal outcome")]
    public void OneTerminalOutcomePerHold()
    {
        var expired = new BookingHold("hold.1", Expiry).Expire(Expiry).Hold;
        Assert.Equal(BookingHoldState.Expired, expired.State);
        var late = expired.Confirm(Expiry.AddMinutes(-1));
        Assert.Equal((BookingHoldState.Expired, BookingHoldCodes.Terminal), (late.Hold.State, late.Refusal));

        var confirmed = new BookingHold("hold.2", Expiry).Confirm(Expiry.AddMinutes(-1)).Hold;
        Assert.Equal((BookingHoldState.Confirmed, BookingHoldCodes.Terminal),
            (confirmed.Expire(Expiry).Hold.State, confirmed.Expire(Expiry).Refusal));
        Assert.Equal(BookingHoldState.Confirmed, confirmed.Cancel().Hold.State);

        var early = new BookingHold("hold.3", Expiry).Expire(Expiry.AddTicks(-1));
        Assert.Equal((BookingHoldState.Held, BookingHoldCodes.NotExpired), (early.Hold.State, early.Refusal));
        Assert.Equal(BookingHoldState.Cancelled, new BookingHold("hold.4", Expiry).Cancel().Hold.State);
    }

    [Fact(DisplayName = "booking-eng-10, booking-eng-18, booking-run-4: confirm reads kernel time inside the transaction, so expiry wins and releases capacity")]
    public async Task ConfirmAtExpiryCommitsExpiredAndReleasesCapacity()
    {
        var hold = new BookingHold("hold.1", Expiry);
        var port = new HoldPort(hold, version: 0);
        var effect = new BookingHoldEffect(new KernelClock(new FixedTimeProvider(Expiry)));

        var result = await effect.ConfirmAsync(hold, expectedVersion: 0, Operation("confirm"), port);

        Assert.Equal((BookingHoldState.Expired, BookingHoldCodes.Expired), (result.Outcome.Hold.State, result.Outcome.Refusal));
        Assert.True(result.Committed);
        Assert.Equal(1, port.Published);
        Assert.Equal(BookingHoldCapacityEffect.Release, port.LastCommit!.CapacityEffect);
        Assert.Equal(BookingHoldState.Expired, port.Current!.State);
    }

    [Fact(DisplayName = "booking-eng-10, booking-run-4: concurrent confirm and expiry at one version produce one terminal outcome and release once after restart")]
    public async Task ConfirmAndExpiryContentionHasOneTerminalOutcomeAndOneRelease()
    {
        var hold = new BookingHold("hold.1", Expiry);
        var port = new HoldPort(hold, version: 0);

        var expiring = new BookingHoldEffect(new KernelClock(new FixedTimeProvider(Expiry)));
        var confirmedAfterExpiry = await expiring.ConfirmAsync(hold, expectedVersion: 0, Operation("confirm"), port);

        // A recreated effect models delayed cleanup/restart. Its stale expiry cannot release the
        // same capacity again, because the transaction port fences the version.
        var afterRestart = new BookingHoldEffect(new KernelClock(new FixedTimeProvider(Expiry)));
        var expiry = await afterRestart.ExpireAsync(hold, expectedVersion: 0, Operation("expire"), port);

        Assert.Equal((BookingHoldState.Expired, BookingHoldCodes.Expired),
            (confirmedAfterExpiry.Outcome.Hold.State, confirmedAfterExpiry.Outcome.Refusal));
        Assert.Equal((BookingHoldState.Expired, BookingHoldCodes.Terminal),
            (expiry.Outcome.Hold.State, expiry.Outcome.Refusal));
        Assert.Equal(1, port.Published);
        Assert.Equal(1, port.Releases);
    }

    [Fact(DisplayName = "booking-eng-10: acquire claims capacity in the same kernel transaction and a retry does not acquire twice")]
    public async Task AcquireIsAtomicAndRetryDoesNotClaimCapacityTwice()
    {
        var hold = new BookingHold("hold.1", Expiry);
        var port = new HoldPort(current: null, version: -1);
        var effect = new BookingHoldEffect(new KernelClock(new FixedTimeProvider(Expiry.AddMinutes(-5))));

        var acquired = await effect.AcquireAsync(hold, Operation("acquire"), port);
        var retry = await effect.AcquireAsync(hold, Operation("acquire-retry"), port);

        Assert.True(acquired.Committed);
        Assert.Equal(BookingHoldState.Held, acquired.Outcome.Hold.State);
        Assert.False(retry.Committed);
        Assert.Equal(BookingHoldCodes.VersionConflict, retry.Outcome.Refusal);
        Assert.Equal(1, port.Published);
        Assert.Equal(1, port.Acquires);
    }

    [Fact(DisplayName = "booking-run-4: a transition the hold refuses at its current version writes nothing, so a terminal hold is never released twice")]
    public async Task CancelOfATerminalHoldAtItsCurrentVersionWritesNothing()
    {
        var cancelled = new BookingHold("hold.1", Expiry) with { State = BookingHoldState.Cancelled };
        var port = new HoldPort(cancelled, version: 3);
        var effect = new BookingHoldEffect(new KernelClock(new FixedTimeProvider(Expiry.AddMinutes(-5))));

        var result = await effect.CancelAsync(cancelled, expectedVersion: 3, Operation("cancel-again"), port);

        Assert.Equal((BookingHoldState.Cancelled, BookingHoldCodes.Terminal), (result.Outcome.Hold.State, result.Outcome.Refusal));
        Assert.False(result.Committed);
        Assert.Equal((0, 0, 3L), (port.Published, port.Releases, port.Version));
    }

    [Fact(DisplayName = "booking-eng-18: an expiry before the kernel expiry instant is refused without a write")]
    public async Task ExpireBeforeExpiryWritesNothing()
    {
        var hold = new BookingHold("hold.1", Expiry);
        var port = new HoldPort(hold, version: 0);
        var effect = new BookingHoldEffect(new KernelClock(new FixedTimeProvider(Expiry.AddMinutes(-5))));

        var result = await effect.ExpireAsync(hold, expectedVersion: 0, Operation("expire-early"), port);

        Assert.Equal((BookingHoldState.Held, BookingHoldCodes.NotExpired), (result.Outcome.Hold.State, result.Outcome.Refusal));
        Assert.False(result.Committed);
        Assert.Equal((0, 0L), (port.Published, port.Version));
    }

    private static BookingHoldOperation Operation(string id) => new(id, id, $"fingerprint-{id}", "actor.1");

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class HoldPort(BookingHold? current, long version) : IBookingHoldTransactionPort
    {
        public BookingHold? Current { get; private set; } = current;
        public long Version { get; private set; } = version;
        public int Published { get; private set; }
        public int Acquires { get; private set; }
        public int Releases { get; private set; }
        public BookingHoldCommit? LastCommit { get; private set; }

        public ValueTask<IKernelPreparedTransaction<BookingHoldCommit, BookingHoldStoreResult>> BeginAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IKernelPreparedTransaction<BookingHoldCommit, BookingHoldStoreResult>>(new Transaction(this));

        private sealed class Transaction(HoldPort owner) : IKernelPreparedTransaction<BookingHoldCommit, BookingHoldStoreResult>
        {
            private BookingHoldCommit? _commit;

            public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

            public ValueTask StageRecordAsync(BookingHoldCommit record, CancellationToken cancellationToken = default)
            {
                _commit = record;
                return ValueTask.CompletedTask;
            }

            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

            public ValueTask<BookingHoldStoreResult> CommitAsync(CancellationToken cancellationToken = default)
            {
                var commit = _commit!;
                if (commit.ExpectedVersion != owner.Version)
                    return ValueTask.FromResult(new BookingHoldStoreResult(false, owner.Current!, owner.Version));
                owner.Current = commit.Hold;
                owner.Version++;
                owner.Published++;
                owner.LastCommit = commit;
                if (commit.CapacityEffect == BookingHoldCapacityEffect.Acquire) owner.Acquires++;
                if (commit.CapacityEffect == BookingHoldCapacityEffect.Release) owner.Releases++;
                return ValueTask.FromResult(new BookingHoldStoreResult(true, owner.Current, owner.Version));
            }

            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
