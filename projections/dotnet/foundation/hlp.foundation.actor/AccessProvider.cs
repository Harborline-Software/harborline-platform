namespace Harborline.Foundation.Authorization;

/// <summary>Host adapter to the existing authorization gate. Consumers must never calculate a verdict.</summary>
public interface IAuthorizationDecider
{
    /// <summary>Decides at the supplied instant and returns that decision's evidence.</summary>
    ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The Access-owned set-filter contract shared by Views, report bases, export and dry runs.</summary>
public interface IAccessSetFilter
{
    /// <summary>Binds a predicate to the explicit query identity and instant, without caching decisions.</summary>
    AccessSetPredicate Bind(string operation, string principal, string tenant, string recordKind, DateTimeOffset at);
}

/// <summary>A bound set predicate. Apply it before every count, group, aggregate or page.</summary>
public sealed class AccessSetPredicate
{
    private readonly AccessProvider _access;
    private readonly string _operation, _principal, _tenant, _kind;
    private readonly DateTimeOffset _at;
    internal AccessSetPredicate(AccessProvider access, string operation, string principal, string tenant, string kind, DateTimeOffset at)
        => (_access, _operation, _principal, _tenant, _kind, _at) = (access, operation, principal, tenant, kind, at);

    /// <summary>Checks this record through the same row-check contract as validation.</summary>
    public ValueTask<AccessCheck> CheckAsync(AccessRecord record, CancellationToken cancellationToken = default) =>
        record.Kind != _kind ? ValueTask.FromResult(new AccessCheck(false, "access.record_kind_mismatch"))
            : _access.CheckAsync(new(_operation, _principal, _tenant, record, _at), cancellationToken);

    /// <summary>Produces the complete visible set before consumers aggregate or page it.</summary>
    public async ValueTask<IReadOnlyList<T>> FilterAsync<T>(IEnumerable<T> rows, Func<T, AccessRecord> record,
        CancellationToken cancellationToken = default)
    {
        var visible = new List<T>();
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((await CheckAsync(record(row), cancellationToken).ConfigureAwait(false)).Allowed) visible.Add(row);
        }
        return visible.AsReadOnly();
    }
}

/// <summary>Production set and row providers over the host's sole decider.</summary>
public sealed class AccessProvider(IAuthorizationDecider decider) : IAccessSetFilter
{
    private readonly IAuthorizationDecider _decider = decider ?? throw new ArgumentNullException(nameof(decider));

    /// <inheritdoc />
    public AccessSetPredicate Bind(string operation, string principal, string tenant, string recordKind, DateTimeOffset at) =>
        new(this, operation, principal, tenant, recordKind, at);

    /// <summary>Executes a fresh row check at the predicate instant, never accepting stage-one evidence.</summary>
    public async ValueTask<AccessCheck> CheckAsync(AccessRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.Principal) || string.IsNullOrWhiteSpace(request.Tenant)
            || string.IsNullOrWhiteSpace(request.Operation) || string.IsNullOrWhiteSpace(request.Record.Id)
            || string.IsNullOrWhiteSpace(request.Record.Kind) || request.Tenant != request.Record.Tenant)
            return new(false, "access.context_invalid");
        var evidence = await _decider.DecideAsync(request, cancellationToken).ConfigureAwait(false);
        if (evidence.Principal != request.Principal || evidence.Tenant != request.Tenant || evidence.At != request.At
            || evidence.Operation != request.Operation || evidence.RecordKind != request.Record.Kind || evidence.RecordId != request.Record.Id)
            return new(false, "access.decision_mismatch");
        return new(evidence.Allowed, evidence.Allowed ? "access.allowed" : evidence.Refusal);
    }

    /// <summary>Runs the fresh row check beside validation at exactly the same explicit predicate instant.</summary>
    public async ValueTask<AccessCheck> ValidateAsync(AccessRequest request,
        Func<DateTimeOffset, CancellationToken, ValueTask<AccessCheck>> validation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validation);
        var check = await CheckAsync(request, cancellationToken).ConfigureAwait(false);
        return check.Allowed ? await validation(request.At, cancellationToken).ConfigureAwait(false) : check;
    }
}
