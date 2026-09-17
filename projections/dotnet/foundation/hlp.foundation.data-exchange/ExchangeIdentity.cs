using System.Security.Cryptography;
using System.Text;

namespace Harborline.Foundation.DataExchange;

public readonly record struct DryRunId(string Value)
{
    public static DryRunId New() => new(Guid.NewGuid().ToString("N"));
}
public readonly record struct CommitRunId(string Value);
public readonly record struct BatchIdentity(string Value);
public readonly record struct EffectIdempotencyIdentity(string Value);

/// <summary>An infrastructure delivery try with no business semantics.</summary>
public readonly record struct AttemptId(Guid Value)
{
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
    string TargetContract);

/// <summary>Derives opaque versioned identities above any delivery retry loop.</summary>
public static class ExchangeIdentity
{
    public static BatchIdentity DeriveBatch(BatchIdentityInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        return new($"hl-batch-v1:{Digest(Join(
            "v1",
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
            inputs.TargetContract))}");
    }

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
        if (values.Any(value => value.Contains('|', StringComparison.Ordinal)))
        {
            throw new ArgumentException("Identity inputs cannot contain the canonical separator.", nameof(values));
        }
        return string.Join('|', values);
    }

    private static string Digest(string preimage)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(preimage))).ToLowerInvariant();
}
