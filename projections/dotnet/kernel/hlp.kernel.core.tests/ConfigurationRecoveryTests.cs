using System.Security.Cryptography;
using System.Text.Json;
using Harborline.Kernel.Core;
using Xunit;

namespace Harborline.Kernel.Core.Tests;

public sealed class ConfigurationRecoveryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2030-01-02T03:04:05Z");
    private static readonly byte[] Content = "effective-generation"u8.ToArray();
    private static readonly string Digest = Convert.ToHexStringLower(SHA256.HashData(Content));

    private static ConfigurationRecoveryRequest Request(string reason = "Node crashed during activation", string snapshot = "{\"grants\":[\"node-operator\"]}") =>
        new("recovery-1", "tenant-a", "operator-1", reason, snapshot);

    private static KernelProfileSnapshot Profile(
        EffectivePointer? pointer = null,
        ReadOnlyMemory<byte>? content = null,
        PreparedGenerationResidue? prepared = null,
        IReadOnlyList<EvidenceOutboxEntry>? outbox = null,
        string tenant = "tenant-a") =>
        new(tenant, pointer ?? new(Digest, "intent-1"), content ?? Content, prepared, outbox ?? []);

    private static ConfigurationRecovery Recovery(Host host, bool capability = true) =>
        new(host, capability ? host : null, new KernelClock(new FixedTimeProvider(Now)));

    [Fact]
    public void TheKernelProfileDeclaresTheConfigurationRecoveryCapability()
    {
        Assert.Contains("configuration-recovery", KernelProfile.Capabilities);
        Assert.Equal(KernelProfile.ConfigurationRecovery, Assert.Single(KernelProfile.Capabilities));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("deactivated")]
    [InlineData("corrupt")]
    public async Task RecoverySucceedsWhateverStateTheConfigurationManagementPackageIsIn(string packageState)
    {
        var host = new Host(Profile(prepared: new("candidate", "projection-1")), packageState);
        var result = await Recovery(host).RecoverAsync(Request(), host);

        Assert.True(result.Committed);
        Assert.Equal(Digest, result.Record!.EffectiveDigest);
        Assert.Equal(1, host.Published);
        Assert.Equal(0, host.CatalogueReads);
        Assert.Equal(1, host.ProfileReads);
    }

    [Fact]
    public async Task RecoveryResolvesNoReleasedDefinitionAndReadsNoPackCatalogueToEstablishAuthority()
    {
        var host = new Host(Profile(), "corrupt");
        var result = await Recovery(host).RecoverAsync(Request(), host);

        Assert.True(result.Committed);
        Assert.Equal(0, host.CatalogueReads);
        Assert.Equal(["capability:operator-1:tenant-a", "profile:tenant-a"], host.AuthorityReads);
        var kernel = typeof(ConfigurationRecovery).Assembly.GetReferencedAssemblies().Select(name => name.Name!);
        Assert.DoesNotContain(kernel, name => name.StartsWith("Harborline.", StringComparison.Ordinal));
        var seams = typeof(ConfigurationRecovery).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType);
        Assert.DoesNotContain(typeof(IKernelCatalogueReader), seams);
    }

    [Theory]
    [InlineData("pointer-missing", KernelRecoveryErrors.EffectiveGenerationMissing)]
    [InlineData("content-missing", KernelRecoveryErrors.EffectiveGenerationMissing)]
    [InlineData("content-digest-mismatch", KernelRecoveryErrors.EffectiveGenerationCorrupt)]
    [InlineData("profile-absent", KernelRecoveryErrors.EffectiveGenerationMissing)]
    public async Task MissingOrCorruptEffectiveGenerationFailsClosedNamingTheState(string state, string code)
    {
        var profile = state switch
        {
            "pointer-missing" => Profile(pointer: new("", "intent-1")),
            "content-missing" => new KernelProfileSnapshot("tenant-a", new(Digest, "intent-1"), null, null, []),
            "content-digest-mismatch" => Profile(content: "tampered"u8.ToArray()),
            _ => null,
        };
        var host = new Host(profile, "missing");
        var result = await Recovery(host).RecoverAsync(Request(), host);

        Assert.False(result.Committed);
        Assert.Equal(code, result.Refusal!.Code);
        Assert.Equal(state, result.Refusal.State);
        Assert.Null(result.Record);
        Assert.Empty(host.Events);
    }

    [Fact]
    public async Task PreparedGenerationLeftByACrashIsAbandonedAndNeverActivated()
    {
        var host = new Host(Profile(prepared: new("candidate-digest", "projection-1")), "missing");
        var result = await Recovery(host).RecoverAsync(Request(), host);

        var repair = Assert.Single(result.Record!.Repairs, repair => repair.Residue == ConfigurationResidue.PreparedGeneration);
        Assert.Equal(ConfigurationTerminalState.Abandoned, repair.Terminal);
        Assert.Equal("candidate-digest", repair.Identity);
        Assert.Equal(Digest, result.Record.EffectiveDigest);
        Assert.Equal(Digest, host.PublishedSet!.Value.Record.EffectiveDigest);
    }

    [Fact]
    public async Task EffectivePointerBoundToACommittedIntentIsConfirmedWithoutMoving()
    {
        var host = new Host(Profile(pointer: new(Digest, "intent-7")), "missing");
        var result = await Recovery(host).RecoverAsync(Request(), host);

        var repair = Assert.Single(result.Record!.Repairs);
        Assert.Equal(ConfigurationResidue.EffectivePointer, repair.Residue);
        Assert.Equal(ConfigurationTerminalState.Confirmed, repair.Terminal);
        Assert.Equal(Digest, repair.Identity);
    }

    [Fact]
    public async Task EffectivePointerWithoutAnEvidenceIntentFailsClosed()
    {
        var host = new Host(Profile(pointer: new(Digest, null)), "missing");
        var result = await Recovery(host).RecoverAsync(Request(), host);

        Assert.Equal(KernelRecoveryErrors.EffectivePointerUnbound, result.Refusal!.Code);
        Assert.Equal("pointer-without-evidence-intent", result.Refusal.State);
        Assert.Empty(host.Events);
    }

    [Fact]
    public async Task UnpublishedEvidenceOutboxEntriesArePublishedAndPublishedOnesAreLeftAlone()
    {
        var host = new Host(Profile(outbox: [new("intent-1", true), new("intent-2", false)]), "missing");
        var result = await Recovery(host).RecoverAsync(Request(), host);

        var repair = Assert.Single(result.Record!.Repairs, repair => repair.Residue == ConfigurationResidue.EvidenceOutbox);
        Assert.Equal("intent-2", repair.Identity);
        Assert.Equal(ConfigurationTerminalState.Published, repair.Terminal);
    }

    [Theory]
    [InlineData("", "snapshot", KernelRecoveryErrors.ReasonRequired, "reason-absent")]
    [InlineData("reason", " ", KernelRecoveryErrors.AuthoritySnapshotRequired, "authority-snapshot-absent")]
    public async Task RecoveryWithoutAReasonOrAuthoritySnapshotIsRefused(string reason, string snapshot, string code, string state)
    {
        var host = new Host(Profile(), "missing");
        var result = await Recovery(host).RecoverAsync(Request(reason, snapshot), host);

        Assert.Equal(code, result.Refusal!.Code);
        Assert.Equal(state, result.Refusal.State);
        Assert.Empty(host.Events);
        Assert.Equal(0, host.ProfileReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryWithoutTheConstrainedCapabilityIsRefusedByName(bool capabilitySupplied)
    {
        var host = new Host(Profile(), "missing") { Admit = false };
        var result = await Recovery(host, capabilitySupplied).RecoverAsync(Request(), host);

        Assert.Equal(KernelRecoveryErrors.CapabilityRequired, result.Refusal!.Code);
        Assert.Equal("configuration-recovery", result.Refusal.State);
        Assert.Empty(host.Events);
        Assert.Equal(0, host.ProfileReads);
    }

    [Fact]
    public async Task TenantOfTheProfileMustBeTheTenantOfTheRequest()
    {
        var host = new Host(Profile(tenant: "tenant-b"), "missing");
        var result = await Recovery(host).RecoverAsync(Request(), host);

        Assert.Equal(KernelRecoveryErrors.TenantMismatch, result.Refusal!.Code);
        Assert.Empty(host.Events);
    }

    [Fact]
    public async Task EveryRecoveryCommitsItsReasonAndAuthoritySnapshotInOneSetWithTheRecord()
    {
        var host = new Host(Profile(prepared: new("candidate", "projection-1"), outbox: [new("intent-2", false)]), "missing");
        var result = await Recovery(host).RecoverAsync(Request(), host);

        Assert.Equal(["begin", "record", "audit", "commit", "dispose"], host.Events);
        var (operation, record, audit) = host.PublishedSet!.Value;
        Assert.Equal("recovery-1", operation.CommandId);
        Assert.Same(result.Record, record);
        Assert.Equal("Node crashed during activation", record.Reason);
        Assert.Equal("{\"grants\":[\"node-operator\"]}", record.AuthoritySnapshot);
        Assert.Equal("operator-1", audit.ActorId);
        Assert.Equal(Now, audit.RecordedAt);
        using var payload = JsonDocument.Parse(audit.Payload);
        Assert.Equal("configuration-recovery", payload.RootElement.GetProperty("capability").GetString());
        Assert.Equal("Node crashed during activation", payload.RootElement.GetProperty("record").GetProperty("reason").GetString());
        Assert.Equal("{\"grants\":[\"node-operator\"]}", payload.RootElement.GetProperty("record").GetProperty("authoritySnapshot").GetString());
        Assert.Equal(3, payload.RootElement.GetProperty("record").GetProperty("repairs").GetArrayLength());
    }

    [Fact]
    public async Task CommitFaultRollsBackAndPublishesNoRecovery()
    {
        var host = new Host(Profile(), "missing", fault: "commit");
        await Assert.ThrowsAsync<InjectedFault>(async () => await Recovery(host).RecoverAsync(Request(), host));

        Assert.Contains("rollback", host.Events);
        Assert.Equal(0, host.Published);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InjectedFault : Exception { }

    /// <summary>One host holding the kernel profile, a pack catalogue in the given state and the recovery transaction port.</summary>
    private sealed class Host(KernelProfileSnapshot? profile, string packageState, string? fault = null)
        : IKernelProfileReader, IKernelCatalogueReader, IKernelConfigurationRecoveryCapability,
          IKernelTransactionPort<ConfigurationRecoveryRecord, string>
    {
        public bool Admit { get; init; } = true;
        public int ProfileReads { get; private set; }
        public int CatalogueReads { get; private set; }
        public List<string> AuthorityReads { get; } = [];
        public List<string> Events { get; } = [];
        public int Published { get; private set; }
        public (KernelOperationIdentity Operation, ConfigurationRecoveryRecord Record, KernelAuditEvidence Audit)? PublishedSet { get; private set; }

        public ValueTask<KernelProfileSnapshot?> ReadAsync(string tenantKey, CancellationToken cancellationToken = default)
        {
            ProfileReads++;
            AuthorityReads.Add($"profile:{tenantKey}");
            return ValueTask.FromResult(profile);
        }

        public ValueTask<CompiledBootstrapShape?> ReadAsync(CompiledShapeIdentity identity, CancellationToken cancellationToken = default)
        {
            CatalogueReads++;
            AuthorityReads.Add($"catalogue:{identity}");
            return packageState switch
            {
                "missing" => ValueTask.FromResult<CompiledBootstrapShape?>(null),
                "deactivated" => ValueTask.FromResult<CompiledBootstrapShape?>(new(identity, "Configuration Management (deactivated)", 0)),
                _ => throw new InvalidDataException("configuration-management-package-corrupt"),
            };
        }

        public ValueTask<bool> CanRecoverAsync(string actorId, string tenantKey, CancellationToken cancellationToken = default)
        {
            AuthorityReads.Add($"capability:{actorId}:{tenantKey}");
            return ValueTask.FromResult(Admit);
        }

        public ValueTask<IKernelTransaction<ConfigurationRecoveryRecord, string>> BeginAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default)
        {
            Events.Add("begin");
            return ValueTask.FromResult<IKernelTransaction<ConfigurationRecoveryRecord, string>>(new Transaction(this, operation, fault));
        }

        private sealed class Transaction(Host owner, KernelOperationIdentity operation, string? fault)
            : IKernelTransaction<ConfigurationRecoveryRecord, string>
        {
            private ConfigurationRecoveryRecord? _record;
            private KernelAuditEvidence? _audit;

            public ValueTask StageRecordAsync(ConfigurationRecoveryRecord record, CancellationToken cancellationToken = default)
            {
                owner.Events.Add("record");
                _record = record;
                return ValueTask.CompletedTask;
            }

            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default)
            {
                owner.Events.Add("audit");
                _audit = audit;
                return ValueTask.CompletedTask;
            }

            public ValueTask<string> CommitAsync(CancellationToken cancellationToken = default)
            {
                owner.Events.Add("commit");
                if (fault == "commit") throw new InjectedFault();
                owner.PublishedSet = (operation, _record!, _audit!);
                owner.Published++;
                return ValueTask.FromResult("committed");
            }

            public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
            {
                owner.Events.Add("rollback");
                return ValueTask.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                owner.Events.Add("dispose");
                return ValueTask.CompletedTask;
            }
        }
    }
}
