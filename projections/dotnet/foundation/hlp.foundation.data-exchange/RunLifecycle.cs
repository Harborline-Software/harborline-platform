namespace Harborline.Foundation.DataExchange;

public sealed record RunRetention(DateTimeOffset RetainUntil, bool LegalHold);

/// <summary>
/// Trusted tenant/platform policy derives retention from a class identifier and run creation time.
/// Unknown classes must refuse. Disposition adapters must check current policy and legal holds
/// atomically with deletion; CanDispose methods are eligibility checks, not deletion permits.
/// </summary>
public interface IRunLifecyclePolicyPort
{
    ValueTask<RunRetention> DeriveAsync(string tenantId, string retentionClass,
        DateTimeOffset requestedAt, CancellationToken cancellationToken = default);
}
