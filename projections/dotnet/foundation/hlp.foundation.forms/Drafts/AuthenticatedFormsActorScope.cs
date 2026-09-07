using Harborline.Foundation.Authorization;

namespace Harborline.Foundation.Forms.Drafts;

/// <summary>Default adapter resolving tenant, actor, and Party from one validated actor context.</summary>
public sealed class AuthenticatedFormsActorScope : IFormsActorScope
{
    private readonly IAuthenticatedActorContext _actor;
    private readonly IPrincipalPartyResolver _resolver;

    public AuthenticatedFormsActorScope(
        IAuthenticatedActorContext actor,
        IPrincipalPartyResolver resolver)
    {
        _actor = actor ?? throw new ArgumentNullException(nameof(actor));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public async ValueTask<FormsActorScope> GetRequiredAsync(
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_actor.UserId))
        {
            throw PrincipalPartyResolutionException.NoAuthenticatedPrincipal();
        }

        var tenant = _actor.Tenant;
        if (tenant is null)
        {
            throw PrincipalPartyResolutionException.TenantUnresolved();
        }
        if (tenant.Id.IsSystemSentinel)
        {
            throw PrincipalPartyResolutionException.TenantSentinel();
        }

        var partyId = await _resolver
            .ResolveAsync(_actor.UserId, tenant.Id, cancellationToken)
            .ConfigureAwait(false);
        if (partyId is null)
        {
            throw PrincipalPartyResolutionException.NoPartyForPrincipal(_actor.UserId, tenant.Id);
        }

        return new FormsActorScope(tenant.Id, partyId.Value, _actor.UserId);
    }
}
