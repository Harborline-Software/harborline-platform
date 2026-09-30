namespace Harborline.Foundation.DataExchange;

/// <summary>Retain-until date and legal-hold flag for a run.</summary>
public sealed record RunRetention(DateTimeOffset RetainUntil, bool LegalHold);

/// <summary>Derives current retention and legal-hold policy for exchange runs.</summary>
public interface IRunLifecyclePolicyPort
{
    /// <summary>Returns the current retain-until date and legal hold for the tenant's retention class, evaluated at the request time.</summary>
    ValueTask<RunRetention> DeriveAsync(string tenantId, string retentionClass,
        DateTimeOffset requestedAt, CancellationToken cancellationToken = default);
}
