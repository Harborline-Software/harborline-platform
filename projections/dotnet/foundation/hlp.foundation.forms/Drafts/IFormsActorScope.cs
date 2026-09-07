using Harborline.Foundation.Assets.Common;

namespace Harborline.Foundation.Forms.Drafts;

/// <summary>The tenant, Party, and actor derived atomically from one authenticated principal.</summary>
public sealed record FormsActorScope(TenantId Tenant, Guid PartyId, string ActorId);

/// <summary>
/// Fail-closed current-actor seam for Forms state. Hosts may replace the default authenticated
/// adapter, but callers cannot inject tenant and Party through independently mutable contexts.
/// </summary>
public interface IFormsActorScope
{
    ValueTask<FormsActorScope> GetRequiredAsync(CancellationToken cancellationToken = default);
}
