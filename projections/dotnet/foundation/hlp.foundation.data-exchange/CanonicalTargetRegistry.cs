namespace Harborline.Foundation.DataExchange;

/// <summary>A registry-resolved canonical Records contract owned by the target domain.</summary>
public sealed record CanonicalRecordsContract(string Contract, string Version);

public interface ICanonicalTargetRegistryPort
{
    ValueTask<CanonicalRecordsContract?> ResolveAsync(string targetContract, CancellationToken cancellationToken = default);
}
