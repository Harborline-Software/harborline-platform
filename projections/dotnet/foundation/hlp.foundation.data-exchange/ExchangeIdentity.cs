using System.Security.Cryptography;
using System.Text;

namespace Harborline.Foundation.DataExchange;

/// <summary>Opaque identity of a dry run.</summary>
public readonly record struct DryRunId(string Value)
{
    /// <summary>Creates a dry run id from a new GUID in N format.</summary>
    public static DryRunId New() => new(Guid.NewGuid().ToString("N"));
}
/// <summary>Opaque identity of a commit run.</summary>
public readonly record struct CommitRunId(string Value);
/// <summary>Opaque batch identity derived from semantic inputs and stable across delivery retries.</summary>
public readonly record struct BatchIdentity(string Value);
/// <summary>Opaque idempotency identity of one effect within a batch.</summary>
public readonly record struct EffectIdempotencyIdentity(string Value);

/// <summary>An infrastructure delivery try with no business semantics.</summary>
public readonly record struct AttemptId(Guid Value)
{
    /// <summary>Creates an attempt id from a new GUID.</summary>
    public static AttemptId New() => new(Guid.NewGuid());
}

/// <summary>The diagnosable semantic preimage of a batch identity.</summary>
public sealed record BatchIdentityInputs(
    string TenantId,
    string DefinitionId,
    string DefinitionVersion,
    string MappingId,
    string MappingVersion,
    string MappingDigest,
    string ConnectorId,
    string ConnectorVersion,
    string SourceFingerprint,
    string InputBoundary,
    string TargetContract,
    string DependencyFingerprint,
    string MappingProfile,
    string TransformVersionsFingerprint,
    string LookupVersionsFingerprint,
    string MatchingInputsFingerprint,
    string SelectedBoundary)
{
    /// <summary>Builds the batch preimage from the tenant and the proposal fingerprint fields.</summary>
    public static BatchIdentityInputs From(string tenantId, ProposalFingerprint proposal) => new(
        tenantId, proposal.DefinitionId, proposal.DefinitionVersion, proposal.MappingId,
        proposal.MappingVersion, proposal.MappingDigest, proposal.ConnectorId, proposal.ConnectorVersion,
        proposal.SourceFingerprint, proposal.InputBoundary, proposal.TargetContract,
        proposal.DependencyFingerprint, proposal.MappingProfile, proposal.TransformVersionsFingerprint,
        proposal.LookupVersionsFingerprint, proposal.MatchingInputsFingerprint, proposal.SelectedBoundary);
}

/// <summary>Derives opaque versioned identities above any delivery retry loop.</summary>
public static class ExchangeIdentity
{
    /// <summary>Derives an hl-batch-v2 identity from the semantic inputs; throws ArgumentNullException for null and ArgumentException when a component is blank or contains the pipe separator.</summary>
    public static BatchIdentity DeriveBatch(BatchIdentityInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        return new($"hl-batch-v2:{Digest(Join(
            "v2",
            inputs.TenantId,
            inputs.DefinitionId,
            inputs.DefinitionVersion,
            inputs.MappingId,
            inputs.MappingVersion,
            inputs.MappingDigest,
            inputs.ConnectorId,
            inputs.ConnectorVersion,
            inputs.SourceFingerprint,
            inputs.InputBoundary,
            inputs.TargetContract,
            inputs.DependencyFingerprint,
            inputs.MappingProfile,
            inputs.TransformVersionsFingerprint,
            inputs.LookupVersionsFingerprint,
            inputs.MatchingInputsFingerprint,
            inputs.SelectedBoundary))}");
    }

    /// <summary>Derives an hl-effect-v1 identity from batch, target contract, source record identity and version and discriminator; throws ArgumentException when a component is blank or contains the pipe separator.</summary>
    public static EffectIdempotencyIdentity DeriveEffect(
        BatchIdentity batch,
        string targetContract,
        string sourceRecordIdentity,
        string sourceRecordVersion,
        string effectDiscriminator)
        => new($"hl-effect-v1:{Digest(Join(
            "v1",
            batch.Value,
            targetContract,
            sourceRecordIdentity,
            sourceRecordVersion,
            effectDiscriminator))}");

    private static string Join(params string[] values)
    {
        if (values.Any(value => string.IsNullOrWhiteSpace(value) || value.Contains('|', StringComparison.Ordinal)))
        {
            throw new ArgumentException("Identity inputs cannot contain the canonical separator.", nameof(values));
        }
        return string.Join('|', values);
    }

    private static string Digest(string preimage)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(preimage))).ToLowerInvariant();
}
