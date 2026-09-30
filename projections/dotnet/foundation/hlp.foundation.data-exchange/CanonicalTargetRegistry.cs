namespace Harborline.Foundation.DataExchange;

/// <summary>A registry-resolved canonical Records contract owned by the target domain.</summary>
public sealed record CanonicalRecordsContract(string Contract, string Version);

/// <summary>Resolves the versioned canonical Records contract that owns a target.</summary>
public interface ICanonicalTargetRegistryPort
{
    /// <summary>Returns the contract registered for the target contract name, or null when none is registered (the committer refuses null as target.contract_unregistered).</summary>
    ValueTask<CanonicalRecordsContract?> ResolveAsync(string targetContract, CancellationToken cancellationToken = default);
}
