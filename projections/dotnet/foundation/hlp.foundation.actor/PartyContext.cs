namespace Harborline.Foundation.Authorization;

/// <summary>
/// Fail-closed facade that resolves the current actor's server-derived Party identifier.
/// </summary>
public sealed class PartyContext : IPartyContext
{
    private readonly IAuthenticatedActorContext _actor;
    private readonly IPrincipalPartyResolver _resolver;

    /// <summary>Creates a Party context over one authenticated actor scope and one host resolver.</summary>
    public PartyContext(IAuthenticatedActorContext actor, IPrincipalPartyResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(resolver);
        _actor = actor;
        _resolver = resolver;
    }

    /// <inheritdoc />
    public async ValueTask<Guid> GetCurrentPartyIdAsync(
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

        return partyId
            ?? throw PrincipalPartyResolutionException.NoPartyForPrincipal(
                _actor.UserId,
                tenant.Id);
    }
}
