using Harborline.Foundation.Authorization;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Engine.Capabilities;
using Harborline.Foundation.MultiTenancy;

namespace Harborline.Foundation.Forms.Engine;

/// <summary>
/// Resolves one current host actor, Party, and verified bearer capability into the Forms scope.
/// Bearer roles are never observed; current host-derived roles are snapshotted on every call.
/// </summary>
public sealed class CurrentFormExecutionContextProvider : IFormExecutionContextProvider
{
    private readonly IAuthenticatedActorContext _actor;
    private readonly IPrincipalPartyResolver _parties;
    private readonly IFormCapabilityBearerProvider _bearers;
    private readonly IFormCapabilityVerifier _capabilities;
    private readonly TimeProvider _clock;

    /// <summary>Creates the scoped production context provider.</summary>
    public CurrentFormExecutionContextProvider(
        IAuthenticatedActorContext actor,
        IPrincipalPartyResolver parties,
        IFormCapabilityBearerProvider bearers,
        IFormCapabilityVerifier capabilities,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(parties);
        ArgumentNullException.ThrowIfNull(bearers);
        ArgumentNullException.ThrowIfNull(capabilities);
        _actor = actor;
        _parties = parties;
        _bearers = bearers;
        _capabilities = capabilities;
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async ValueTask<FormExecutionScope> GetRequiredAsync(
        FormEngineAction action,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Enum.IsDefined(action)) throw new FormEngineDeniedException();
            var before = Capture(_actor);
            var bearer = await _bearers.GetBearerAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(bearer)) throw new FormEngineDeniedException();

            var capability = await _capabilities
                .VerifyAsync(bearer, _clock.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
            if (capability.Tenant != before.Tenant
                || !string.Equals(capability.Subject, before.ActorId, StringComparison.Ordinal)
                || !capability.Actions.Contains(RequiredCapability(action)))
                throw new FormEngineDeniedException();

            var partyId = await _parties
                .ResolveAsync(before.ActorId, before.Tenant, cancellationToken)
                .ConfigureAwait(false);
            if (partyId is null || partyId == Guid.Empty) throw new FormEngineDeniedException();
            if (_clock.GetUtcNow() > capability.ExpiresAt) throw new FormEngineDeniedException();
            return new(before.Tenant, partyId.Value, before.ActorId, before.Roles);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FormExecutionTenantUnavailableException)
        {
            throw;
        }
        catch (FormEngineDeniedException)
        {
            throw;
        }
        catch (FormCapabilityDeniedException)
        {
            throw new FormEngineDeniedException();
        }
        catch
        {
            throw new FormEngineDeniedException();
        }
    }

    private static ActorSnapshot Capture(IAuthenticatedActorContext actor)
    {
        var actorId = actor.UserId;
        var tenant = actor.Tenant;
        var currentRoles = actor.Roles;
        if (tenant is null || tenant.Id.IsSystemSentinel || tenant.Status != TenantStatus.Active)
            throw new FormExecutionTenantUnavailableException();
        if (string.IsNullOrWhiteSpace(actorId) || currentRoles is null)
            throw new FormEngineDeniedException();
        if (currentRoles.Any(string.IsNullOrWhiteSpace)) throw new FormEngineDeniedException();
        var roles = currentRoles
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new(tenant.Id, actorId, roles);
    }

    private static FormCapabilityAction RequiredCapability(FormEngineAction action) => action switch
    {
        FormEngineAction.Read => FormCapabilityAction.Read,
        FormEngineAction.Validate or FormEngineAction.Submit => FormCapabilityAction.Write,
        _ => throw new FormEngineDeniedException(),
    };

    private sealed record ActorSnapshot(TenantId Tenant, string ActorId, IReadOnlyList<string> Roles);
}
